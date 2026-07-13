using System.Security.Cryptography;

namespace Ansight.Pairing;

/// <summary>
/// Signing-key boundary for a protocol-v2 client identity. Platform providers
/// can replace the managed implementation with non-exportable native handles.
/// </summary>
internal interface IPairingV2SigningKey : IDisposable
{
    string KeyId { get; }

    string PublicKey { get; }

    string KeyReference { get; }

    string Sign(string canonicalText);
}

internal interface IPairingV2SigningKeyProvider
{
    IPairingV2SigningKey Create();

    IPairingV2SigningKey Open(string keyReference);
}

internal sealed class ManagedPairingV2SigningKeyProvider : IPairingV2SigningKeyProvider
{
    public IPairingV2SigningKey Create()
        => new ManagedPairingV2SigningKey(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public IPairingV2SigningKey Open(string keyReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyReference);
        var key = ECDsa.Create();
        try
        {
            var privateKey = Convert.FromBase64String(keyReference);
            key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            if (bytesRead != privateKey.Length || key.KeySize != 256)
            {
                throw new CryptographicException("Stored client signing key is invalid.");
            }

            return new ManagedPairingV2SigningKey(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

internal sealed class ManagedPairingV2SigningKey : IPairingV2SigningKey
{
    private readonly ECDsa key;

    public ManagedPairingV2SigningKey(ECDsa key)
    {
        this.key = key ?? throw new ArgumentNullException(nameof(key));
        PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        KeyId = PairingV2Crypto.ComputeClientKeyId(key);
        KeyReference = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    public string KeyId { get; }

    public string PublicKey { get; }

    public string KeyReference { get; }

    public string Sign(string canonicalText) => PairingV2Crypto.Sign(key, canonicalText);

    public void Dispose() => key.Dispose();
}
