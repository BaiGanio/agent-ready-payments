using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Payments.Api.Auth;
using Payments.Api.Storage;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>One Postgres container and one migrated schema for the whole test run.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;
    private NpgsqlConnection _conn = null!;
    private Respawner _respawner = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string ConnectionString => _pg.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        _pg = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("payments_labs")
            .WithUsername("labs")
            .WithPassword("labs")
            .Build();

        await _pg.StartAsync();

        Migrate(_pg.GetConnectionString());

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Postgres", _pg.GetConnectionString()));

        _conn = new NpgsqlConnection(_pg.GetConnectionString());
        await _conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(_conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["schemaversions", "merchant"]
        });
    }
    /// <summary>Deletes every row except the seeded merchants. Keeps the schema.</summary>
    public Task ResetAsync() => _respawner.ResetAsync(_conn);

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }
    private void Migrate(string connectionString)
    {
        string scripts = Path.Combine(RepoRoot(), "db", "postgres");

        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsFromFileSystem(scripts)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException("Migrations failed.", result.Error);

    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("Payments.slnx").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Payments.slnx in any parent directory.");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

/// <summary>Base class for API tests. Empties the tables before every test.</summary>
[Collection("api")]
public abstract class ApiTestBase(ApiFixture fixture) : IAsyncLifetime
{
    protected ApiFixture Fixture { get; } = fixture;
    protected HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Fixture.ResetAsync();

        // Respawn empties api_key before every test, so the default key is inserted every time.
        Client = await ClientWith(
            ["payments:read", "payments:write", "payments:refund"],
            kind: "service",
            merchant: SeededMerchant);
    }

    public ValueTask DisposeAsync()
    {
        Client?.Dispose();
        return ValueTask.CompletedTask;
    }

      /// <summary>The first seeded merchant. Every happy-path test uses it.</summary>
    protected static readonly Guid SeededMerchant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>The second seeded merchant. A key for one merchant must never see the other's payments.</summary>
    protected static readonly Guid OtherMerchant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Inserts a key straight into api_key and returns the raw key. Only the hash is stored,
    /// so the raw key lives only in the test. Defaults to an agent key for the seeded merchant.
    /// </summary>
    protected async Task<string> InsertKeyAsync(string[] scopes, string kind = "agent", Guid? merchant = null)
    {
        string raw = Guid.NewGuid().ToString("N");

        await ExecuteSqlAsync(
            """
            insert into api_key (key_hash, name, merchant_id, kind, scopes)
            values (@Hash, @Name, @Merchant, @Kind, @Scopes)
            """,
            new { Hash = ApiKeyHash.Of(raw), Name = raw, Merchant = merchant ?? SeededMerchant, Kind = kind, Scopes = scopes });

        return raw;
    }

    /// <summary>A new client that sends a fresh key with these scopes on every request.</summary>
    protected async Task<HttpClient> ClientWith(string[] scopes, string kind = "agent", Guid? merchant = null)
    {
        var client = Fixture.Factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Api-Key", await InsertKeyAsync(scopes, kind, merchant));

        return client;
    }

    /// <summary>
    /// Posts a valid body and returns the created payment. Fails the test if the API did not return 201.
    /// Always sends an Idempotency-Key. Pass <paramref name="key"/> to reuse one on purpose.
    /// </summary>
    protected async Task<PaymentDto> CreatePaymentAsync(decimal amount = 100m, string currency = "EUR", string? key = null)
    {
        var response = await PostAsync("/payments", new CreatePaymentRequest(SeededMerchant, amount, currency), key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);

        return dto!;
    }

    /// <summary>
    /// Posts to any path with an Idempotency-Key and returns the raw response, so a test can assert a 409.
    /// A fresh key every call unless the test names one. PostAsJsonAsync cannot add a header, so this
    /// builds the request by hand. Pass <paramref name="client"/> to post with another API key.
    /// </summary>
    protected async Task<HttpResponseMessage> PostAsync(string path, object? body = null, string? key = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());

        return await (client ?? Client).SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a payment and authorises it. Fails the test if either call did not succeed.</summary>
    protected async Task<PaymentDto> AuthorisedPaymentAsync(decimal amount = 100m, string currency = "EUR")
    {
        var created = await CreatePaymentAsync(amount, currency);

        var response = await PostAsync($"/payments/{created.Id}/authorise");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Creates, authorises and captures a payment. <paramref name="captured"/> defaults to the full amount,
    /// so the payment ends Captured; pass less for a PartiallyCaptured one.
    /// </summary>
    protected async Task<PaymentDto> CapturedPaymentAsync(decimal amount = 100m, decimal? captured = null, string currency = "EUR")
    {
        var authorised = await AuthorisedPaymentAsync(amount, currency);

        var response = await PostAsync(
            $"/payments/{authorised.Id}/capture",
            new AmountRequest(captured ?? amount, currency));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Reads the "code" extension out of a ProblemDetails body.</summary>
    protected static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);

        return problem!.Extensions["code"] is JsonElement code ? code.GetString() : null;
    }

    /// <summary>Counts the rows in one table, straight from Postgres. Bypasses the API on purpose.</summary>
    protected async Task<long> CountRowsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);

        return await connection.ExecuteScalarAsync<long>($"select count(*) from {table}");
    }

     /// <summary>Builds a store by engine name. The only place a test names an engine.</summary>
    protected IPaymentStore StoreFor(string engine) => engine switch
    {
        "dapper" => new DapperPaymentStore(Fixture.ConnectionString, TimeProvider.System),
        // "ef" => new EfCorePaymentStore(new PaymentsDbContext(
        //     new DbContextOptionsBuilder<PaymentsDbContext>()
        //         .UseNpgsql(Fixture.ConnectionString).Options)),
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unknown store engine.")
    };

    protected IIdempotencyStore IdempotencyStore() => new DapperIdempotencyStore(Fixture.ConnectionString);

    /// <summary>Reads one value straight from Postgres, to check what a store really wrote.</summary>
    protected async Task<T> QuerySqlAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);

        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    /// <summary>Runs SQL straight at Postgres. For arranging rows no store would ever write.</summary>
    protected async Task ExecuteSqlAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);

        await connection.ExecuteAsync(sql, parameters);
    }
}