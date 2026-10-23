namespace Payments.Agent.Commands;

/// <summary>
/// What the whole process needs before it knows which command it is running: where the repo is,
/// where the API is, where the model is, and which model name to ask for.
/// <para>
/// It is read once, in <c>Program</c>, and handed to the command. A command that read the
/// environment itself could be given a different API than the MCP child it started.
/// </para>
/// </summary>
/// <param name="RepoRoot">The repository root, used to find the MCP server's dll.</param>
/// <param name="ApiUrl">The payments API. The trailing slash matters: the MCP server refuses a URL that is not absolute.</param>
/// <param name="LlmUrl">The OpenAI-shaped endpoint, ending in <c>/v1</c>: the SDK appends <c>chat/completions</c>.</param>
/// <param name="LlmModel">The model name sent in the request. llama-server serves one model and ignores it.</param>
public sealed record HostSettings(string RepoRoot, string ApiUrl, string LlmUrl, string LlmModel);
