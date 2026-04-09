namespace Ansight;

internal static class HostPairingProgressReporter
{
    public static void Report(
        IProgress<HostConnectionProgressUpdate>? progress,
        HostConnectionProgressKind kind,
        string message,
        bool isVerbose = false,
        HostConnectionSource source = HostConnectionSource.None,
        string? reasonCode = null)
    {
        if (progress is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        progress.Report(new HostConnectionProgressUpdate(
            kind,
            message.Trim(),
            isVerbose,
            source,
            reasonCode));
    }
}
