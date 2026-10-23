using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Payments.Api.Auth;
using Payments.Migrations;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Payments.Testing;

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

    /// <summary>
    /// Inserts a key straight into api_key and returns the raw key. Only the hash is stored,
    /// so the raw key lives only in the test. It is here, not in a test base class, because
    /// the agent tests need a key too and they do not inherit from ApiTestBase.
    /// </summary>
    public async Task<string> InsertKeyAsync(string[] scopes, string kind, Guid merchant)
    {
        string raw = Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            insert into api_key (key_hash, name, merchant_id, kind, scopes)
            values (@hash, @name, @merchant, @kind, @scopes)
            """,
            connection);

        command.Parameters.AddWithValue("hash", ApiKeyHash.Of(raw));
        command.Parameters.AddWithValue("name", raw);
        command.Parameters.AddWithValue("merchant", merchant);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("scopes", scopes);

        await command.ExecuteNonQueryAsync();

        return raw;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    private static void Migrate(string connectionString)
    {
        var result = Migrator.Run("postgres", connectionString);

        if (!result.Successful)
            throw new InvalidOperationException("Migrations failed.", result.Error);
    }
}
