using System.Text.Json;

namespace Payments.Agent;

/// <summary>
/// One configured agent, as it is written in <c>agents.json</c>.
/// <para>
/// The file holds the <b>name</b> of the environment variable that carries the key, never the
/// key itself. A key in a file that is copied to the output folder and committed to git is a
/// key that has leaked, and two agents that differ only by configuration are the point of this
/// step: same loop, same model, same MCP server, different key.
/// </para>
/// </summary>
public sealed record AgentProfile(
    string Name,
    string ApiKeyEnv,
    string PromptVersion,
    decimal RefundCeiling,
    string SystemPrompt);

/// <summary>A profile plus the key it named, read from the environment. Ready to run.</summary>
public sealed record ResolvedAgent(AgentProfile Profile, AgentOptions Options, string ApiKey);

/// <summary>
/// Reads <c>agents.json</c> and turns one entry into <see cref="AgentOptions"/> plus a key.
/// <para>
/// Every failure here is a set-up mistake, so each one is a sentence that says what to do and
/// exit code 2. It never guesses: an unknown name does not fall back to the default agent,
/// because "my read-only agent refunded something" must never start with a typo.
/// </para>
/// </summary>
public static class AgentProfiles
{
    /// <summary>The agent used when no <c>--agent</c> is given.</summary>
    public const string DefaultName = "support-bot";

    /// <summary>
    /// Next to the executable, not in the source folder: the file is copied to the output
    /// folder, so a published agent carries its own configuration.
    /// </summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "agents.json");

    /// <summary>
    /// Finds <paramref name="name"/> and reads its key. On any problem
    /// <paramref name="error"/> is the message to print before exit 2.
    /// </summary>
    public static bool TryResolve(string name, out ResolvedAgent? agent, out string? error)
    {
        agent = null;

        if (!File.Exists(FilePath))
        {
            error = $"agents.json was not found at {FilePath}. "
                + "Build the project again: it is copied to the output folder.";

            return false;
        }

        List<AgentProfile>? profiles;

        try
        {
            profiles = JsonSerializer
                .Deserialize<AgentsFile>(File.ReadAllText(FilePath), JsonSerializerOptions.Web)
                ?.Agents;
        }
        catch (JsonException ex)
        {
            error = $"agents.json cannot be read: {ex.Message}";

            return false;
        }

        if (profiles is null or { Count: 0 })
        {
            error = $"agents.json has no agents in it ({FilePath}).";

            return false;
        }

        var profile = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            error = $"There is no agent called '{name}'. agents.json has: "
                + string.Join(", ", profiles.Select(p => p.Name)) + ".";

            return false;
        }

        var key = Environment.GetEnvironmentVariable(profile.ApiKeyEnv);

        if (string.IsNullOrWhiteSpace(key))
        {
            // The agent is configured and the key is missing: say which variable, because the
            // two agents use two different ones and the error is otherwise identical.
            error = $"{profile.Name} needs its key in {profile.ApiKeyEnv}, which is not set. "
                + "Fill it in .env, then: set -a; source .env; set +a";

            return false;
        }

        agent = new ResolvedAgent(profile, ToOptions(profile), key);
        error = null;

        return true;
    }

    /// <summary>
    /// The four configured fields land on <see cref="AgentOptions"/>; every other limit keeps
    /// its default, so a new guard is on for both agents the day it is written.
    /// </summary>
    private static AgentOptions ToOptions(AgentProfile profile) => new()
    {
        AgentName = profile.Name,
        PromptVersion = profile.PromptVersion,
        RefundCeiling = profile.RefundCeiling,
        SystemPrompt = profile.SystemPrompt,
    };

    private sealed record AgentsFile(List<AgentProfile> Agents);
}
