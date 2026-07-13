using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Ansight.Pairing;

internal static class PairingV2Crypto
{
    public const string SignatureAlgorithm = "ES256-P1363";
    public const int NonceByteCount = 32;
    public const int RequestIdByteCount = 16;

    public static string Sign(ECDsa key, string canonicalText)
    {
        ArgumentNullException.ThrowIfNull(key);
        var signature = key.SignData(
            Encoding.UTF8.GetBytes(canonicalText),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    public static bool Verify(string publicKeyBase64, string signatureBase64, string canonicalText)
    {
        try
        {
            var publicKey = Convert.FromBase64String(publicKeyBase64);
            var signature = Convert.FromBase64String(signatureBase64);
            if (signature.Length != 64)
            {
                return false;
            }

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            return bytesRead == publicKey.Length && key.KeySize == 256 && key.VerifyData(
                Encoding.UTF8.GetBytes(canonicalText),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ComputeSpkiFingerprint(string publicKeyBase64)
        => PairingCrypto.ToBase64Url(SHA256.HashData(Convert.FromBase64String(publicKeyBase64)));

    public static string ComputeClientKeyId(ECDsa key)
        => PairingCrypto.ToBase64Url(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    public static string ComputeTlsSpkiSha256(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return PairingCrypto.ToBase64Url(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    }

    public static string ComputeConfigSignatureSha256(string signatureBase64)
        => PairingCrypto.ToBase64Url(SHA256.HashData(Convert.FromBase64String(signatureBase64)));

    public static string ComputeEnrollmentProof(string secretBase64Url, string canonicalText)
    {
        var secret = PairingCrypto.FromBase64Url(secretBase64Url);
        using var hmac = new HMACSHA256(secret);
        return PairingCrypto.ToBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalText)));
    }

    public static bool FixedTimeEqualsBase64Url(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                PairingCrypto.FromBase64Url(left),
                PairingCrypto.FromBase64Url(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool HasDecodedLength(string value, int expectedLength)
    {
        try
        {
            return PairingCrypto.FromBase64Url(value).Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
}
