using Microsoft.Data.SqlClient;
using Payments.Migrations;
using Respawn;
using Xunit;

namespace Payments.Testing;

/// <summary>
/// One SQL Server database per test class, made inside the compose container.
/// <para>
/// It is not a Testcontainers container like <see cref="ApiFixture"/>. The SQL Server image is
/// amd64 and runs under Rosetta on this Mac, so starting one per test run is far too slow. The
/// container is already up from compose; each test class only makes its own database inside it,
/// <c>PaymentsLabs_Test_&lt;guid&gt;</c>, migrates it and drops it again (<c>db/README.md</c>).
/// </para>
/// <para>
/// It lives here, in Payments.Testing, and not in the agent tests, because lab 04 needs exactly
/// the same thing and a second copy would drift from this one.
/// </para>
/// </summary>
public sealed class SqlServerDatabase : IAsyncLifetime
{
    private SqlConnection _conn = null!;
    private Respawner? _respawner;

    /// <summary>The database name. A fresh guid, so two test classes never share one.</summary>
    public string Name { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Name = $"PaymentsLabs_Test_{Guid.NewGuid():N}";
        ConnectionString = LocalDatabases.SqlServer(Name);

        Migrator.EnsureDatabase("sqlserver", ConnectionString);

        // DbUp reports a failure in the result, it does not throw. Unchecked, a broken script
        // leaves an empty database and every test fails on a missing table instead of saying
        // which line of SQL is wrong.
        var result = Migrator.Run("sqlserver", ConnectionString);

        if (!result.Successful)
            throw new InvalidOperationException($"Migrations failed for {Name}.", result.Error);

        _conn = new SqlConnection(ConnectionString);
        await _conn.OpenAsync();
    }

    /// <summary>
    /// Deletes every row. Keeps the schema, and keeps DbUp's own record of it.
    /// <para>
    /// The respawner is built on the first reset, not up in InitializeAsync. Respawn reads the
    /// table list once and refuses a schema with no tables at all ("No tables found"), so an
    /// eager build would turn a lab whose script is not written yet into a fixture crash: every
    /// test red with the fixture's message, and the missing table never named.
    /// </para>
    /// </summary>
    public async Task ResetAsync()
    {
        _respawner ??= await Respawner.CreateAsync(_conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = ["SchemaVersions"]
        });

        await _respawner.ResetAsync(_conn);
    }

    public async ValueTask DisposeAsync()
    {
        // Nothing was made, so there is nothing to drop. InitializeAsync can throw before the
        // name is set -- no container, no password in .env -- and xUnit still disposes us.
        if (Name is null) return;

        if (_conn is not null) await _conn.DisposeAsync();

        // Closing our own connection is not enough. ADO.NET keeps the socket in a pool for the
        // next caller, and SQL Server counts a pooled connection as a user of the database, so
        // DROP answers "database in use" (error 3702). Clear the pools, then take the database
        // single-user with ROLLBACK IMMEDIATE to kick anything still holding on.
        SqlConnection.ClearAllPools();

        await using var master = new SqlConnection(LocalDatabases.SqlServer("master"));
        await master.OpenAsync();

        // Brackets, because the name carries underscores and a guid. A database name cannot be
        // a parameter in T-SQL, and this one is built from Guid.NewGuid above, never from input.
        await using var drop = new SqlCommand(
            $"""
            ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{Name}];
            """,
            master);

        await drop.ExecuteNonQueryAsync();
    }
}
