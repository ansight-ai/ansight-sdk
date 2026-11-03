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
    public int? ChunkDurationSeconds { get; init; }
    public int MaximumChunkRecords { get; init; } = 10_000;

    public static NativeOfflineCaptureOptions ForCloudUploads(int chunkDurationSeconds = 10)
    {
        return new NativeOfflineCaptureOptions
        {
            ChunkDurationSeconds = chunkDurationSeconds,
            MaximumSessionBytes = 4L * 1024 * 1024,
            MaximumSessions = 512,
            TransferMode = OfflineCaptureTransferMode.Manual
        };
    }

    internal void Validate()
    {
        var invalidStorageLimits = MaximumSessions <= 0
            || MaximumSessionBytes <= 0
            || MaximumRetainedBytes < MaximumSessionBytes
            || MaximumQueuedBytes <= 0
            || !Enum.IsDefined(TransferMode);

        if (invalidStorageLimits)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSessions), "Offline capture requires positive limits and a total byte budget at least as large as one session.");
        }

        if (ChunkDurationSeconds is { } duration)
        {
            var invalidChunkLimits = duration is < 1 or > 300
                || MaximumSessionBytes > 4L * 1024 * 1024
                || MaximumChunkRecords is < 1 or > 10_000;

            if (invalidChunkLimits)
            {
                throw new ArgumentOutOfRangeException(nameof(ChunkDurationSeconds), "Cloud chunks require 1–300 seconds and at most 4 MiB / 10000 records.");
            }
        }
    }
}

public enum OfflineCaptureTransferMode { Automatic, Manual }
