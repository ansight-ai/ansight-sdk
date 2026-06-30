namespace Ansight.OfflineCapture;

internal sealed class OfflineCaptureSettingsDocument
{
    public int Version { get; set; } = 1;

    public OfflineCaptureActivationMode ActivationMode { get; set; }

    public OfflineCaptureOptions Options { get; set; } = new();
}
