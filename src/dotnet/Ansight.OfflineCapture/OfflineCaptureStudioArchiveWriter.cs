using System.Text.Encodings.Web;
using Ansight.Pairing;

namespace Ansight.OfflineCapture;

internal static class OfflineCaptureStudioArchiveWriter
{
    private const string SessionEntryPath = "session.json";
    private const string LogsEntryPath = "session-data/logs.json";
    private const string TelemetryEntryPath = "session-data/telemetry.json";
    private const string TouchesEntryPath = "session-data/touches.json";
    private const string SessionImagesDirectoryName = "session-images";
    private const string SessionCaptureSchemaName = "ansight.session-capture.v1";
    private const string SessionArchiveLogsSchemaName = "ansight.session-archive-logs.v1";
    private const string SessionArchiveTelemetrySchemaName = "ansight.session-archive-telemetry.v1";
    private const string SessionTouchesSchemaName = "ansight.touches.v1";

    private static readonly JsonSerializerOptions StudioJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions DeviceProfileReadOptions = new(PairingJson.Compact)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task WriteAsync(
        string sessionDirectory,
        IOfflineCaptureArchiveEntryWriter archive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentNullException.ThrowIfNull(archive);

        var savedAtUtc = DateTimeOffset.UtcNow;
        var context = await CreateContextAsync(sessionDirectory, savedAtUtc, cancellationToken);

        await archive.WriteEntryAsync(
            SessionEntryPath,
            savedAtUtc,
            (stream, token) => JsonSerializer.SerializeAsync(
                stream,
                CreateSessionDocument(context),
                StudioJsonOptions,
                token),
            cancellationToken);

        await archive.WriteEntryAsync(
            LogsEntryPath,
            savedAtUtc,
            (stream, token) => WriteLogsAsync(sessionDirectory, stream, savedAtUtc, token),
            cancellationToken);

        await archive.WriteEntryAsync(
            TelemetryEntryPath,
            savedAtUtc,
            (stream, token) => WriteTelemetryAsync(sessionDirectory, context.Channels, stream, savedAtUtc, token),
            cancellationToken);

        await archive.WriteEntryAsync(
            TouchesEntryPath,
            savedAtUtc,
            (stream, token) => WriteTouchesAsync(sessionDirectory, stream, savedAtUtc, token),
            cancellationToken);

        foreach (var image in context.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await archive.WriteEntryAsync(
                ResolveImageEntryPath(image.Frame),
                File.GetLastWriteTimeUtc(image.SourcePath),
                async (stream, token) =>
                {
                    await using var fileStream = new FileStream(
                        image.SourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await fileStream.CopyToAsync(stream, token);
                },
                cancellationToken);
        }
    }

    private static async Task<StudioArchiveContext> CreateContextAsync(
        string sessionDirectory,
        DateTimeOffset savedAtUtc,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(sessionDirectory, cancellationToken);
        var deviceProfile = await ReadDeviceProfileAsync(sessionDirectory, cancellationToken);
        var customProperties = await ReadCustomPropertiesAsync(sessionDirectory, cancellationToken);
        var channels = await ReadChannelsAsync(sessionDirectory, cancellationToken);
        var images = await ReadImagesAsync(sessionDirectory, cancellationToken);
        var timelineEndUtc = ResolveTimelineEnd(manifest, images, savedAtUtc);
        return new StudioArchiveContext(
            sessionDirectory,
            manifest,
            deviceProfile.Profile,
            deviceProfile.Json,
            customProperties,
            channels,
            images,
            timelineEndUtc,
            GetDirectorySize(sessionDirectory));
    }

    private static StudioSessionCaptureDocument CreateSessionDocument(StudioArchiveContext context)
    {
        var manifest = context.Manifest;
        var appId = ResolveFirstNonEmpty(
            manifest.AppId,
            context.DeviceProfile?.App?.AppId,
            "offline-capture")!;
        var clientName = ResolveFirstNonEmpty(
            manifest.ClientName,
            context.DeviceProfile?.App?.AppName,
            context.DeviceProfile?.App?.AppId,
            appId,
            "Offline Capture")!;
        var remoteAddress = ResolveFirstNonEmpty(manifest.RemoteAddress, "offline")!;

        return new StudioSessionCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Author = null,
            Session = new StudioAppSessionSnapshot
            {
                SessionId = ResolveFirstNonEmpty(manifest.SessionId, Path.GetFileName(context.SessionDirectory))!,
                AppId = appId,
                ClientName = clientName,
                RemoteAddress = remoteAddress,
                CreatedUtc = manifest.StartedAtUtc,
                ConfigId = null,
                ProcessSessionId = manifest.ProcessSessionId,
                Status = "Imported",
                LastUpdatedUtc = context.TimelineEndUtc,
                IsHistorical = true,
                CacheSizeBytes = context.CacheSizeBytes,
                IsPinned = false,
                Author = null,
                ReplaySource = null,
                SdkVersion = ResolveFirstNonEmpty(manifest.SdkVersion, context.DeviceProfile?.Sdk?.Version),
                Name = null,
                Tags = ["offline-capture"],
                Notes = null,
                CustomProperties = context.CustomProperties.Count == 0 ? null : context.CustomProperties,
                AppState = manifest.AppState,
                AppStateChangedUtc = manifest.AppStateChangedUtc,
                DeviceProfile = context.DeviceProfile,
                DeviceProfileJson = context.DeviceProfileJson,
                AppIcon = null,
                Analyses = [],
                Annotations = [],
                Images = context.Images.Select(image => image.Frame).ToArray(),
                Touches = [],
                VisualTreeSnapshots = [],
                ArtifactSnapshots = [],
                Logs = [],
                MetricChannels = [],
                Metrics = []
            }
        };
    }

    private static async Task WriteLogsAsync(
        string sessionDirectory,
        Stream stream,
        DateTimeOffset savedAtUtc,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        writer.WriteString("schema", SessionArchiveLogsSchemaName);
        writer.WriteString("savedAtUtc", savedAtUtc);
        writer.WritePropertyName("logs");
        writer.WriteStartArray();

        foreach (var filePath in EnumerateJsonLineFiles(OfflineCapturePaths.EventsDirectory(sessionDirectory)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 32 * 1024,
                FileOptions.SequentialScan));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!TryReadEventLog(line, out var log))
                {
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteString("timestampUtc", log.TimestampUtc);
                writer.WriteString("message", log.Message);
                writer.WriteNumber("priority", log.Priority);
                writer.WriteString("source", log.Source);
                writer.WriteString("tag", log.Tag);
                writer.WriteString("eventId", log.EventId);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteTelemetryAsync(
        string sessionDirectory,
        IReadOnlyList<StudioMetricChannel> channels,
        Stream stream,
        DateTimeOffset savedAtUtc,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        writer.WriteString("schema", SessionArchiveTelemetrySchemaName);
        writer.WriteString("savedAtUtc", savedAtUtc);
        writer.WritePropertyName("channels");
        JsonSerializer.Serialize(writer, channels, StudioJsonOptions);
        writer.WritePropertyName("metrics");
        writer.WriteStartArray();

        foreach (var filePath in EnumerateJsonLineFiles(OfflineCapturePaths.MetricsDirectory(sessionDirectory)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 32 * 1024,
                FileOptions.SequentialScan));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!TryReadMetric(line, out var metric))
                {
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteNumber("channelId", metric.ChannelId);
                writer.WriteNumber("value", metric.Value);
                writer.WriteString("capturedAtUtc", metric.CapturedAtUtc);
                writer.WriteNumber("segmentId", 0);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteTouchesAsync(
        string sessionDirectory,
        Stream stream,
        DateTimeOffset savedAtUtc,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        writer.WriteString("schema", SessionTouchesSchemaName);
        writer.WriteString("savedAtUtc", savedAtUtc);
        writer.WritePropertyName("batches");
        writer.WriteStartArray();

        StudioTouchBatchAccumulator? batch = null;
        foreach (var filePath in EnumerateJsonLineFiles(OfflineCapturePaths.TouchesDirectory(sessionDirectory)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 32 * 1024,
                FileOptions.SequentialScan));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!TryReadTouch(line, out var touch))
                {
                    continue;
                }

                if (batch is null || !batch.CanAdd(touch))
                {
                    batch?.WriteTo(writer);
                    batch = new StudioTouchBatchAccumulator(touch);
                }

                batch.Add(touch);
            }
        }

        batch?.WriteTo(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task<OfflineCaptureSessionManifest> ReadManifestAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = OfflineCapturePaths.ManifestPath(sessionDirectory);
        if (!File.Exists(manifestPath))
        {
            return new OfflineCaptureSessionManifest
            {
                SessionId = Path.GetFileName(sessionDirectory),
                StartedAtUtc = Directory.GetCreationTimeUtc(sessionDirectory)
            };
        }

        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<OfflineCaptureSessionManifest>(
                   stream,
                   OfflineCaptureJson.Metadata,
                   cancellationToken)
               ?? new OfflineCaptureSessionManifest
               {
                   SessionId = Path.GetFileName(sessionDirectory),
                   StartedAtUtc = Directory.GetCreationTimeUtc(sessionDirectory)
               };
    }

    private static async Task<StudioDeviceProfileReadResult> ReadDeviceProfileAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var path = OfflineCapturePaths.DeviceProfilePath(sessionDirectory);
        if (!File.Exists(path))
        {
            return new StudioDeviceProfileReadResult(null, null);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StudioDeviceProfileReadResult(null, null);
        }

        try
        {
            var profile = JsonSerializer.Deserialize<DeviceAppProfile>(json, DeviceProfileReadOptions);
            if (profile is null)
            {
                return new StudioDeviceProfileReadResult(null, null);
            }

            return new StudioDeviceProfileReadResult(profile, JsonSerializer.Serialize(profile, PairingJson.Compact));
        }
        catch (JsonException)
        {
            return new StudioDeviceProfileReadResult(null, null);
        }
    }

    private static async Task<JsonObject> ReadCustomPropertiesAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var path = OfflineCapturePaths.CustomPropertiesPath(sessionDirectory);
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            return await JsonSerializer.DeserializeAsync<JsonObject>(
                       stream,
                       OfflineCaptureJson.Data,
                       cancellationToken)
                   ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static async Task<IReadOnlyList<StudioMetricChannel>> ReadChannelsAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var path = OfflineCapturePaths.ChannelsPath(sessionDirectory);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("ch", out var channelsElement)
            || channelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var channels = new List<StudioMetricChannel>();
        foreach (var channelElement in channelsElement.EnumerateArray())
        {
            if (!channelElement.TryGetProperty("id", out var idElement)
                || !idElement.TryGetByte(out var id))
            {
                continue;
            }

            channels.Add(new StudioMetricChannel
            {
                ChannelId = id,
                Name = ReadString(channelElement, "n") ?? $"Channel {id}",
                ColorHex = ReadString(channelElement, "c") ?? "#64748B",
                Type = "custom"
            });
        }

        return channels;
    }

    private static async Task<IReadOnlyList<StudioImageArchiveFrame>> ReadImagesAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var frames = new List<StudioImageArchiveFrame>();
        var frameIds = new HashSet<string>(StringComparer.Ordinal);
        var frameIndex = 0;

        foreach (var filePath in EnumerateJsonLineFiles(OfflineCapturePaths.ScreenshotIndexDirectory(sessionDirectory)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 32 * 1024,
                FileOptions.SequentialScan));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!TryReadImage(sessionDirectory, line, frameIds, ++frameIndex, out var image))
                {
                    continue;
                }

                frames.Add(image);
            }
        }

        return frames
            .OrderBy(image => image.Frame.CapturedAtUtc)
            .ThenBy(image => image.Frame.FrameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryReadMetric(string line, out StudioMetricSample metric)
    {
        metric = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("t", out var timeElement)
                || !timeElement.TryGetInt64(out var unixMilliseconds)
                || !root.TryGetProperty("c", out var channelElement)
                || !channelElement.TryGetByte(out var channelId)
                || !root.TryGetProperty("v", out var valueElement)
                || !valueElement.TryGetInt64(out var value))
            {
                return false;
            }

            metric = new StudioMetricSample(
                channelId,
                value,
                DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadEventLog(string line, out StudioLogEntry log)
    {
        log = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("t", out var timeElement)
                || !timeElement.TryGetInt64(out var unixMilliseconds))
            {
                return false;
            }

            var label = ReadString(root, "l") ?? "Event";
            var details = ReadString(root, "d");
            var type = root.TryGetProperty("k", out var typeElement) && typeElement.TryGetInt32(out var typeValue)
                ? typeValue
                : 0;
            log = new StudioLogEntry(
                DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds),
                string.IsNullOrWhiteSpace(details) ? label : $"{label}:{details}",
                ResolveLogPriority(type),
                "offline-capture",
                ResolveEventTypeName(type),
                ReadString(root, "id"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadTouch(string line, out StudioTouchRecord touch)
    {
        touch = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("t", out var timeElement)
                || !timeElement.TryGetInt64(out var unixMilliseconds)
                || !root.TryGetProperty("a", out var actionElement)
                || !actionElement.TryGetInt32(out var action)
                || !root.TryGetProperty("p", out var pointerElement)
                || !pointerElement.TryGetInt64(out var pointerId)
                || !root.TryGetProperty("x", out var xElement)
                || !xElement.TryGetDouble(out var x)
                || !root.TryGetProperty("y", out var yElement)
                || !yElement.TryGetDouble(out var y))
            {
                return false;
            }

            touch = new StudioTouchRecord(
                DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds),
                Math.Clamp(action, 0, 4),
                pointerId,
                ReadInt(root, "i") ?? 0,
                ReadInt(root, "pc") ?? 1,
                x,
                y,
                ReadDouble(root, "w"),
                ReadDouble(root, "h"),
                EncodeUnit(ReadString(root, "u")),
                ReadDouble(root, "s"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadImage(
        string sessionDirectory,
        string line,
        ISet<string> frameIds,
        int frameIndex,
        out StudioImageArchiveFrame image)
    {
        image = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("t", out var timeElement)
                || !timeElement.TryGetInt64(out var unixMilliseconds))
            {
                return false;
            }

            var relativePath = ReadString(root, "p");
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var sourcePath = Path.Combine(
                sessionDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath))
            {
                return false;
            }

            var frameId = CreateUniqueFrameId(relativePath, frameIds, frameIndex);
            var fileInfo = new FileInfo(sourcePath);
            image = new StudioImageArchiveFrame(
                sourcePath,
                new StudioImageFrame
                {
                    FrameId = frameId,
                    CapturedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds),
                    Format = "jpeg",
                    Width = ReadInt(root, "w") ?? 0,
                    Height = ReadInt(root, "h") ?? 0,
                    Quality = ReadInt(root, "q") ?? 0,
                    ByteCount = ReadLong(root, "b") ?? fileInfo.Length
                });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset ResolveTimelineEnd(
        OfflineCaptureSessionManifest manifest,
        IReadOnlyList<StudioImageArchiveFrame> images,
        DateTimeOffset fallbackUtc)
    {
        var latest = manifest.StoppedAtUtc ?? manifest.StartedAtUtc;
        foreach (var image in images)
        {
            if (image.Frame.CapturedAtUtc > latest)
            {
                latest = image.Frame.CapturedAtUtc;
            }
        }

        return latest == default ? fallbackUtc : latest;
    }

    private static IEnumerable<string> EnumerateJsonLineFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            : [];
    }

    private static string ResolveImageEntryPath(StudioImageFrame frame)
    {
        return $"{SessionImagesDirectoryName}/{frame.FrameId}.jpg";
    }

    private static string CreateUniqueFrameId(string relativePath, ISet<string> frameIds, int frameIndex)
    {
        var baseId = SanitizeFrameId(Path.GetFileNameWithoutExtension(relativePath));
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = $"frame-{frameIndex}";
        }

        var frameId = baseId;
        var suffix = 2;
        while (!frameIds.Add(frameId))
        {
            frameId = $"{baseId}-{suffix++}";
        }

        return frameId;
    }

    private static string SanitizeFrameId(string value)
    {
        return new string(value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-')
            .ToArray()).Trim('-');
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
    }

    private static string? ResolveFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int ResolveLogPriority(int appEventType)
    {
        return (AppEventType)appEventType switch
        {
            AppEventType.Debug => 1,
            AppEventType.Warning => 3,
            AppEventType.Error or AppEventType.Exception => 4,
            _ => 2
        };
    }

    private static string ResolveEventTypeName(int appEventType)
    {
        return Enum.IsDefined(typeof(AppEventType), appEventType)
            ? ((AppEventType)appEventType).ToString()
            : "Event";
    }

    private static string EncodeUnit(string? coordinateUnit)
    {
        var normalized = coordinateUnit?.Trim();
        return normalized?.ToLowerInvariant() switch
        {
            "pixels" or "pixel" or "px" => "px",
            "points" or "point" or "pt" => "pt",
            "normalized" or "unit" or "ratio" or "n" => "n",
            _ => string.IsNullOrWhiteSpace(normalized) ? "px" : normalized!
        };
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, double? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static long GetDirectorySize(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : 0L;
    }

    private sealed record StudioArchiveContext(
        string SessionDirectory,
        OfflineCaptureSessionManifest Manifest,
        DeviceAppProfile? DeviceProfile,
        string? DeviceProfileJson,
        JsonObject CustomProperties,
        IReadOnlyList<StudioMetricChannel> Channels,
        IReadOnlyList<StudioImageArchiveFrame> Images,
        DateTimeOffset TimelineEndUtc,
        long CacheSizeBytes);

    private sealed record StudioDeviceProfileReadResult(DeviceAppProfile? Profile, string? Json);

    private sealed record StudioMetricSample(byte ChannelId, long Value, DateTimeOffset CapturedAtUtc);

    private sealed record StudioLogEntry(
        DateTimeOffset TimestampUtc,
        string Message,
        int Priority,
        string Source,
        string Tag,
        string? EventId);

    private readonly record struct StudioTouchRecord(
        DateTimeOffset CapturedAtUtc,
        int Action,
        long PointerId,
        int PointerIndex,
        int PointerCount,
        double X,
        double Y,
        double? SurfaceWidth,
        double? SurfaceHeight,
        string Unit,
        double? SurfaceScale);

    private readonly record struct StudioTouchBatchKey(
        string Space,
        string Unit,
        double? SurfaceWidth,
        double? SurfaceHeight,
        double? SurfaceScale);

    private sealed class StudioTouchBatchAccumulator
    {
        private const int MaximumRows = 512;
        private readonly List<StudioTouchRecord> rows = [];

        public StudioTouchBatchAccumulator(StudioTouchRecord firstTouch)
        {
            Key = new StudioTouchBatchKey(
                "w",
                firstTouch.Unit,
                firstTouch.SurfaceWidth,
                firstTouch.SurfaceHeight,
                firstTouch.SurfaceScale);
            T0 = firstTouch.CapturedAtUtc.ToUniversalTime();
        }

        public StudioTouchBatchKey Key { get; }

        public DateTimeOffset T0 { get; }

        public bool CanAdd(StudioTouchRecord touch)
        {
            var key = new StudioTouchBatchKey(
                "w",
                touch.Unit,
                touch.SurfaceWidth,
                touch.SurfaceHeight,
                touch.SurfaceScale);
            return rows.Count < MaximumRows && key.Equals(Key);
        }

        public void Add(StudioTouchRecord touch)
        {
            rows.Add(touch);
        }

        public void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("t0", T0);
            writer.WriteString("space", Key.Space);
            writer.WriteString("unit", Key.Unit);
            writer.WritePropertyName("surface");
            writer.WriteStartArray();
            WriteNullableNumber(writer, Key.SurfaceWidth);
            WriteNullableNumber(writer, Key.SurfaceHeight);
            WriteNullableNumber(writer, Key.SurfaceScale);
            writer.WriteEndArray();
            writer.WritePropertyName("rows");
            writer.WriteStartArray();

            foreach (var row in rows)
            {
                var deltaMilliseconds = (long)Math.Round(
                    (row.CapturedAtUtc.ToUniversalTime() - T0).TotalMilliseconds,
                    MidpointRounding.AwayFromZero);
                writer.WriteStartArray();
                writer.WriteNumberValue(Math.Max(0L, deltaMilliseconds));
                writer.WriteNumberValue(row.Action);
                writer.WriteNumberValue(row.PointerId);
                writer.WriteNumberValue(row.X);
                writer.WriteNumberValue(row.Y);
                if (row.PointerIndex != 0 || row.PointerCount != 1)
                {
                    writer.WriteNumberValue(row.PointerIndex);
                    writer.WriteNumberValue(row.PointerCount);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private sealed class StudioSessionCaptureDocument
    {
        public string Schema { get; init; } = SessionCaptureSchemaName;

        public required DateTimeOffset SavedAtUtc { get; init; }

        public object? Author { get; init; }

        public required StudioAppSessionSnapshot Session { get; init; }
    }

    private sealed class StudioAppSessionSnapshot
    {
        public required string SessionId { get; init; }

        public required string AppId { get; init; }

        public required string ClientName { get; init; }

        public required string RemoteAddress { get; init; }

        public string? Name { get; init; }

        public required DateTimeOffset CreatedUtc { get; init; }

        public required string? ConfigId { get; init; }

        public string? ProcessSessionId { get; init; }

        public required string Status { get; init; }

        public required DateTimeOffset LastUpdatedUtc { get; init; }

        public required bool IsHistorical { get; init; }

        public long CacheSizeBytes { get; init; }

        public bool IsPinned { get; init; }

        public object? Author { get; init; }

        public object? ReplaySource { get; init; }

        public string? SdkVersion { get; init; }

        public IReadOnlyList<string> Tags { get; init; } = [];

        public string? Notes { get; init; }

        public JsonObject? CustomProperties { get; init; }

        public AppLifecycleState AppState { get; init; } = AppLifecycleState.Unknown;

        public DateTimeOffset? AppStateChangedUtc { get; init; }

        public DeviceAppProfile? DeviceProfile { get; init; }

        public string? DeviceProfileJson { get; init; }

        public object? AppIcon { get; init; }

        public IReadOnlyList<object> Analyses { get; init; } = [];

        public IReadOnlyList<object> Annotations { get; init; } = [];

        public IReadOnlyList<StudioImageFrame> Images { get; init; } = [];

        public IReadOnlyList<object> Touches { get; init; } = [];

        public IReadOnlyList<object> VisualTreeSnapshots { get; init; } = [];

        public IReadOnlyList<object> ArtifactSnapshots { get; init; } = [];

        public required IReadOnlyList<object> Logs { get; init; }

        public required IReadOnlyList<object> MetricChannels { get; init; }

        public required IReadOnlyList<object> Metrics { get; init; }
    }

    private sealed class StudioImageFrame
    {
        public required string FrameId { get; init; }

        public required DateTimeOffset CapturedAtUtc { get; init; }

        public required string Format { get; init; }

        public required int Width { get; init; }

        public required int Height { get; init; }

        public required int Quality { get; init; }

        public required long ByteCount { get; init; }
    }

    private sealed record StudioImageArchiveFrame(string SourcePath, StudioImageFrame Frame);

    private sealed class StudioMetricChannel
    {
        public required byte ChannelId { get; init; }

        public required string Name { get; init; }

        public required string ColorHex { get; init; }

        public string? Unit { get; init; }

        public string Type { get; init; } = "custom";

        public string? Source { get; init; }

        public string? Group { get; init; }

        public string? Kind { get; init; }
    }
}
