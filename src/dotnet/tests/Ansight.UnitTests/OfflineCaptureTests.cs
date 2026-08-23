using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ansight.Input;
using Ansight.OfflineCapture;
using Ansight.Network;

namespace Ansight.UnitTests;

public sealed class OfflineCaptureTests
{
    [Fact]
    public async Task NetworkRequest_WritesOneRedactedDocumentPerRequest()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);

        var session = await controller.StartAsync();
        runtime.RecordNetworkRequest(new NetworkRequestRecord
        {
            Id = "request-001",
            Source = "test",
            StartedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-23T00:00:00.125Z"),
            DurationMilliseconds = 125,
            Method = "get",
            Url = "https://example.test/orders?token=secret&view=full",
            RequestHeaders =
            [
                new NetworkHeader { Name = "Authorization", Value = "Bearer secret" },
                new NetworkHeader { Name = "Accept", Value = "application/json" }
            ],
            StatusCode = 200
        });
        await controller.StopAsync();

        var requestFile = Assert.Single(Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "network", "requests"),
            "*.json"));
        Assert.EndsWith("-request-001.json", requestFile, StringComparison.Ordinal);
        using var document = await ReadJsonDocumentAsync(requestFile);
        Assert.Equal("ansight.network-request.v1", document.RootElement.GetProperty("Schema").GetString());
        Assert.Equal("GET", document.RootElement.GetProperty("Method").GetString());
        Assert.Contains("token=%3Credacted%3E", document.RootElement.GetProperty("Url").GetString());
        Assert.Equal("<redacted>", document.RootElement.GetProperty("RequestHeaders")[0].GetProperty("Value").GetString());

        using var manifest = await ReadJsonDocumentAsync(Path.Combine(session.DirectoryPath, "manifest.json"));
        Assert.Equal(1, manifest.RootElement.GetProperty("NetworkRequestCount").GetInt64());
    }

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

        using (var manifest = await ReadJsonDocumentAsync(Path.Combine(session.DirectoryPath, "manifest.json")))
        {
            Assert.Equal("normal", manifest.RootElement.GetProperty("TerminationKind").GetString());
            Assert.False(string.IsNullOrWhiteSpace(manifest.RootElement.GetProperty("ProcessSessionId").GetString()));
            Assert.Empty(manifest.RootElement.GetProperty("CrashReportIds").EnumerateArray());
        }

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
                MaxWidth = 320,
                CaptureGpuBackedSurfaces = false
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
        Assert.False(optionsSnapshot.SessionJpegCaptureOverride.CaptureGpuBackedSurfaces);

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

    [Fact]
    public void UploadOptions_DefaultEndpoint_UsesBrandedAppRoute()
    {
        Assert.Equal(
            new Uri("https://app.ansight.ai/submit_capture"),
            new OfflineCaptureUploadOptions().Endpoint);
    }

    [Fact]
    public async Task UploadArchiveAsync_UsesScopedKeySignedUploadAndFinalizes()
    {
        using var tempDirectory = new TemporaryDirectory();
        var archivePath = Path.Combine(tempDirectory.Path, "capture.zip");
        var archiveBytes = "PK-test-offline-capture"u8.ToArray();
        await File.WriteAllBytesAsync(archivePath, archiveBytes);
        var handler = new CaptureUploadHandler(archiveBytes);
        using var httpClient = new HttpClient(handler);
        var uploader = new OfflineCaptureUploader(httpClient);
        var progress = new UploadProgressCollector();

        var result = await uploader.UploadArchiveAsync(
            archivePath,
            new OfflineCaptureUploadMetadata(
                "session-123",
                "com.example.app",
                DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-27T00:01:00Z"),
                "1.2.3"),
            new OfflineCaptureUploadOptions
            {
                ApiKey = "an_cap_test_key_that_is_long_enough",
                Endpoint = new Uri("https://api.example.test/capture-upload"),
                Title = "Checkout capture",
                RetryDelay = TimeSpan.Zero
            },
            progress);

        Assert.Equal("upload-123", result.UploadId);
        Assert.Equal("session-row-123", result.SessionId);
        Assert.Equal(new Uri("https://app.ansight.ai/session/session-row-123"), result.SessionUrl);
        Assert.Equal(archiveBytes.Length, result.ArchiveByteSize);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(archiveBytes, handler.UploadedBytes);
        Assert.All(handler.ApiRequests, request =>
            Assert.Equal("an_cap_test_key_that_is_long_enough", request.Authorization));
        Assert.Contains(progress.Updates, update => update.Stage == OfflineCaptureUploadStage.Hashing);
        Assert.Contains(progress.Updates, update => update.Stage == OfflineCaptureUploadStage.Uploading);
        Assert.Equal(OfflineCaptureUploadStage.Completed, progress.Updates[^1].Stage);
    }

    [Fact]
    public async Task UploadAsync_RejectsActiveCaptureBeforeNetworkRequest()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runtime = CreateRuntime();
        await using var controller = CreateController(runtime, tempDirectory.Path);
        var session = await controller.StartAsync();
        var handler = new RejectUnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.UploadAsync(
                new OfflineCaptureUploadOptions
                {
                    ApiKey = "an_cap_test_key_that_is_long_enough",
                    SessionId = session.SessionId
                },
                new OfflineCaptureUploader(httpClient)));

        Assert.Contains("Stop the offline capture", error.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UploadArchiveAsync_RequiresCapturePackageId()
    {
        using var tempDirectory = new TemporaryDirectory();
        var archivePath = Path.Combine(tempDirectory.Path, "capture.zip");
        await File.WriteAllBytesAsync(archivePath, "PK-test"u8.ToArray());
        var handler = new RejectUnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new OfflineCaptureUploader(httpClient).UploadArchiveAsync(
                archivePath,
                new OfflineCaptureUploadMetadata("session-123", string.Empty),
                new OfflineCaptureUploadOptions
                {
                    ApiKey = "an_cap_test_key_that_is_long_enough"
                }));

        Assert.Contains("app ID (package ID)", error.Message);
        Assert.Equal(0, handler.RequestCount);
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

    private sealed class UploadProgressCollector : IProgress<OfflineCaptureUploadProgress>
    {
        public List<OfflineCaptureUploadProgress> Updates { get; } = [];

        public void Report(OfflineCaptureUploadProgress value)
        {
            Updates.Add(value);
        }
    }

    private sealed class CaptureUploadHandler(byte[] expectedArchiveBytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public byte[]? UploadedBytes { get; private set; }

        public List<(string? Authorization, string Body)> ApiRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri == new Uri("https://storage.example.test/signed-upload"))
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                UploadedBytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(expectedArchiveBytes, UploadedBytes);
                return CreateJsonResponse(HttpStatusCode.OK, new { Key = "capture.zip" });
            }

            Assert.Equal(new Uri("https://api.example.test/capture-upload"), request.RequestUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiRequests.Add((request.Headers.Authorization?.Parameter, body));
            using var document = JsonDocument.Parse(body);
            var action = document.RootElement.GetProperty("action").GetString();
            if (action == "create")
            {
                Assert.Equal(
                    "com.example.app",
                    document.RootElement.GetProperty("capture").GetProperty("appId").GetString());
            }
            return action switch
            {
                "create" => CreateJsonResponse(HttpStatusCode.Created, new
                {
                    upload = new
                    {
                        id = "upload-123",
                        status = "pending_upload",
                        byteSize = expectedArchiveBytes.Length,
                        expiresAtUtc = "2026-07-27T02:00:00Z"
                    },
                    uploadUrl = "https://storage.example.test/signed-upload"
                }),
                "complete" => CreateJsonResponse(HttpStatusCode.OK, new
                {
                    upload = new
                    {
                        id = "upload-123",
                        status = "completed"
                    },
                    sessionId = "session-row-123",
                    sessionUrl = "https://app.ansight.ai/session/session-row-123"
                }),
                _ => throw new InvalidOperationException($"Unexpected action: {action}")
            };
        }

        private static HttpResponseMessage CreateJsonResponse(
            HttpStatusCode statusCode,
            object body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(body)
            };
        }
    }

    private sealed class RejectUnexpectedRequestHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("No HTTP request was expected.");
        }
    }
}
