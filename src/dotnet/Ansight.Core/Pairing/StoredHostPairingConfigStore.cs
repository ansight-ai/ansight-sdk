namespace Ansight.Pairing;

internal sealed class StoredHostPairingConfigStore
{
    private readonly string filePath;

    public StoredHostPairingConfigStore(string cacheKey, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheKey));
        }

        this.filePath = string.IsNullOrWhiteSpace(filePath)
            ? ResolveDefaultFilePath(cacheKey)
            : Path.GetFullPath(filePath.Trim());
    }

    public bool HasStoredConfig => File.Exists(filePath);

    public bool TryLoad(out string? json, out string error)
    {
        json = null;
        error = string.Empty;

        if (!File.Exists(filePath))
        {
            error = "No saved Ansight host config is available.";
            return false;
        }

        try
        {
            json = File.ReadAllText(filePath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to read saved Ansight host config: {ex.Message}";
            return false;
        }
    }

    public void Save(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsSecureV2)
        {
            throw new InvalidOperationException("Protocol-v2 enrollment secrets must not be written to the legacy config store.");
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var configDocument = PairingConfigDocumentService.CreateConfigDocument(document);
        var json = PairingConfigDocumentJson.Serialize(configDocument, indented: true);
        File.WriteAllText(filePath, json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    internal string FilePath => filePath;

    internal static string ResolveDefaultFilePath(string cacheKey)
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
            $"{SanitizeFileName(cacheKey)}.preferred.json");
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
