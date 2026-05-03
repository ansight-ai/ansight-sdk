using System.Security.Cryptography;

namespace Ansight.Pairing;

internal static class PairingCrypto
{
    public static string CreateBase64UrlRandom(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return ToBase64Url(bytes);
    }

    public static string ToBase64Url(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] FromBase64Url(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value
            .Trim()
            .Replace('-', '+')
            .Replace('_', '/');
        var paddingLength = 4 - (normalized.Length % 4);
        if (paddingLength is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + paddingLength, '=');
        }

        return Convert.FromBase64String(normalized);
    }
}
