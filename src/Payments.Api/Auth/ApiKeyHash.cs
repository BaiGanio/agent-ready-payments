
namespace Payments.Api.Auth;

using System.Security.Cryptography;
using System.Text;

public static class ApiKeyHash
    {

        public static string Of(string rawKey)
          => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

    }