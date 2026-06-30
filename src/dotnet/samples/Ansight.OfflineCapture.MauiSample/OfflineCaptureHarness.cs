using Microsoft.Maui.Storage;

namespace Ansight.OfflineCapture.MauiSample;

public sealed class OfflineCaptureHarness
{
    private const int DefaultMaximumSessionMegabytes = 64;
    private const int DefaultMaximumRetainedMegabytes = 256;

    private readonly OfflineCaptureController controller;
    private readonly Random random = new();
    private readonly Lock bufferLock = new();
    private readonly List<byte[]> retainedBuffers = new();
    private int burstNumber;

    private OfflineCaptureHarness()
    {
        CaptureRoot = Path.Combine(FileSystem.Current.AppDataDirectory, ".ansight");
        controller = OfflineCapture.Configure(new OfflineCaptureOptions
        {
            RootDirectory = CaptureRoot,
            MaximumSessionBytes = Megabytes(DefaultMaximumSessionMegabytes),
            MaximumRetainedBytes = Megabytes(DefaultMaximumRetainedMegabytes),
            SegmentDuration = TimeSpan.FromSeconds(5)
        });
    }

    public static OfflineCaptureHarness Shared { get; } = new();

    public string CaptureRoot { get; }

    public string? LastExportPath { get; private set; }

    public bool IsCapturing => controller.IsCapturing;

    public OfflineCaptureOptions Options => controller.Options;

    public int RetainedBufferCount
    {
        get
        {
            lock (bufferLock)
            {
                return retainedBuffers.Count;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
        => await controller.InitializeAsync(cancellationToken);

    public async Task<OfflineCaptureSessionInfo> StartAsync(CancellationToken cancellationToken = default)
        => await controller.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
        => await controller.StopAsync(cancellationToken);

    public async Task SetActivationModeAsync(
        OfflineCaptureActivationMode activationMode,
        CancellationToken cancellationToken = default)
        => await controller.SetActivationModeAsync(activationMode, cancellationToken);

    public async Task ApplyOptionsAsync(
        int retentionSeconds,
        int maximumSessionMegabytes,
        int segmentSeconds,
        bool useJpegOverride,
        bool jpegEnabled,
        int jpegIntervalMilliseconds,
        int jpegQuality,
        int jpegMaxWidth,
        CancellationToken cancellationToken = default)
    {
        await controller.UpdateOptionsAsync(options =>
        {
            options.RetentionWindowOverride = TimeSpan.FromSeconds(retentionSeconds);
            options.MaximumSessionBytes = Megabytes(maximumSessionMegabytes);
            options.MaximumRetainedBytes = Math.Max(options.MaximumSessionBytes, Megabytes(DefaultMaximumRetainedMegabytes));
            options.SegmentDuration = TimeSpan.FromSeconds(segmentSeconds);

            if (!useJpegOverride)
            {
                options.SessionJpegCaptureEnabledOverride = null;
                options.SessionJpegCaptureOverride = null;
            }
            else if (!jpegEnabled)
            {
                options.SessionJpegCaptureEnabledOverride = false;
                options.SessionJpegCaptureOverride = null;
            }
            else
            {
                options.SessionJpegCaptureEnabledOverride = true;
                options.SessionJpegCaptureOverride = new SessionJpegCaptureOptions
                {
                    IntervalMilliseconds = (ushort)Math.Clamp(jpegIntervalMilliseconds, 250, ushort.MaxValue),
                    Quality = Math.Clamp(jpegQuality, 1, 100),
                    MaxWidth = Math.Clamp(jpegMaxWidth, 160, 8192)
                };
            }
        }, cancellationToken);
    }

    public void RecordMetric()
    {
        Runtime.Metric(random.Next(20, 240), SampleAnsightConfiguration.SampleMetricChannelId);
    }

    public void RecordEvent(string label)
    {
        Runtime.Event(
            label,
            AppEventType.Info,
            SampleAnsightConfiguration.SampleEventChannelId,
            $"burst={burstNumber};capturing={IsCapturing}");
    }

    public void RecordScreenView(string screenName)
    {
        Runtime.ScreenViewed(screenName, SampleAnsightConfiguration.SampleEventChannelId, "offline-capture-sample");
    }

    public void RecordInteraction(string label)
    {
        Runtime.Event(
            label,
            AppEventType.Info,
            SampleAnsightConfiguration.SampleInteractionChannelId,
            $"buffers={RetainedBufferCount}");
    }

    public async Task GenerateBurstAsync(int count, CancellationToken cancellationToken = default)
    {
        burstNumber++;
        Runtime.RegisterCustomProperty("offlineCapture", "lastBurst", burstNumber);
        Runtime.RegisterCustomProperty("offlineCapture", "lastBurstUtc", DateTimeOffset.UtcNow.ToString("O"));

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Runtime.Metric(random.Next(30, 180), SampleAnsightConfiguration.SampleMetricChannelId);
            Runtime.Event(
                $"Burst event {burstNumber}.{i + 1}",
                i % 3 == 0 ? AppEventType.ScreenViewed : AppEventType.Info,
                SampleAnsightConfiguration.SampleEventChannelId,
                $"index={i}");

            if (i % 8 == 0)
            {
                Runtime.ScreenViewed($"Burst screen {burstNumber}.{i / 8 + 1}", SampleAnsightConfiguration.SampleEventChannelId);
            }

            await Task.Delay(40, cancellationToken);
        }
    }

    public void RegisterSessionProperty()
    {
        Runtime.RegisterCustomProperty("offlineCapture", "lastManualUpdateUtc", DateTimeOffset.UtcNow.ToString("O"));
        Runtime.RegisterCustomProperty("offlineCapture", "captureRoot", CaptureRoot);
        Runtime.RegisterCustomProperty("offlineCapture", "retainedBuffers", RetainedBufferCount);
    }

    public void ClearSessionProperties()
    {
        Runtime.ClearCustomProperties();
    }

    public void AddMemoryPressure(int megabytes)
    {
        var buffer = new byte[megabytes * 1024 * 1024];
        for (var i = 0; i < buffer.Length; i += 4096)
        {
            buffer[i] = (byte)random.Next(0, 255);
        }

        lock (bufferLock)
        {
            retainedBuffers.Add(buffer);
        }

        Runtime.Event(
            $"Retained {megabytes} MB buffer",
            AppEventType.Info,
            SampleAnsightConfiguration.SampleEventChannelId,
            $"count={RetainedBufferCount}");
    }

    public void ReleaseMemoryPressure()
    {
        byte[]? buffer = null;
        lock (bufferLock)
        {
            if (retainedBuffers.Count > 0)
            {
                buffer = retainedBuffers[^1];
                retainedBuffers.RemoveAt(retainedBuffers.Count - 1);
            }
        }

        if (buffer is null)
        {
            return;
        }

        Runtime.Event(
            "Released retained buffer",
            AppEventType.Info,
            SampleAnsightConfiguration.SampleEventChannelId,
            $"remaining={RetainedBufferCount}");
    }

    public async Task<string> ExportAsync(string? password, CancellationToken cancellationToken = default)
    {
        var exportDirectory = Path.Combine(FileSystem.Current.CacheDirectory, "ansight-offline-exports");
        Directory.CreateDirectory(exportDirectory);
        var exportPath = Path.Combine(
            exportDirectory,
            $"ansight-offline-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip");

        await controller.ExportToFileAsync(exportPath, new OfflineCaptureExportOptions
        {
            Password = string.IsNullOrWhiteSpace(password) ? null : password.Trim()
        }, cancellationToken);

        LastExportPath = exportPath;
        return exportPath;
    }

    public async Task<OfflineCaptureSampleSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var session = await controller.GetCurrentSessionAsync(cancellationToken);
        if (session is null || !Directory.Exists(session.DirectoryPath))
        {
            return OfflineCaptureSampleSummary.Empty(CaptureRoot, LastExportPath, IsCapturing, RetainedBufferCount);
        }

        return new OfflineCaptureSampleSummary(
            CaptureRoot,
            LastExportPath,
            session,
            IsCapturing,
            RetainedBufferCount,
            CountFiles(session.DirectoryPath, "telemetry/metrics", "*.jsonl"),
            CountFiles(session.DirectoryPath, "telemetry/events", "*.jsonl"),
            CountFiles(session.DirectoryPath, "input/touches", "*.jsonl"),
            CountFiles(session.DirectoryPath, "screenshots", "*.jpg"),
            CountFiles(session.DirectoryPath, "screenshots/index", "*.jsonl"),
            session.SizeBytes);
    }

    public async Task<IReadOnlyList<CaptureFileItem>> GetCurrentSessionFilesAsync(CancellationToken cancellationToken = default)
    {
        var session = await controller.GetCurrentSessionAsync(cancellationToken);
        if (session is null || !Directory.Exists(session.DirectoryPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(session.DirectoryPath, "*", SearchOption.AllDirectories)
            .Select(path => CaptureFileItem.FromFile(session.DirectoryPath, path))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CountFiles(string sessionDirectory, string relativeDirectory, string pattern)
    {
        var directory = Path.Combine(
            sessionDirectory,
            relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern).Length
            : 0;
    }

    private static long Megabytes(int megabytes) => megabytes * 1024L * 1024L;
}
