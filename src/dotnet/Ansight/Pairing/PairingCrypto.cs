using System.Security.Cryptography;

namespace Ansight.Pairing;

internal static class PairingCrypto
{
    public static string CreateBase64UrlRandom(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
