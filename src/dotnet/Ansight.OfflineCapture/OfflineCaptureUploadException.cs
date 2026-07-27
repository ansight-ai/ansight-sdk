namespace Ansight.OfflineCapture;

/// <summary>
/// Reports an Ansight capture API or object-storage upload failure.
/// </summary>
public sealed class OfflineCaptureUploadException : Exception
{
    /// <summary>
    /// Creates an upload exception.
    /// </summary>
    public OfflineCaptureUploadException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// HTTP status returned by the remote endpoint, when available.
    /// </summary>
    public int? StatusCode { get; }
}
