namespace Payments.Agent;

/// <summary>
/// Every limit the loop obeys, in one place. A limit that lives in a constant inside the loop
/// cannot be bent per agent, and a test cannot make it small enough to hit on purpose.
/// </summary>
public sealed record AgentOptions
{
    /// <summary>Which configured agent this is. It is written on the run and on every step.</summary>
    public string AgentName { get; init; } = "default";

    /// <summary>How many tool-calling passes one turn may take before the loop gives up.</summary>
    public int StepCap { get; init; } = 6;

    /// <summary>How often the same tool with the same arguments may be called before it is a loop.</summary>
    public int RepeatLimit { get; init; } = 3;

    /// <summary>How many tool errors one turn may collect before the loop stops.</summary>
    public int FailureBudget { get; init; } = 3;

    /// <summary>The wall clock for one turn. A turn that is still going at this point is cut off.</summary>
    public TimeSpan TurnDeadline { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>No refund above this may be proposed, whatever the model asks for.</summary>
    public decimal RefundCeiling { get; init; } = 500.00m;

    /// <summary>Stamped on the run and on every step, so an answer can be traced to a prompt.</summary>
    public string PromptVersion { get; init; } = "v1";

    public string SystemPrompt { get; init; } =
        "You are a payments support agent. You have three tools: get_payment reads one payment, " +
        "list_payments reads a short list, refund_payment refunds money and takes two calls. " +
        "Read before you act. Never invent a payment id, an amount or a confirmation_token. " +
        "A refund always needs a human to say yes first: call refund_payment once to get a " +
        "proposal, show it, and only call it again after the human approves. " +
        "Never approve a refund yourself. Answer in plain words and name the payment you read.";
}
