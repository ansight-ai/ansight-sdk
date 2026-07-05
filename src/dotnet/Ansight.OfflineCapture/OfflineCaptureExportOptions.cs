namespace Ansight.OfflineCapture;

/// <summary>
/// Options used when exporting offline capture data.
/// </summary>
public sealed class OfflineCaptureExportOptions
{
    /// <summary>
    /// Optional password. When supplied, ZIP entries are encrypted with AES-256 through SharpZipLib on net9 targets.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional session id. When null, the latest session is exported.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Includes the .ansight folder name as the archive root.
    /// </summary>
    public bool IncludeRootDirectory { get; set; } = true;

    /// <summary>
    /// Retained for source compatibility. Offline capture exports no longer generate expanded Studio session entries.
    /// </summary>
    public bool IncludeStudioSessionArchive { get; set; } = true;

    /// <summary>
    /// Retained for source compatibility. Raw append-only .ansight capture files are always included.
    /// </summary>
    public bool IncludeRawCaptureFiles { get; set; } = true;

    internal bool UsePassword => !string.IsNullOrEmpty(Password);
}
