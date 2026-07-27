namespace Ansight.OfflineCapture;

/// <summary>
/// A stage in the offline capture export and upload pipeline.
/// </summary>
public enum OfflineCaptureUploadStage
{
    /// <summary>The capture is being exported to a temporary ZIP archive.</summary>
    Exporting,

    /// <summary>The archive SHA-256 digest is being calculated.</summary>
    Hashing,

    /// <summary>The SDK is creating an idempotent upload receipt.</summary>
    CreatingUpload,

    /// <summary>The archive bytes are being written to signed object storage.</summary>
    Uploading,

    /// <summary>The uploaded object is being finalized as a team session.</summary>
    Finalizing,

    /// <summary>The team session is available.</summary>
    Completed
}
