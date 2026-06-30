namespace Ansight.OfflineCapture;

internal interface IOfflineCaptureArchiveEntryWriter
{
    Task WriteEntryAsync(
        string entryName,
        DateTimeOffset lastWriteTimeUtc,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken);
}
