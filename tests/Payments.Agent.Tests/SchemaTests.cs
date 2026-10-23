using Microsoft.Data.SqlClient;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// The five rules <c>db/sqlserver/0001_agent_run.sql</c> has to carry, checked against the
/// database the script really made.
/// <para>
/// Plain <c>SqlClient</c>, never EF Core. These tests check the <b>script</b>. EF Core would
/// happily build a model with a status list and a JSON check that live only in C# attributes,
/// and the column underneath would take any string you gave it. A bug in C# has to hit a wall.
/// </para>
/// <para>
/// Every insert names its columns, and names only the ones the rule needs. So any other column
/// the script adds must be nullable or carry a default, or these tests go red for the wrong
/// reason. Each test uses a fresh run id, so the tests do not need a reset between them.
/// </para>
/// </summary>
[Trait("runs", "local")]
public sealed class SchemaTests(SqlServerDatabase db) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task The_agent_tables_exist()
    {
        await using var conn = await OpenAsync();

        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.tables WHERE name IN ('AgentRun', 'AgentStep');", conn);

        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task A_status_outside_the_list_is_refused()
    {
        // 'Paused' is not a status the loop has. The CHECK constraint is the wall.
        var ex = await Assert.ThrowsAsync<SqlException>(
            () => InsertRunAsync(status: "Paused"));

        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task Messages_that_are_not_json_are_refused()
    {
        // SQL Server 2022 has no json type, so the column is nvarchar(max) plus an ISJSON
        // check. Without it a half-written history is stored and the next resume crashes.
        var ex = await Assert.ThrowsAsync<SqlException>(
            () => InsertRunAsync(messages: "not json"));

        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task Two_steps_cannot_share_an_ordinal()
    {
        var runId = await InsertRunAsync();

        await InsertStepAsync(runId, ordinal: 1);

        // Two workers resuming the same run would both write step 1. The unique index is what
        // stops a run having two step 1s and losing one of them.
        var ex = await Assert.ThrowsAsync<SqlException>(
            () => InsertStepAsync(runId, ordinal: 1));

        Assert.Equal(2627, ex.Number);
    }

    [Fact]
    public async Task RowVer_changes_on_every_update()
    {
        var runId = await InsertRunAsync();

        byte[] before = await ReadRowVerAsync(runId);

        await using var conn = await OpenAsync();

        await using var update = new SqlCommand(
            "UPDATE AgentRun SET StepCount = StepCount + 1 WHERE Id = @Id;", conn);

        update.Parameters.AddWithValue("Id", runId);

        await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        byte[] after = await ReadRowVerAsync(runId);

        // rowversion is the database's own counter, not ours. EF Core's concurrency token is
        // worth nothing if the column does not move by itself on every update.
        Assert.NotEqual(before, after);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        return conn;
    }

    /// <summary>Inserts one run and returns its id. The arguments are what a test bends.</summary>
    private async Task<Guid> InsertRunAsync(string status = "Running", string messages = "[]")
    {
        var id = Guid.NewGuid();

        await using var conn = await OpenAsync();

        await using var command = new SqlCommand(
            """
            INSERT INTO AgentRun (Id, AgentName, ModelId, PromptVersion, Status, Messages, StepCount, CreatedUtc)
            VALUES (@Id, @AgentName, @ModelId, @PromptVersion, @Status, @Messages, 0, SYSUTCDATETIME());
            """,
            conn);

        command.Parameters.AddWithValue("Id", id);
        command.Parameters.AddWithValue("AgentName", "support-bot");
        command.Parameters.AddWithValue("ModelId", "test-model");
        command.Parameters.AddWithValue("PromptVersion", "v1");
        command.Parameters.AddWithValue("Status", status);
        command.Parameters.AddWithValue("Messages", messages);

        await command.ExecuteNonQueryAsync();

        return id;
    }

    private async Task InsertStepAsync(Guid runId, int ordinal)
    {
        await using var conn = await OpenAsync();

        await using var command = new SqlCommand(
            """
            INSERT INTO AgentStep (Id, RunId, Ordinal, ToolName, Outcome)
            VALUES (@Id, @RunId, @Ordinal, @ToolName, @Outcome);
            """,
            conn);

        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("Ordinal", ordinal);
        command.Parameters.AddWithValue("ToolName", "get_payment");
        command.Parameters.AddWithValue("Outcome", "Completed");

        await command.ExecuteNonQueryAsync();
    }

    private async Task<byte[]> ReadRowVerAsync(Guid runId)
    {
        await using var conn = await OpenAsync();

        await using var command = new SqlCommand(
            "SELECT RowVer FROM AgentRun WHERE Id = @Id;", conn);

        command.Parameters.AddWithValue("Id", runId);

        return (byte[])(await command.ExecuteScalarAsync())!;
    }
}
