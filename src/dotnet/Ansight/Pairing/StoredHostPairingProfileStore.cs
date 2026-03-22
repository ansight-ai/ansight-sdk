namespace Ansight.Pairing;

internal sealed class StoredHostPairingProfileStore
{
    private readonly string filePath;

    public StoredHostPairingProfileStore(string cacheKey, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheKey));
        }

        this.filePath = string.IsNullOrWhiteSpace(filePath)
            ? ResolveDefaultFilePath(cacheKey)
            : Path.GetFullPath(filePath.Trim());
    }

    public bool HasStoredDocument => File.Exists(filePath);

    public bool TryLoad(out string? json, out string error)
    {
        json = null;
        error = string.Empty;

        if (!File.Exists(filePath))
        {
            error = "No saved Ansight pairing profile is available.";
            return false;
        }

        try
        {
            json = File.ReadAllText(filePath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to read saved Ansight pairing profile: {ex.Message}";
            return false;
        }
    }

    public void Save(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(filePath, PairingDocumentJson.Serialize(document, indented: true));
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
