namespace Payments.Mcp;

/// <summary>
/// What the API answered: the body, or the status and the problem's code.
/// An HttpResponseMessage never leaves the client, so no tool can forget to
/// dispose one or read a status it does not understand.
/// </summary>
internal sealed class ApiResult<T>
{
    private ApiResult(bool ok, int status, T? value, string? code, string? detail)
    {
        Ok = ok;
        Status = status;
        Value = value;
        Code = code;
        Detail = detail;
    }

    internal bool Ok { get; }

    /// <summary>The HTTP status, or 0 when the call never reached the API.</summary>
    internal int Status { get; }

    internal T? Value { get; }

    /// <summary>The ProblemDetails "code" extension, when the body had one.</summary>
    internal string? Code { get; }

    /// <summary>
    /// The API's own words. For the MCP server's log only. Never put this in a
    /// tool result: the body may echo the API key or another merchant's data.
    /// </summary>
    internal string? Detail { get; }

    internal static ApiResult<T> Success(T value) => new(true, 200, value, null, null);

    internal static ApiResult<T> Problem(int status, string? code, string? detail)
        => new(false, status, default, code, detail);
}