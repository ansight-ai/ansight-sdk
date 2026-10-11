using System.Text;
using System.Text.Json;

namespace Ansight.Connectivity;

public sealed class ConnectivitySegmentIndex
{
    public required string Path { get; init; }
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public long RecordCount { get; set; }
}

public sealed class ConnectivityCaptureIndex
{
    public string Schema { get; init; } = "ansight.connectivity-index.v1";
    public IReadOnlyList<string> SupportedMeasurements { get; set; } = [];
    public IReadOnlyList<ConnectivitySegmentIndex> Segments { get; set; } = [];
    public long RetainedRecordCount { get; set; }
    public bool RetentionTrimmed { get; set; }
    public IReadOnlyList<ConnectivityRecord> StateChanges { get; set; } = [];
}

/// <summary>Single-writer append-only segments. Every observation carries its complete state.</summary>
public sealed class ConnectivityArchiveWriter : IAsyncDisposable
{
    private readonly string directory;
    private TimeSpan segmentDuration;
    private readonly List<ConnectivitySegmentIndex> segments = [];
    private readonly HashSet<string> supported = new(StringComparer.Ordinal);
    private StreamWriter? writer;
    private ConnectivitySegmentIndex? current;
    private ConnectivityRecord? previous;
    private readonly List<ConnectivityRecord> stateChanges = [];
    private long segmentNumber;
    private bool retentionTrimmed;

    public ConnectivityArchiveWriter(string directory, TimeSpan? segmentDuration = null)
    {
        this.directory = directory;
        this.segmentDuration = segmentDuration ?? TimeSpan.FromMinutes(1);
    }

    public void UpdateSegmentDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromSeconds(1)) segmentDuration = duration;
    }

    public string? CurrentPath => current is null ? null : System.IO.Path.Combine(directory, current.Path);
    public string IndexPath => System.IO.Path.Combine(directory, "index.json");

    public async Task WriteAsync(ConnectivityRecord record, CancellationToken cancellationToken = default)
    {
        var json = ConnectivityJson.Serialize(record);
        if (previous?.Id == record.Id) return;
        if (current is null || record.CapturedAtUtc - current.FromUtc >= segmentDuration || current.RecordCount >= 256)
        {
            if (writer is not null) await writer.DisposeAsync();
            Directory.CreateDirectory(System.IO.Path.Combine(directory, "segments"));
            current = new ConnectivitySegmentIndex
            {
                Path = $"segments/{++segmentNumber:D6}.jsonl",
                FromUtc = record.CapturedAtUtc, ToUtc = record.CapturedAtUtc
            };
            segments.Add(current);
            writer = new StreamWriter(new FileStream(CurrentPath!, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            // A preceding observation provides context when this segment is loaded on its own.
            // Preserve its identity and timestamp; readers deduplicate checkpoints by ID.
            if (previous is not null)
            {
                await writer.WriteLineAsync(ConnectivityJson.Serialize(previous).AsMemory(), cancellationToken);
                current.RecordCount++;
            }
        }
        foreach (var measurement in record.SupportedMeasurements) supported.Add(measurement);
        await writer!.WriteLineAsync(json.AsMemory(), cancellationToken);
        current.FromUtc = record.CapturedAtUtc < current.FromUtc ? record.CapturedAtUtc : current.FromUtc;
        current.ToUtc = record.CapturedAtUtc > current.ToUtc ? record.CapturedAtUtc : current.ToUtc;
        current.RecordCount++;
        if (record.Kind != "quality" || previous is null || previous.Kind == "gap") stateChanges.Add(record);
        previous = record;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (writer is null) return;
        await writer.FlushAsync(cancellationToken);
        retentionTrimmed |= segments.RemoveAll(segment => !File.Exists(System.IO.Path.Combine(directory, segment.Path))) > 0;
        if (segments.Count > 0)
        {
            var baseline = stateChanges.LastOrDefault(value => value.CapturedAtUtc < segments[0].FromUtc);
            stateChanges.RemoveAll(value => value.CapturedAtUtc < segments[0].FromUtc && value != baseline);
        }
        var index = new ConnectivityCaptureIndex
        {
            SupportedMeasurements = supported.Order(StringComparer.Ordinal).ToArray(),
            Segments = segments.ToArray(), RetainedRecordCount = segments.Sum(segment => segment.RecordCount) - Math.Max(0, segments.Count - 1),
            StateChanges = stateChanges.ToArray(),
            RetentionTrimmed = retentionTrimmed
        };
        var temporary = IndexPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(index, ConnectivityJson.Options), cancellationToken);
        File.Move(temporary, IndexPath, overwrite: true);
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();
        if (writer is not null) await writer.DisposeAsync();
        writer = null;
    }
}
