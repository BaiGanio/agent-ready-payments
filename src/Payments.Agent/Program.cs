using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Payments.Agent;
using Payments.Agent.Storage;
using Payments.Agent.Tools;
using Payments.Migrations;

// The console that wires the real parts together: llama-server through IChatClient, the MCP
// server as a child process over stdio, and EF Core on the dev SQL Server. Nothing in AgentLoop
// knows which of them is real, which is the point: this file is the only thing that changes when
// the model, the tools or the store change.

// 1. The command line, before anything is started. A wrong command must not launch a child
//    process, open a database or spend a token.
if (args.Length < 2)
{
    Console.Error.WriteLine("""
        usage:
          run "<request>" [--crash-after N]
          resume <runId>
        """);

    return 2;
}

var command = args[0];

Guid? resumeId = null;

if (command == "resume")
{
    if (!Guid.TryParse(args[1], out var parsed))
    {
        Console.Error.WriteLine($"'{args[1]}' is not a run id.");

        return 2;
    }

    resumeId = parsed;
}
else if (command != "run")
{
    Console.Error.WriteLine($"Unknown command '{command}'. The commands are: run, resume.");

    return 2;
}

var crashAfter = CrashAfter(args);

if (crashAfter == -1)
{
    return 2;
}

// 2. The environment. .env is loaded here and not left to LocalDatabases: the key reads below
//    must not depend on something else having loaded the file first.
var repoRoot = RepoRoot.Find();

DotNetEnv.Env.Load(Path.Combine(repoRoot, ".env"));

// The defaults carry the dev set-up, because .env has no PAYMENTS_API_URL and no LLM_URL. The
// trailing slash on the API matters: the MCP server refuses a URL that is not absolute. The /v1
// on the model matters: the OpenAI SDK appends chat/completions to whatever endpoint it is given.
var apiUrl = Environment.GetEnvironmentVariable("PAYMENTS_API_URL") ?? "http://localhost:5080/";
var llmUrl = Environment.GetEnvironmentVariable("LLM_URL") ?? "http://localhost:8080/v1";
var agentKey = Environment.GetEnvironmentVariable("PAYMENTS_AGENT_KEY");

if (string.IsNullOrWhiteSpace(agentKey))
{
    // A clear message and exit 2. Without this the agent key reaches the MCP child as an empty
    // string, every tool call comes back 401, and the model is blamed for a set-up mistake.
    Console.Error.WriteLine(
        "PAYMENTS_AGENT_KEY is not set. The agent needs its own API key for the payments API.");
    Console.Error.WriteLine(
        "Copy .env.example to .env and fill it in, then: set -a; source .env; set +a");

    return 2;
}

// 3. The tools: Payments.Mcp as a child process, speaking MCP over its stdin and stdout. The
//    child gets the two variables it needs and nothing else, so one place decides which agent
//    this process is. Note the names differ: our PAYMENTS_AGENT_KEY is its PAYMENTS_API_KEY.
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "payments-mcp",
    Command = "dotnet",
    Arguments = [Path.Combine(repoRoot, "src/Payments.Mcp/bin/Debug/net10.0/Payments.Mcp.dll")],
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["PAYMENTS_API_URL"] = apiUrl,
        ["PAYMENTS_API_KEY"] = agentKey,
    },
});

// await using, not using: disposing the client is what ends the child process. A plain using
// leaves an orphan Payments.Mcp.dll running after every run.
await using var mcp = await McpClient.CreateAsync(transport);

var tools = await McpToolHost.CreateAsync(mcp);

// 4. The model. "local" is the model name field, which llama-server ignores and answers with its
//    own; the reply's ModelId is what lands on every step. The credential cannot be empty even
//    though llama-server never checks it.
var model = new OpenAI.Chat.ChatClient(
        "local",
        new ApiKeyCredential("none"),
        new OpenAIClientOptions { Endpoint = new Uri(llmUrl) })
    .AsIChatClient();

// 5. The store. EfAgentRunStore takes a factory, not a context: one fresh context per call, the
//    way two separate processes would see the row. The two tables come from DbUp, not from EF.
var connection = LocalDatabases.SqlServer();

var contextOptions = new DbContextOptionsBuilder<AgentDbContext>()
    .UseSqlServer(connection)
    .Options;

// The printing and the crash are decorators, in that order. The loop never learns that anything
// is watching it or that the process can die: a loop with an "if demo" branch would prove nothing.
IAgentRunStore store = new PrintingStore(new EfAgentRunStore(() => new AgentDbContext(contextOptions)));

if (crashAfter is int after and > 0)
{
    store = new CrashingStore(store, after);
}

var loop = new AgentLoop(
    model,
    tools,
    new ConsoleApprovalChannel(),
    store,
    new AgentOptions(),
    TimeProvider.System);

if (resumeId is not null)
{
    Console.Error.WriteLine("resume is not built yet. It is step 19.");

    return 2;
}

Console.WriteLine($"agent   {new AgentOptions().AgentName}");
Console.WriteLine($"model   {llmUrl}");
Console.WriteLine($"api     {apiUrl}");
Console.WriteLine();

var run = await loop.StartAsync(args[1], CancellationToken.None);

Console.WriteLine();
Console.WriteLine(run.Status == RunStatus.Failed
    ? $"FAILED  {run.Reason}"
    : run.Answer);

// The exit code is for a script, not for a person: 0 finished, 1 a guard stopped it, 2 a set-up
// mistake, 99 the deliberate crash.
return run.Status == RunStatus.Failed ? 1 : 0;

// -1 means "already complained, exit 2". A bad number must not quietly become no crash at all.
static int? CrashAfter(string[] args)
{
    var at = Array.IndexOf(args, "--crash-after");

    if (at < 0)
    {
        return null;
    }

    if (at + 1 >= args.Length || !int.TryParse(args[at + 1], out var n) || n < 1)
    {
        Console.Error.WriteLine("--crash-after needs a whole number of 1 or more.");

        return -1;
    }

    return n;
}

/// <summary>
/// Shows the run while it happens: the run id on the first save, then one line per saved step.
/// <para>
/// The id is printed from here and not from the caller because the run is created inside
/// <c>StartAsync</c>. It lands on the first save, which happens before any model call, so the id
/// is on screen before anything slow or anything that can crash.
/// </para>
/// </summary>
internal sealed class PrintingStore(IAgentRunStore inner) : IAgentRunStore
{
    private bool _announced;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
    {
        if (!_announced)
        {
            Console.WriteLine($"run     {run.Id}");

            _announced = true;
        }

        var outcome = await inner.SaveAsync(run, newSteps, ct);

        foreach (var step in newSteps)
        {
            Console.WriteLine(
                $"  [{step.Ordinal}] {step.Kind} {step.ToolName ?? "-"} → {step.Outcome}" +
                (step.Approver is null ? "" : $" (by {step.Approver})"));
        }

        return outcome;
    }
}

/// <summary>
/// Stops the process on purpose, to make a real crash for the resume by hand. It counts saved
/// steps, and it dies <b>after</b> the save: a crash before the write would lose the step, and
/// then the resume would only be tested against a shorter history.
/// </summary>
internal sealed class CrashingStore(IAgentRunStore inner, int after) : IAgentRunStore
{
    private int _saved;

    public Task<LoadedRun?> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);

    public async Task<SaveOutcome> SaveAsync(
        AgentRun run, IReadOnlyList<AgentStep> newSteps, CancellationToken ct)
    {
        var outcome = await inner.SaveAsync(run, newSteps, ct);

        _saved += newSteps.Count;

        if (_saved >= after)
        {
            Console.Error.WriteLine($"[crash] {_saved} steps saved. Dying on purpose.");

            Environment.Exit(99);
        }

        return outcome;
    }
}
