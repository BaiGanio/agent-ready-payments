namespace Payments.Agent.Commands;

/// <summary>
/// The command line, read before anything is started. A wrong command must not launch a child
/// process, open a database or spend a token, so every check here happens before
/// <see cref="AgentHost"/> is built.
/// </summary>
public static class CommandLine
{
    public const string Usage = """
        usage:
          chat  [--agent <name>]
          run   [--agent <name>] "<request>" [--crash-after N]
          resume <runId>
          show   <runId>
          seed-demo
          eval  [--agent <name>] --label <name> [--only S03] [--out <file.md>]

        agents come from agents.json next to the executable (support-bot, read-bot).
        eval needs a live model and the service key in PAYMENTS_DEV_KEY.
        every eval run saves a report under labs/09-agent-loop/eval-runs/.
        """;

    /// <summary>The agent asked for, or the default one.</summary>
    public static string AgentName(string[] args)
        => Flag(args, "--agent") ?? AgentProfiles.DefaultName;

    /// <summary>The value after <paramref name="name"/>, or null if the flag is not there.</summary>
    public static string? Flag(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    /// The first argument that is not the command, not a flag and not a flag's value: the
    /// request text or a run id. With five commands and two flags, counting positions by hand
    /// is how <c>run --agent read-bot "..."</c> quietly becomes a request of "read-bot".
    /// </summary>
    public static string? Positional(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // Both flags we have take a value, so skip it too.
                i++;

                continue;
            }

            return args[i];
        }

        return null;
    }

    /// <summary>
    /// Null means no crash was asked for. A bad number is an error, never quietly no crash at
    /// all: a resume proved against a run that never died proves nothing.
    /// </summary>
    public static bool TryCrashAfter(string[] args, out int? after, out string? error)
    {
        after = null;
        error = null;

        var at = Array.IndexOf(args, "--crash-after");

        if (at < 0)
        {
            return true;
        }

        if (at + 1 >= args.Length || !int.TryParse(args[at + 1], out var n) || n < 1)
        {
            error = "--crash-after needs a whole number of 1 or more.";

            return false;
        }

        after = n;

        return true;
    }

    /// <summary>
    /// The name the evaluation's scores are filed under: the model on the rung being scored.
    /// <para>
    /// It is required and never guessed from <c>LLM_MODEL</c>, because llama-server answers with
    /// its own id whatever it is asked for, and two rungs scored under the same label are two
    /// numbers nobody can tell apart afterwards.
    /// </para>
    /// </summary>
    public static bool TryLabel(string[] args, out string label, out string? error)
    {
        label = Flag(args, "--label") ?? "";

        if (string.IsNullOrWhiteSpace(label))
        {
            error = "eval needs --label with the model being scored, for example:\n"
                + "  eval --label gemma-4-E4B";

            return false;
        }

        error = null;

        return true;
    }

    /// <summary>One scenario instead of all ten, while a scenario is being written.</summary>
    public static string? Only(string[] args) => Flag(args, "--only");

    /// <summary>
    /// Where the run's report goes. Without it the runner names the file after the time the run
    /// started and puts it in <c>labs/09-agent-loop/eval-runs/</c>.
    /// </summary>
    public static string? Out(string[] args) => Flag(args, "--out");

    /// <summary>A run id, for <c>resume</c> and <c>show</c>.</summary>
    public static bool TryRunId(string[] args, out Guid id, out string? error)
    {
        var text = Positional(args);

        if (text is null)
        {
            id = Guid.Empty;
            error = $"{args[0]} needs a run id. A run prints its id on its first line.";

            return false;
        }

        if (!Guid.TryParse(text, out id))
        {
            error = $"'{text}' is not a run id.";

            return false;
        }

        error = null;

        return true;
    }
}
