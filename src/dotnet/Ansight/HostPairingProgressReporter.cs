namespace Ansight;

internal static class HostPairingProgressReporter
{
    public static void Report(
        IProgress<HostPairingProgressUpdate>? progress,
        HostPairingProgressKind kind,
        string message,
        bool isVerbose = false,
        HostPairingSource source = HostPairingSource.None,
        string? reasonCode = null)
    {
        if (progress is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        progress.Report(new HostPairingProgressUpdate(
            kind,
            message.Trim(),
            isVerbose,
            source,
            reasonCode));
    }
}
