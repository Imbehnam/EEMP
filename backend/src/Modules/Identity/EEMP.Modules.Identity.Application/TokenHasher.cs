using System.Security.Cryptography;
using System.Text;

namespace EEMP.Modules.Identity.Application;

public static class TokenHasher
{
    public static string NewToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(48));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
