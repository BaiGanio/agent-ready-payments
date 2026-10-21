using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payments.Api.Storage;

public static class RequestHash
{
    public static string Of(string method, string path, object? request)
    {
        var json = request is null ? "" : JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        var sha =  SHA256.HashData(Encoding.UTF8.GetBytes($"{method} {path}\n{json}"));

        return Convert.ToHexStringLower(sha);
    }
}
