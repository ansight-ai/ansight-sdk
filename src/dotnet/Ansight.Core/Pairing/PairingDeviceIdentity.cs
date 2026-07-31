namespace Ansight.Pairing;

internal static class PairingDeviceIdentity
{
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> accessTokens = new(StringComparer.Ordinal);

    public static string GetOrCreate(string appId)
    {
        var normalizedAppId = string.IsNullOrWhiteSpace(appId) ? "app" : appId.Trim();
        lock (gate)
        {
            if (identities.TryGetValue(normalizedAppId, out var identity))
            {
                return identity;
            }

            var path = ResolvePath(normalizedAppId, ".txt");
            identity = TryRead(path) ?? PairingCrypto.CreateBase64UrlRandom(16);
            identities[normalizedAppId] = identity;
            TryPersist(path, identity);
            return identity;
        }
    }

    public static string GetOrCreateAccessToken(string appId)
    {
        var normalizedAppId = string.IsNullOrWhiteSpace(appId) ? "app" : appId.Trim();
        lock (gate)
        {
            if (accessTokens.TryGetValue(normalizedAppId, out var accessToken))
            {
                return accessToken;
            }

            var path = ResolvePath(normalizedAppId, ".token");
            accessToken = TryRead(path) ?? PairingCrypto.CreateBase64UrlRandom(32);
            accessTokens[normalizedAppId] = accessToken;
            TryPersist(path, accessToken);
            return accessToken;
        }
    }

    private static string ResolvePath(string appId, string extension)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        var safeAppId = string.Concat(
            appId.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(root, "Ansight", "device-identities", $"{safeAppId}{extension}");
    }

    private static string? TryRead(string path)
    {
        try
        {
            var value = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void TryPersist(string path, string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // A process-stable identity still allows the current session to connect.
        }
    }
}
