using System.Reflection;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class StoredPairingDocumentCache
{
    private readonly string cacheFilePath;
    private readonly PairingConfigDocumentService configDocumentService = new();

    public StoredPairingDocumentCache(string cacheKey, string? cacheFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheKey));
        }

        this.cacheFilePath = string.IsNullOrWhiteSpace(cacheFilePath)
            ? ResolveDefaultCacheFilePath(cacheKey)
            : Path.GetFullPath(cacheFilePath.Trim());
    }

    internal string CacheFilePath => cacheFilePath;

    public bool HasCachedDocument => File.Exists(cacheFilePath);

    public bool TryLoadValidated(
        string? expectedAppId,
        out ParsedPairingDocument? document,
        out string error)
    {
        document = null;
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

        if (!configDocumentService.TryParseAndValidateDocument(json, expectedAppId, out document, out error))
        {
            document = null;
            Clear();
            error = string.IsNullOrWhiteSpace(error)
                ? "Cached Ansight host session is invalid and was cleared."
                : $"{error} Cached Ansight host session was cleared.";
            return false;
        }

        return true;
    }

    public void Save(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directoryPath = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var configDocument = PairingConfigDocumentService.CreateConfigDocument(document);
        var json = PairingConfigDocumentJson.Serialize(configDocument, indented: true);
        File.WriteAllText(cacheFilePath, json);
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
}
