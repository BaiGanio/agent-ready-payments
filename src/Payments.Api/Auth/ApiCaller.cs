namespace Payments.Api.Auth;

public sealed record ApiCaller(string Name, Guid MerchantId, string Kind, IReadOnlySet<string> Scopes);