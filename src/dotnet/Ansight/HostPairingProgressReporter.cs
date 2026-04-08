namespace Ansight;

internal static class HostPairingProgressReporter
{
    public static void Report(
        IProgress<StudioConnectionProgressUpdate>? progress,
        StudioConnectionProgressKind kind,
        string message,
        bool isVerbose = false,
        StudioConnectionSource source = StudioConnectionSource.None,
        string? reasonCode = null)
    {
        if (progress is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        progress.Report(new StudioConnectionProgressUpdate(
            kind,
            message.Trim(),
            isVerbose,
            source,
            reasonCode));
    }
}
