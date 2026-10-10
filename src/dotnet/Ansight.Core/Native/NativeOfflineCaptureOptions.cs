namespace Ansight.Native;

/// <summary>Native disconnected recording for Apple and Android. Storage, screenshots,
/// retention, recovery, ZIP export and host delivery are owned by the native SDK.</summary>
public sealed record NativeOfflineCaptureOptions
{
    public bool Enabled { get; init; } = true;
    public bool RecordingEnabled { get; init; } = true;
    public string? RootDirectory { get; init; }
    public int MaximumSessions { get; init; } = 10;
    public long MaximumSessionBytes { get; init; } = 128L * 1024 * 1024;
    public long MaximumRetainedBytes { get; init; } = 512L * 1024 * 1024;
    public int MaximumQueuedBytes { get; init; } = 8 * 1024 * 1024;
    public OfflineCaptureTransferMode TransferMode { get; init; } = OfflineCaptureTransferMode.Automatic;

    internal void Validate()
    {
        if (MaximumSessions <= 0 || MaximumSessionBytes <= 0 || MaximumRetainedBytes < MaximumSessionBytes || MaximumQueuedBytes <= 0 || !Enum.IsDefined(TransferMode))
            throw new ArgumentOutOfRangeException(nameof(MaximumSessions), "Offline capture requires positive limits and a total byte budget at least as large as one session.");
    }
}

public enum OfflineCaptureTransferMode { Automatic, Manual }
