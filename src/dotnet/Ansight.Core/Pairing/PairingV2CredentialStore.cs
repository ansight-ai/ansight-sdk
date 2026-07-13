using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingV2CredentialStore
{
    private const string KeyPrefix = "v2:";
    private const string IndexPrefix = "v2-index:";
    private const string LastCredentialKey = "v2-last";
    private readonly IPairingSecureStore secureStore;
    private readonly PairingV2Validator validator = new();

    public PairingV2CredentialStore(IPairingSecureStore? secureStore = null)
    {
        this.secureStore = secureStore ?? PairingSecureStoreFactory.Create();
    }

    public bool HasCredential => secureStore.TryGet(LastCredentialKey, out var key) && !string.IsNullOrWhiteSpace(key);

    public bool TryLoad(PairingConfigV2 config, DateTimeOffset now, out PairingV2Credential? credential)
    {
        credential = null;
        var key = CreateStoreKey(config.Host.HostId, config.AppId);
        if (!secureStore.TryGet(key, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PairingV2Credential>(json, PairingJson.Compact);
            if (parsed is null ||
                !string.Equals(parsed.HostId, config.Host.HostId, StringComparison.Ordinal) ||
                !string.Equals(parsed.AppId, config.AppId, StringComparison.Ordinal) ||
                !PairingV2Crypto.TryParseTimestamp(parsed.Grant.ExpiresAt, out var expiresAt) ||
                expiresAt <= now)
            {
                secureStore.Remove(key);
                return false;
            }

            credential = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException)
        {
            secureStore.Remove(key);
            return false;
        }
    }

    public void Save(PairingV2Credential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var storeKey = CreateStoreKey(credential.HostId, credential.AppId);
        secureStore.Set(
            storeKey,
            JsonSerializer.Serialize(credential, PairingJson.Compact));
        secureStore.Set(CreateIndexKey(credential.AppId), storeKey);
        secureStore.Set(LastCredentialKey, storeKey);
    }

    public bool TryLoadForApp(string appId, DateTimeOffset now, out PairingV2Credential? credential)
    {
        credential = null;
        if (string.IsNullOrWhiteSpace(appId) ||
            !secureStore.TryGet(CreateIndexKey(appId), out var storeKey) ||
            string.IsNullOrWhiteSpace(storeKey) ||
            !secureStore.TryGet(storeKey, out var json) ||
            string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PairingV2Credential>(json, PairingJson.Compact);
            if (parsed is null ||
                !string.Equals(parsed.AppId, appId, StringComparison.Ordinal) ||
                !validator.TryValidateReconnectConfig(parsed.ReconnectConfig, appId, now, out _) ||
                !validator.TryValidateGrant(parsed.ReconnectConfig, parsed.ClientKeyId, parsed.Grant, now, out _))
            {
                secureStore.Remove(storeKey);
                secureStore.Remove(CreateIndexKey(appId));
                return false;
            }

            credential = parsed;
            return true;
        }
        catch (JsonException)
        {
            secureStore.Remove(storeKey);
            secureStore.Remove(CreateIndexKey(appId));
            return false;
        }
    }

    public bool TryLoadLast(DateTimeOffset now, out PairingV2Credential? credential)
    {
        credential = null;
        if (!secureStore.TryGet(LastCredentialKey, out var storeKey) ||
            string.IsNullOrWhiteSpace(storeKey) ||
            !secureStore.TryGet(storeKey, out var json) ||
            string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PairingV2Credential>(json, PairingJson.Compact);
            if (parsed is null ||
                !validator.TryValidateReconnectConfig(parsed.ReconnectConfig, parsed.AppId, now, out _) ||
                !validator.TryValidateGrant(parsed.ReconnectConfig, parsed.ClientKeyId, parsed.Grant, now, out _))
            {
                secureStore.Remove(storeKey);
                secureStore.Remove(LastCredentialKey);
                return false;
            }

            credential = parsed;
            return true;
        }
        catch (JsonException)
        {
            secureStore.Remove(storeKey);
            secureStore.Remove(LastCredentialKey);
            return false;
        }
    }

    public void UpdateRouting(PairingConfigV2 config, string hostAddress, int discoveryPort)
    {
        if (!TryLoad(config, DateTimeOffset.UtcNow, out var credential) || credential is null)
        {
            return;
        }

        credential.LastHostAddress = hostAddress;
        credential.DiscoveryPort = discoveryPort;
        Save(credential);
    }

    public void Remove(string hostId, string appId)
    {
        secureStore.Remove(CreateStoreKey(hostId, appId));
        secureStore.Remove(CreateIndexKey(appId));
        secureStore.Remove(LastCredentialKey);
    }

    private static string CreateStoreKey(string hostId, string appId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{hostId}\n{appId}"));
        return KeyPrefix + PairingCrypto.ToBase64Url(digest);
    }

    private static string CreateIndexKey(string appId)
        => IndexPrefix + PairingCrypto.ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(appId)));
}

internal sealed class PairingV2Credential
{
    public required string HostId { get; set; }
    public required string AppId { get; set; }
    public required string ClientKeyId { get; set; }
    public required string ClientPublicKey { get; set; }
    public required string ClientKeyReference { get; set; }
    public required PairingGrantV2 Grant { get; set; }
    public required PairingConfigV2 ReconnectConfig { get; set; }
    public string? LastHostAddress { get; set; }
    public int DiscoveryPort { get; set; }
}
