using System.IO.Compression;
using System.Text.Json;
using Ansight.Input;
using Ansight.OfflineCapture;

namespace Ansight.UnitTests;

public sealed class OfflineCaptureTests
{
    [Fact]
    public async Task StartStop_WritesMinifiedTelemetryData()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);

        var session = await controller.StartAsync();
        runtime.Metric(42, Constants.ReservedChannels.FramesPerSecond_Id);
        runtime.Event("checkout_started", AppEventType.Info, Constants.ReservedChannels.ChannelNotSpecified_Id, "cart");
        await controller.StopAsync();

        var metricFile = Assert.Single(Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "telemetry", "metrics"),
            "*.jsonl"));
        var eventFile = Assert.Single(Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "telemetry", "events"),
            "*.jsonl"));
        var metricLine = Assert.Single(await File.ReadAllLinesAsync(metricFile));
        var eventLine = Assert.Single(await File.ReadAllLinesAsync(eventFile));

        Assert.DoesNotContain(" ", metricLine);
        Assert.DoesNotContain("capturedAtUtc", metricLine);
        Assert.Contains("\"t\":", metricLine);
        Assert.Contains("\"c\":3", metricLine);
        Assert.Contains("\"v\":42", metricLine);

        Assert.DoesNotContain(" ", eventLine);
        Assert.DoesNotContain("capturedAtUtc", eventLine);
        Assert.Contains("\"l\":\"checkout_started\"", eventLine);
        Assert.Contains("\"d\":\"cart\"", eventLine);
    }

    [Fact]
    public async Task UpdateOptionsAsync_MutatesActiveCaptureSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);

        var session = await controller.StartAsync();
        await controller.UpdateOptionsAsync(options =>
        {
            options.RetentionWindowOverride = TimeSpan.FromSeconds(5);
            options.MaximumSessionBytes = 8 * 1024 * 1024;
            options.SegmentDuration = TimeSpan.FromSeconds(2);
            options.SessionJpegCaptureEnabledOverride = true;
            options.SessionJpegCaptureOverride = new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = 500,
                Quality = 55,
                MaxWidth = 320
            };
        });

        var optionsSnapshot = controller.Options;
        Assert.Equal(TimeSpan.FromSeconds(5), optionsSnapshot.RetentionWindowOverride);
        Assert.Equal(8 * 1024 * 1024, optionsSnapshot.MaximumSessionBytes);
        Assert.Equal(TimeSpan.FromSeconds(2), optionsSnapshot.SegmentDuration);
        Assert.True(optionsSnapshot.SessionJpegCaptureEnabledOverride);
        Assert.NotNull(optionsSnapshot.SessionJpegCaptureOverride);
        Assert.Equal(500, optionsSnapshot.SessionJpegCaptureOverride.IntervalMilliseconds);
        Assert.Equal(55, optionsSnapshot.SessionJpegCaptureOverride.Quality);
        Assert.Equal(320, optionsSnapshot.SessionJpegCaptureOverride.MaxWidth);

        using var manifest = await ReadJsonDocumentAsync(Path.Combine(session.DirectoryPath, "manifest.json"));
        Assert.True(manifest.RootElement.GetProperty("SessionJpegCaptureEnabled").GetBoolean());
        Assert.Equal(500, manifest.RootElement.GetProperty("SessionJpegCaptureIntervalMilliseconds").GetInt32());
        Assert.Equal(55, manifest.RootElement.GetProperty("SessionJpegCaptureQuality").GetInt32());
        Assert.Equal(320, manifest.RootElement.GetProperty("SessionJpegCaptureMaxWidth").GetInt32());
        Assert.Equal(8 * 1024 * 1024, manifest.RootElement.GetProperty("MaximumSessionBytes").GetInt64());
    }

    [Fact]
    public async Task StartAsync_UsesRuntimeCaptureOptionsByDefault()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = new RuntimeImpl(Options.CreateBuilder()
            .WithFramesPerSecond()
            .WithRetentionPeriodSeconds(180)
            .WithSessionJpegCapture(750, 55, 320)
            .Build());
        await using var controller = new OfflineCaptureController(runtime, new OfflineCaptureOptions
        {
            RootDirectory = Path.Combine(tempDirectory.Path, ".ansight"),
            SegmentDuration = TimeSpan.FromSeconds(1)
        });

        var session = await controller.StartAsync();

        using var manifest = await ReadJsonDocumentAsync(Path.Combine(session.DirectoryPath, "manifest.json"));
        Assert.Equal(TimeSpan.FromSeconds(180), TimeSpan.Parse(manifest.RootElement.GetProperty("RetentionWindow").GetString()!));
        Assert.True(manifest.RootElement.GetProperty("SessionJpegCaptureEnabled").GetBoolean());
        Assert.Equal(750, manifest.RootElement.GetProperty("SessionJpegCaptureIntervalMilliseconds").GetInt32());
        Assert.Equal(55, manifest.RootElement.GetProperty("SessionJpegCaptureQuality").GetInt32());
        Assert.Equal(320, manifest.RootElement.GetProperty("SessionJpegCaptureMaxWidth").GetInt32());
    }

    [Fact]
    public async Task NextSessionOnly_StartsOnInitializeAndClearsFutureActivation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();

        await using (var firstController = CreateController(runtime, tempDirectory.Path))
        {
            await firstController.SetActivationModeAsync(OfflineCaptureActivationMode.NextSessionOnly);
            Assert.False(firstController.IsCapturing);
        }

        await using var secondController = CreateController(runtime, tempDirectory.Path);
        await secondController.InitializeAsync();

        Assert.True(secondController.IsCapturing);
        Assert.Equal(OfflineCaptureActivationMode.Disabled, secondController.Options.ActivationMode);
    }

    [Fact]
    public async Task ExportToFile_CreatesZipArchive()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        runtime.RegisterCustomProperty("scenario", "mode", "offline");
        await using var controller = CreateController(runtime, tempDirectory.Path);

        var session = await controller.StartAsync();
        runtime.Metric(99, Constants.ReservedChannels.FramesPerSecond_Id);
        runtime.Event("export_warning", AppEventType.Warning, Constants.ReservedChannels.ChannelNotSpecified_Id, "needs-review");
        runtime.TouchCaptureHub.Record(CreateTouch());
        var archivePath = Path.Combine(tempDirectory.Path, "capture.zip");
        await controller.ExportToFileAsync(archivePath);

        Assert.True(File.Exists(archivePath));
        using var archive = ZipFile.OpenRead(archivePath);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "session.json");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("session-data/", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("session-images/", StringComparison.Ordinal));

        var archiveRoot = $".ansight/sessions/{session.SessionId}/";
        Assert.Contains(archive.Entries, entry => entry.FullName == $"{archiveRoot}manifest.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == $"{archiveRoot}metadata/channels.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == $"{archiveRoot}metadata/custom-properties.json");

        var metricEntry = Assert.Single(archive.Entries, entry =>
            entry.FullName.StartsWith($"{archiveRoot}telemetry/metrics/m-", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));
        var eventEntry = Assert.Single(archive.Entries, entry =>
            entry.FullName.StartsWith($"{archiveRoot}telemetry/events/e-", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));
        var touchEntry = Assert.Single(archive.Entries, entry =>
            entry.FullName.StartsWith($"{archiveRoot}input/touches/t-", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));

        var metricLine = Assert.Single((await ReadArchiveEntryTextAsync(metricEntry)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("capturedAtUtc", metricLine);
        Assert.Contains("\"c\":3", metricLine);
        Assert.Contains("\"v\":99", metricLine);

        var eventLine = Assert.Single((await ReadArchiveEntryTextAsync(eventEntry)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("message", eventLine);
        Assert.Contains("\"l\":\"export_warning\"", eventLine);
        Assert.Contains("\"d\":\"needs-review\"", eventLine);

        var touchLine = Assert.Single((await ReadArchiveEntryTextAsync(touchEntry)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("rows", touchLine);
        Assert.Contains("\"a\":0", touchLine);
        Assert.Contains("\"p\":1", touchLine);
    }

    [Fact]
    public async Task UpdateOptionsAsync_AppliesActiveRetentionWindow()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);

        var session = await controller.StartAsync();
        runtime.Metric(1, Constants.ReservedChannels.FramesPerSecond_Id);
        await Task.Delay(1500);
        runtime.Metric(2, Constants.ReservedChannels.FramesPerSecond_Id);
        await controller.UpdateOptionsAsync(options =>
        {
            options.RetentionWindowOverride = TimeSpan.FromSeconds(1);
        });

        var metricFiles = Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "telemetry", "metrics"),
            "*.jsonl");
        Assert.Single(metricFiles);
        var metricLine = Assert.Single(await File.ReadAllLinesAsync(metricFiles[0]));
        Assert.Contains("\"v\":2", metricLine);
    }

    [Fact]
    public async Task ExportToStream_WithPassword_WritesArchiveBytes()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);

        await controller.StartAsync();
        runtime.Event("export_password_test");
        using var stream = new MemoryStream();
        await controller.ExportToStreamAsync(stream, new OfflineCaptureExportOptions
        {
            Password = "test-password"
        });

        Assert.True(stream.Length > 0);
        stream.Position = 0;
        Assert.Equal((byte)'P', stream.ReadByte());
        Assert.Equal((byte)'K', stream.ReadByte());
    }

    private static RuntimeImpl CreateRuntime()
    {
        return new RuntimeImpl(Options.CreateBuilder()
            .WithFramesPerSecond()
            .WithRetentionPeriodSeconds(120)
            .Build());
    }

    private static OfflineCaptureController CreateController(RuntimeImpl runtime, string rootDirectory)
    {
        return new OfflineCaptureController(runtime, new OfflineCaptureOptions
        {
            RootDirectory = Path.Combine(rootDirectory, ".ansight"),
            SegmentDuration = TimeSpan.FromSeconds(1)
        });
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<string> ReadArchiveEntryTextAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static CapturedTouch CreateTouch()
    {
        return new CapturedTouch(
            CapturedTouchAction.Down,
            pointerId: 1,
            pointerIndex: 0,
            pointerCount: 1,
            x: 10,
            y: 20,
            surfaceWidth: 100,
            surfaceHeight: 200,
            coordinateUnit: "pixels",
            surfaceScale: 2,
            capturedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ansight-offline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
