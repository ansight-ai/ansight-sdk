using System.Text;

namespace Ansight.OfflineCapture;

internal sealed class SegmentedJsonLineWriter : IAsyncDisposable
{
    private readonly string directoryPath;
    private readonly string prefix;
    private TimeSpan segmentDuration;
    private FileStream? stream;
    private StreamWriter? writer;
    private DateTimeOffset segmentStartedAtUtc;
    private string? currentPath;

    public SegmentedJsonLineWriter(string directoryPath, string prefix, TimeSpan segmentDuration)
    {
        this.directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        this.prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        this.segmentDuration = segmentDuration;
    }

    public string? CurrentPath => currentPath;

    public void UpdateSegmentDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromSeconds(1))
        {
            segmentDuration = duration;
        }
    }

    public async Task WriteLineAsync(
        DateTimeOffset capturedAtUtc,
        string line,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        await EnsureWriterAsync(capturedAtUtc, cancellationToken);
        await writer!.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (writer is not null)
        {
            await writer.FlushAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (writer is not null)
        {
            await writer.FlushAsync();
            await writer.DisposeAsync();
        }

        if (stream is not null)
        {
            await stream.DisposeAsync();
        }
    }

    private async Task EnsureWriterAsync(DateTimeOffset capturedAtUtc, CancellationToken cancellationToken)
    {
        if (writer is not null && capturedAtUtc - segmentStartedAtUtc < segmentDuration)
        {
            return;
        }

        if (writer is not null)
        {
            await writer.FlushAsync(cancellationToken);
            await writer.DisposeAsync();
            writer = null;
        }

        if (stream is not null)
        {
            await stream.DisposeAsync();
            stream = null;
        }

        Directory.CreateDirectory(directoryPath);
        segmentStartedAtUtc = capturedAtUtc.ToUniversalTime();
        var fileName = $"{prefix}-{segmentStartedAtUtc:yyyyMMddHHmmssfff}.jsonl";
        currentPath = Path.Combine(directoryPath, fileName);
        stream = new FileStream(
            currentPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024);
    }
}
