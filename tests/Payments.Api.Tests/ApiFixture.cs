using DbUp;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
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
        var pg = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("payments_labs")
            .WithUsername("labs")
            .WithPassword("labs")
            .Build();

        await pg.StartAsync();

        Migrate(_pg.GetConnectionString());

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Postgres", pg.GetConnectionString()));

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
        Client = Fixture.Factory.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        Client?.Dispose();
        return ValueTask.CompletedTask;
    }
}