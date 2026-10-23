using Payments.Agent;
using Payments.Agent.Commands;
using Payments.Agent.Eval;
using Payments.Migrations;

// The front door: read the command line, read the environment, pick the command, and let the
// command do its own wiring. Five commands need five different parts of the stack, so nothing
// heavy is started here: `show` must not launch an MCP child process to print a saved row.

if (args.Length == 0)
{
    Console.Error.WriteLine(CommandLine.Usage);

    return 2;
}

var command = args[0];

// The commands are checked before anything is started, so a typo costs nothing.
if (command is not ("chat" or "run" or "resume" or "show" or "seed-demo" or "eval"))
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLine.Usage);

    return 2;
}

// .env is loaded here and not left to LocalDatabases: the key reads below must not depend on
// something else having loaded the file first.
var repoRoot = RepoRoot.Find();

DotNetEnv.Env.Load(Path.Combine(repoRoot, ".env"));

// The defaults carry the dev set-up, because .env has no PAYMENTS_API_URL and no LLM_URL. The
// trailing slash on the API matters: the MCP server refuses a URL that is not absolute. The /v1
// on the model matters: the OpenAI SDK appends chat/completions to whatever endpoint it is given.
// LLM_MODEL is the name sent in the request. llama-server serves one model and answers with its
// own id whatever we ask for, so this only matters for a server that holds several models.
var settings = new HostSettings(
    repoRoot,
    Environment.GetEnvironmentVariable("PAYMENTS_API_URL") ?? "http://localhost:5080/",
    Environment.GetEnvironmentVariable("LLM_URL") ?? "http://localhost:8080/v1",
    Environment.GetEnvironmentVariable("LLM_MODEL") ?? "local");

switch (command)
{
    case "seed-demo":
        return await SeedDemoCommand.ExecuteAsync(settings);

    case "show":
    {
        if (!CommandLine.TryRunId(args, out var id, out var bad))
        {
            Console.Error.WriteLine(bad);

            return 2;
        }

        return await ShowCommand.ExecuteAsync(id);
    }

    case "resume":
    {
        if (!CommandLine.TryRunId(args, out var runId, out var bad))
        {
            Console.Error.WriteLine(bad);

            return 2;
        }

        // No --agent here. The run already says which agent started it, and the command reads
        // that from the row: see ResumeCommand.
        return await ResumeCommand.ExecuteAsync(settings, runId);
    }
}

// chat, run and eval are left, and all three need a configured agent: its options for the loop
// and its key for the MCP child. eval scores one agent, because the ceiling and the prompt are
// part of what is being scored.
if (!AgentProfiles.TryResolve(CommandLine.AgentName(args), out var agent, out var error))
{
    Console.Error.WriteLine(error);

    return 2;
}

if (command == "eval")
{
    if (!CommandLine.TryLabel(args, out var label, out var missing))
    {
        Console.Error.WriteLine(missing);

        return 2;
    }

    return await EvalRunner.ExecuteAsync(
        settings, agent!, label, CommandLine.Only(args), CommandLine.Out(args));
}

if (command == "chat")
{
    return await ChatCommand.ExecuteAsync(settings, agent!);
}

if (!CommandLine.TryCrashAfter(args, out var crashAfter, out var complaint))
{
    Console.Error.WriteLine(complaint);

    return 2;
}

var request = CommandLine.Positional(args);

if (string.IsNullOrWhiteSpace(request))
{
    Console.Error.WriteLine("run needs a request in quotes, for example:");
    Console.Error.WriteLine("  run --agent support-bot \"Which of our payments are captured?\"");

    return 2;
}

return await RunCommand.ExecuteAsync(settings, agent!, request, crashAfter);
