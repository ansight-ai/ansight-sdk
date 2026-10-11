using System.Text.Json;
using Ansight.Connectivity;
using Xunit;

namespace Ansight.Protocol.Tests;

public sealed class ConnectivityTests
{
    private static readonly DateTimeOffset origin = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    private static ConnectivityRecord Record(int sequence) => new()
    {
        Id = sequence.ToString(), ObservationId = "test", Sequence = sequence, CapturedAtUtc = origin.AddSeconds(sequence),
        ElapsedMilliseconds = sequence * 1000, Kind = "snapshot", Source = "test",
        Snapshot = new() { Availability = "available", Transports = ["wifi"] }, SupportedMeasurements = ["availability", "transports"]
    };

    [Fact]
    public void EncodingPreservesUnknownAndZeroWithoutInventingFalse()
    {
        var record = Record(1) with { Quality = new() { Source = "test", MeasuredAtUtc = origin, SignalStrength = 0, SignalStrengthUnit = "level", EstimatedDownstreamKbps = 0 } };
        var json = ConnectivityJson.Serialize(record);
        Assert.DoesNotContain("\"vpn\"", json);
        var parsed = Assert.IsType<ConnectivityRecord>(ConnectivityJson.Parse(json));
        Assert.Null(parsed.Snapshot.Vpn);
        Assert.Equal(0, parsed.Quality!.SignalStrength);
        Assert.Equal(0, parsed.Quality.EstimatedDownstreamKbps);
        Assert.Null(ConnectivityJson.Parse(json.Replace("\"wifi\"", "\"SSID\"")));
        Assert.Null(ConnectivityJson.Parse(json.Replace("\"snapshot\"", "\"change\"")));
        Assert.Null(ConnectivityJson.Parse(json.Replace("\"transports\":[\"wifi\"]", "\"transports\":null")));
        Assert.Null(ConnectivityJson.Parse("{}"));
    }

    [Fact]
    public async Task RotationAndRetentionKeepContextWithoutChangingItsTime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ansight-connectivity-" + Guid.NewGuid());
        try
        {
            await using var writer = new ConnectivityArchiveWriter(directory, TimeSpan.FromSeconds(2));
            await writer.WriteAsync(Record(1));
            await writer.WriteAsync(Record(2));
            await writer.WriteAsync(Record(3));
            await writer.FlushAsync();
            var files = Directory.GetFiles(Path.Combine(directory, "segments"), "*.jsonl").Order().ToArray();
            Assert.Equal(2, files.Length);
            var checkpoint = ConnectivityJson.Parse((await File.ReadAllLinesAsync(files[1]))[0]);
            Assert.Equal("2", checkpoint!.Id);
            Assert.Equal(origin.AddSeconds(2), checkpoint.CapturedAtUtc);
            File.Delete(files[0]);
            await writer.FlushAsync();
            var index = JsonSerializer.Deserialize<ConnectivityCaptureIndex>(await File.ReadAllTextAsync(writer.IndexPath), ConnectivityJson.Options)!;
            Assert.True(index.RetentionTrimmed);
            Assert.Single(index.Segments);
            Assert.Equal(2, index.RetainedRecordCount);
            Assert.Equal(new[] { "2", "3" }, index.StateChanges.Select(value => value.Id));
        }
        finally { Directory.Delete(directory, true); }
    }
}
