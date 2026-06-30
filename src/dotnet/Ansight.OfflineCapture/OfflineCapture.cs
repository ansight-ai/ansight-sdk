namespace Ansight.OfflineCapture;

/// <summary>
/// Convenience access to a process-wide offline capture controller.
/// </summary>
public static class OfflineCapture
{
    private static readonly Lock sharedLock = new();
    private static OfflineCaptureController? shared;

    /// <summary>
    /// Gets or creates the shared offline capture controller.
    /// </summary>
    public static OfflineCaptureController Shared
    {
        get
        {
            lock (sharedLock)
            {
                return shared ??= new OfflineCaptureController();
            }
        }
    }

    /// <summary>
    /// Replaces the shared controller. Intended for apps that need custom options before first use.
    /// </summary>
    public static OfflineCaptureController Configure(OfflineCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (sharedLock)
        {
            shared = new OfflineCaptureController(options);
            return shared;
        }
    }
}
