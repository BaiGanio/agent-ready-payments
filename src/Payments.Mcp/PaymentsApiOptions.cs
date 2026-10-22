 namespace Payments.Mcp;

  /// <summary>
  /// Where the payments API is, and the key to call it with. Both come from the
  /// environment, never from the model. Validated at start-up, so a missing key
  /// stops the server with the variable named instead of turning up as a 401 on
  /// the first tool call, twenty minutes into a conversation.
  /// </summary>
  public sealed class PaymentsApiOptions
  {
      /// <summary>From PAYMENTS_API_URL. An absolute URL ending in a slash.</summary>
      public string Url { get; set; } = string.Empty;

      /// <summary>From PAYMENTS_API_KEY. The MCP server's own key.</summary>
      public string ApiKey { get; set; } = string.Empty;
  }