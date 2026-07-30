using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ansight.Pairing;

internal sealed class StoredPairingDocumentCache
{
    public static readonly TimeSpan DefaultProfileRetention = TimeSpan.FromDays(14);
    private const string ProfilesSchema = "ansight.cached-pairing-profiles.v1";
    private const string UnknownNetworkKey = "wifi:<unknown>";

    private readonly string cacheFilePath;
    private readonly PairingConfigDocumentService configDocumentService = new();
    private readonly TimeSpan profileRetention;

    public StoredPairingDocumentCache(
        string cacheKey,
        string? cacheFilePath = null,
        TimeSpan? profileRetention = null)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheKey));
        }

        this.cacheFilePath = string.IsNullOrWhiteSpace(cacheFilePath)
            ? ResolveDefaultCacheFilePath(cacheKey)
            : Path.GetFullPath(cacheFilePath.Trim());
        this.profileRetention = ValidateProfileRetention(profileRetention ?? DefaultProfileRetention);
    }

    internal string CacheFilePath => cacheFilePath;

    public bool HasCachedDocument
    {
        get
        {
            if (!TryReadStoredProfiles(DateTimeOffset.UtcNow, out var profiles, out _, out _))
            {
                return false;
            }

            return profiles.Count > 0;
        }
    }

    public bool TryLoadValidated(
        string? expectedAppId,
        out ParsedPairingDocument? document,
        out string error)
    {
        document = null;
        if (!TryLoadValidatedProfiles(expectedAppId, out var profiles, out error) ||
            profiles.Count == 0)
        {
            return false;
        }

        document = profiles[0].Document;
        return true;
    }

    public bool TryLoadValidatedProfiles(
        string? expectedAppId,
        out IReadOnlyList<StoredPairingDocumentProfile> profiles,
        out string error)
    {
        return TryLoadValidatedProfiles(
            expectedAppId,
            DateTimeOffset.UtcNow,
            out profiles,
            out error);
    }

    internal bool TryLoadValidatedProfiles(
        string? expectedAppId,
        DateTimeOffset now,
        out IReadOnlyList<StoredPairingDocumentProfile> profiles,
        out string error)
    {
        profiles = [];
        error = string.Empty;

        if (!TryReadStoredProfiles(now, out var storedProfiles, out var shouldRewrite, out error))
        {
            return false;
        }

        var validatedProfiles = new List<StoredPairingDocumentProfile>();
        var removedProfiles = false;
        foreach (var profile in storedProfiles)
        {
            if (!configDocumentService.TryValidateDocument(profile.Document, expectedAppId, out var validationError))
            {
                removedProfiles = true;
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = validationError;
                }

                continue;
            }

            validatedProfiles.Add(profile);
        }

        if (shouldRewrite || removedProfiles)
        {
            WriteProfiles(validatedProfiles);
        }

        if (validatedProfiles.Count == 0)
        {
            error = string.IsNullOrWhiteSpace(error)
                ? "No cached Ansight host session is available."
                : $"{error} Cached Ansight host session was cleared.";
            return false;
        }

        profiles = validatedProfiles;
        error = string.Empty;
        return true;
    }

    public void Save(ParsedPairingDocument document)
        => Save(document, DateTimeOffset.UtcNow);

    internal void Save(ParsedPairingDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TryReadStoredProfiles(now, out var profiles, out _, out _))
        {
            profiles = [];
        }

        var storedProfile = CreateProfile(document, now);
        profiles.RemoveAll(profile => string.Equals(
            profile.NetworkKey,
            storedProfile.NetworkKey,
            StringComparison.OrdinalIgnoreCase));
        profiles.Add(storedProfile);
        WriteProfiles(profiles);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public void ClearProfile(StoredPairingDocumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ClearProfile(profile.NetworkKey);
    }

    public void ClearProfile(string networkKey)
    {
        if (string.IsNullOrWhiteSpace(networkKey))
        {
            return;
        }

        try
        {
            if (!TryReadStoredProfiles(DateTimeOffset.UtcNow, out var profiles, out _, out _))
            {
                return;
            }

            profiles.RemoveAll(profile => string.Equals(
                profile.NetworkKey,
                networkKey,
                StringComparison.OrdinalIgnoreCase));
            WriteProfiles(profiles);
        }
        catch
        {
            // Best effort.
        }
    }

    internal static string ResolveCacheKey(IDeviceAppProfileProvider profileProvider)
    {
        ArgumentNullException.ThrowIfNull(profileProvider);

        try
        {
            var profile = profileProvider.CreateDeviceAppProfile();
            var appId = profile?.App?.AppId?.Trim();
            if (!string.IsNullOrWhiteSpace(appId))
            {
                return appId;
            }

            var appName = profile?.App?.AppName?.Trim();
            if (!string.IsNullOrWhiteSpace(appName))
            {
                return appName;
            }
        }
        catch
        {
            // Fall back to assembly metadata.
        }

        return Assembly.GetEntryAssembly()?.GetName().Name?.Trim()
               ?? "ansight-default";
    }

    private bool TryReadStoredProfiles(
        DateTimeOffset now,
        out List<StoredPairingDocumentProfile> profiles,
        out bool shouldRewrite,
        out string error)
    {
        profiles = [];
        shouldRewrite = false;
        error = string.Empty;

        if (!File.Exists(cacheFilePath))
        {
            error = "No cached Ansight host session is available.";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(cacheFilePath);
        }
        catch (Exception ex)
        {
            error = $"Failed to read cached Ansight host session: {ex.Message}";
            return false;
        }

        if (!TryParseStoredProfiles(json, now, out profiles, out shouldRewrite, out error))
        {
            Clear();
            error = string.IsNullOrWhiteSpace(error)
                ? "Cached Ansight host session is invalid and was cleared."
                : $"{error} Cached Ansight host session was cleared.";
            return false;
        }

        if (shouldRewrite)
        {
            WriteProfiles(profiles);
        }

        return true;
    }

    private bool TryParseStoredProfiles(
        string json,
        DateTimeOffset now,
        out List<StoredPairingDocumentProfile> profiles,
        out bool shouldRewrite,
        out string error)
    {
        profiles = [];
        shouldRewrite = false;
        error = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Cached host session JSON root must be an object.";
                return false;
            }

            var schema = root.TryGetProperty("schema", out var schemaElement)
                ? schemaElement.GetString()
                : null;

            if (!string.Equals(schema, ProfilesSchema, StringComparison.Ordinal))
            {
                error = $"Unsupported cached host session schema '{schema ?? "<missing>"}'.";
                return false;
            }

            var cacheFile = JsonSerializer.Deserialize<StoredPairingDocumentCacheFile>(json, PairingJson.Compact);
            if (cacheFile?.Profiles is null)
            {
                error = "Cached host session profile list was missing.";
                return false;
            }

            foreach (var entry in cacheFile.Profiles)
            {
                if (entry is null || entry.Document?.Config is null)
                {
                    shouldRewrite = true;
                    continue;
                }

                var profile = CreateProfile(entry, now);
                if (profile.ExpiresAtUtc <= now)
                {
                    shouldRewrite = true;
                    continue;
                }

                profiles.Add(profile);
            }

            profiles = SortProfiles(profiles);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse cached Ansight host session: {ex.Message}";
            return false;
        }
    }

    private StoredPairingDocumentProfile CreateProfile(StoredPairingDocumentCacheEntry entry, DateTimeOffset now)
    {
        var document = PairingConfigDocumentService.CreateDocument(entry.Document!);
        var networkKey = string.IsNullOrWhiteSpace(entry.NetworkKey)
            ? ResolveNetworkKey(document)
            : entry.NetworkKey.Trim();
        var lastConnectedAtUtc = entry.LastConnectedAtUtc == default
            ? ResolveLastConnectedAt(document, now)
            : entry.LastConnectedAtUtc;
        var expiresAtUtc = entry.ExpiresAtUtc == default
            ? lastConnectedAtUtc.Add(profileRetention)
            : entry.ExpiresAtUtc;
        var wifiName = FirstNonWhiteSpace(entry.WifiName, document.DiscoveryHint?.WifiName);
        var hostName = FirstNonWhiteSpace(entry.HostName, document.DiscoveryHint?.HostName);

        return new StoredPairingDocumentProfile(
            networkKey,
            wifiName,
            hostName,
            lastConnectedAtUtc,
            expiresAtUtc,
            document);
    }

    private StoredPairingDocumentProfile CreateProfile(ParsedPairingDocument document, DateTimeOffset now)
    {
        var lastConnectedAtUtc = ResolveLastConnectedAt(document, now);
        return new StoredPairingDocumentProfile(
            ResolveNetworkKey(document),
            NormalizeWifiName(document.DiscoveryHint?.WifiName),
            NullIfWhiteSpace(document.DiscoveryHint?.HostName),
            lastConnectedAtUtc,
            lastConnectedAtUtc.Add(profileRetention),
            document);
    }

    private void WriteProfiles(IReadOnlyCollection<StoredPairingDocumentProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            Clear();
            return;
        }

        var directoryPath = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var model = new StoredPairingDocumentCacheFile
        {
            Schema = ProfilesSchema,
            Profiles = SortProfiles(profiles)
                .Select(CreateEntry)
                .ToList()
        };
        var json = JsonSerializer.Serialize(model, PairingJson.Pretty);
        File.WriteAllText(cacheFilePath, json);
    }

    private static StoredPairingDocumentCacheEntry CreateEntry(StoredPairingDocumentProfile profile)
    {
        return new StoredPairingDocumentCacheEntry
        {
            NetworkKey = profile.NetworkKey,
            WifiName = profile.WifiName,
            HostName = profile.HostName,
            LastConnectedAtUtc = profile.LastConnectedAtUtc,
            ExpiresAtUtc = profile.ExpiresAtUtc,
            Document = PairingConfigDocumentService.CreateConfigDocument(profile.Document)
        };
    }

    private static List<StoredPairingDocumentProfile> SortProfiles(IEnumerable<StoredPairingDocumentProfile> profiles)
    {
        return profiles
            .OrderByDescending(profile => profile.LastConnectedAtUtc)
            .ThenBy(profile => profile.NetworkKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset ResolveLastConnectedAt(ParsedPairingDocument document, DateTimeOffset now)
    {
        return document.DiscoveryHint?.CapturedAt ?? now;
    }

    internal static string ResolveNetworkKey(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ResolveNetworkKey(document.DiscoveryHint?.WifiName);
    }

    private static string ResolveNetworkKey(string? wifiName)
    {
        var normalizedWifiName = NormalizeWifiName(wifiName);
        return string.IsNullOrWhiteSpace(normalizedWifiName)
            ? UnknownNetworkKey
            : $"wifi:{normalizedWifiName}";
    }

    private static string? NormalizeWifiName(string? wifiName)
    {
        return NullIfWhiteSpace(wifiName);
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = NullIfWhiteSpace(value);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TimeSpan ValidateProfileRetention(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Cached profile retention must be positive.");
        }

        return retention;
    }

    private static string ResolveDefaultCacheFilePath(string cacheKey)
    {
        var rootDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            rootDirectory = Path.GetTempPath();
        }

        return Path.Combine(
            rootDirectory,
            "Ansight",
            "pairing",
            $"{SanitizeFileName(cacheKey)}.auto-probe.json");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var buffer = value.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (invalidCharacters.Contains(buffer[i]))
            {
                buffer[i] = '_';
            }
        }

        var sanitized = new string(buffer).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "ansight-default" : sanitized;
    }

    private sealed class StoredPairingDocumentCacheFile
    {
        public required string Schema { get; init; }

        public List<StoredPairingDocumentCacheEntry>? Profiles { get; init; }
    }

    private sealed class StoredPairingDocumentCacheEntry
    {
        public string? NetworkKey { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WifiName { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HostName { get; init; }

        public DateTimeOffset LastConnectedAtUtc { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }

        public PairingConfigDocument? Document { get; init; }
    }
}

internal sealed class StoredPairingDocumentProfile
{
    public StoredPairingDocumentProfile(
        string networkKey,
        string? wifiName,
        string? hostName,
        DateTimeOffset lastConnectedAtUtc,
        DateTimeOffset expiresAtUtc,
        ParsedPairingDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkKey);
        ArgumentNullException.ThrowIfNull(document);

        NetworkKey = networkKey.Trim();
        WifiName = string.IsNullOrWhiteSpace(wifiName) ? null : wifiName.Trim();
        HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName.Trim();
        LastConnectedAtUtc = lastConnectedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Document = document;
    }

    public string NetworkKey { get; }

    public string? WifiName { get; }

    public string? HostName { get; }

    public DateTimeOffset LastConnectedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public ParsedPairingDocument Document { get; }
}
