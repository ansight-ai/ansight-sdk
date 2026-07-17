namespace Ansight.OfflineCapture;

using System.Threading.Channels;
using Ansight.Annotations;
using TelemetryChannel = Ansight.Telemetry.Channels.Channel;

/// <summary>
/// Starts, stops, mutates, persists, and exports Ansight offline capture sessions.
/// </summary>
public sealed class OfflineCaptureController : IAsyncDisposable, IAnnotationSink
{
    private readonly Lock stateLock = new();
    private readonly IRuntime runtime;
    private readonly TouchCaptureHub? touchCaptureHub;
    private readonly SemaphoreSlim annotationWriteGate = new(1, 1);
    private OfflineCaptureOptions options;
    private Channel<OfflineCaptureWriteRecord>? recordChannel;
    private CancellationTokenSource? writerCts;
    private CancellationTokenSource? screenshotCts;
    private Task? writerTask;
    private Task? screenshotTask;
    private SegmentedJsonLineWriter? metricWriter;
    private SegmentedJsonLineWriter? eventWriter;
    private SegmentedJsonLineWriter? touchWriter;
    private SegmentedJsonLineWriter? screenshotIndexWriter;
    private EventHandler<MetricsUpdatedEventArgs>? metricsUpdatedHandler;
    private EventHandler<AppEventsUpdatedEventArgs>? eventsUpdatedHandler;
    private EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
    private IDisposable? annotationSinkRegistration;
    private long droppedRecordCount;
    private string? activeSessionDirectory;
    private string? activeSessionId;
    private OfflineCaptureSessionManifest? activeManifest;
    private DateTimeOffset lastRetentionRunUtc = DateTimeOffset.MinValue;
    private bool disposed;

    /// <summary>
    /// Creates a controller for the initialized singleton <see cref="Runtime"/>.
    /// </summary>
    public OfflineCaptureController(OfflineCaptureOptions? options = null)
        : this(Runtime.IsInitialized
            ? Runtime.MutableInstance
            : throw new InvalidOperationException("Runtime must be initialized before offline capture can be attached."),
            options)
    {
    }

    /// <summary>
    /// Creates a controller over a runtime instance.
    /// </summary>
    public OfflineCaptureController(IRuntime runtime, OfflineCaptureOptions? options = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.options = (options ?? new OfflineCaptureOptions()).Normalize();
        touchCaptureHub = runtime is RuntimeImpl runtimeImpl
            ? runtimeImpl.TouchCaptureHub
            : null;
    }

    /// <summary>
    /// Returns true while an offline capture session is writing to disk.
    /// </summary>
    public bool IsCapturing
    {
        get
        {
            lock (stateLock)
            {
                return activeSessionDirectory is not null;
            }
        }
    }

    /// <summary>
    /// Current normalized options. Mutate with <see cref="UpdateOptionsAsync"/>.
    /// </summary>
    public OfflineCaptureOptions Options
    {
        get
        {
            lock (stateLock)
            {
                return options.Clone();
            }
        }
    }

    /// <summary>
    /// Stable annotation sink identifier.
    /// </summary>
    public string Id => "offline.capture";

    /// <summary>
    /// Loads persisted activation settings and starts capture when configured for next-session or always-on capture.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var loadedOptions = await LoadPersistedOptionsAsync(cancellationToken);
        OfflineCaptureActivationMode activationMode;
        lock (stateLock)
        {
            options = MergeOptions(options, loadedOptions).Normalize();
            activationMode = options.ActivationMode;
        }

        if (activationMode is OfflineCaptureActivationMode.AlwaysOn or OfflineCaptureActivationMode.NextSessionOnly)
        {
            await StartAsync(cancellationToken);
        }

        if (activationMode == OfflineCaptureActivationMode.NextSessionOnly)
        {
            lock (stateLock)
            {
                options.ActivationMode = OfflineCaptureActivationMode.Disabled;
            }

            await PersistSettingsAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Starts capture immediately for the current app session.
    /// </summary>
    public async Task<OfflineCaptureSessionInfo> StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (stateLock)
        {
            if (activeSessionDirectory is not null)
            {
                return CreateActiveSessionInfo();
            }
        }

        var normalizedOptions = Options.Normalize();
        var effectiveOptions = ResolveEffectiveOptions(normalizedOptions);
        Directory.CreateDirectory(effectiveOptions.RootDirectory);
        Directory.CreateDirectory(OfflineCapturePaths.SessionsDirectory(effectiveOptions.RootDirectory));
        var sessionId = Guid.CreateVersion7().ToString("N");
        var sessionDirectory = OfflineCapturePaths.SessionDirectory(effectiveOptions.RootDirectory, sessionId);
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "metadata"));
        Directory.CreateDirectory(OfflineCapturePaths.MetricsDirectory(sessionDirectory));
        Directory.CreateDirectory(OfflineCapturePaths.EventsDirectory(sessionDirectory));
        Directory.CreateDirectory(OfflineCapturePaths.TouchesDirectory(sessionDirectory));
        Directory.CreateDirectory(OfflineCapturePaths.ScreenshotsDirectory(sessionDirectory));
        Directory.CreateDirectory(OfflineCapturePaths.ScreenshotIndexDirectory(sessionDirectory));
        Directory.CreateDirectory(OfflineCapturePaths.AnnotationBundlesDirectory(sessionDirectory));

        var manifest = new OfflineCaptureSessionManifest
        {
            SessionId = sessionId,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        ApplyEffectiveOptionsToManifest(manifest, effectiveOptions);
        var deviceProfile = CreateDeviceProfile();
        ApplyRuntimeSessionMetadataToManifest(manifest, deviceProfile);
        await WriteManifestAsync(sessionDirectory, manifest, cancellationToken);
        await WriteChannelsAsync(sessionDirectory, runtime.DataSink.Channels, cancellationToken);
        await WriteDeviceProfileAsync(sessionDirectory, deviceProfile, cancellationToken);
        await WriteCustomPropertiesAsync(sessionDirectory, cancellationToken);

        Interlocked.Exchange(ref droppedRecordCount, 0);
        var channel = System.Threading.Channels.Channel.CreateBounded<OfflineCaptureWriteRecord>(
            new BoundedChannelOptions(effectiveOptions.MaximumQueuedRecords)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ => Interlocked.Increment(ref droppedRecordCount));

        var writerCancellation = new CancellationTokenSource();
        var screenshotCancellation = new CancellationTokenSource();
        var metricSegmentWriter = new SegmentedJsonLineWriter(
            OfflineCapturePaths.MetricsDirectory(sessionDirectory),
            "m",
            effectiveOptions.SegmentDuration);
        var eventSegmentWriter = new SegmentedJsonLineWriter(
            OfflineCapturePaths.EventsDirectory(sessionDirectory),
            "e",
            effectiveOptions.SegmentDuration);
        var touchSegmentWriter = new SegmentedJsonLineWriter(
            OfflineCapturePaths.TouchesDirectory(sessionDirectory),
            "t",
            effectiveOptions.SegmentDuration);
        var screenshotIndexSegmentWriter = new SegmentedJsonLineWriter(
            OfflineCapturePaths.ScreenshotIndexDirectory(sessionDirectory),
            "s",
            effectiveOptions.SegmentDuration);

        lock (stateLock)
        {
            options = normalizedOptions;
            activeSessionId = sessionId;
            activeSessionDirectory = sessionDirectory;
            activeManifest = manifest;
            recordChannel = channel;
            writerCts = writerCancellation;
            screenshotCts = screenshotCancellation;
            metricWriter = metricSegmentWriter;
            eventWriter = eventSegmentWriter;
            touchWriter = touchSegmentWriter;
            screenshotIndexWriter = screenshotIndexSegmentWriter;
            writerTask = Task.Run(
                () => RunWriterAsync(
                    channel,
                    metricSegmentWriter,
                    eventSegmentWriter,
                    touchSegmentWriter,
                    screenshotIndexSegmentWriter,
                    writerCancellation.Token),
                CancellationToken.None);
            screenshotTask = Task.Run(() => RunScreenshotPumpAsync(screenshotCancellation.Token), CancellationToken.None);
        }

        AttachRuntimeFeeds();
        annotationSinkRegistration = Feedback.RegisterSinkForRuntime(runtime, this);
        await ApplyRetentionAsync(cancellationToken);
        return CreateSessionInfo(sessionDirectory, isActive: true);
    }

    /// <summary>
    /// Stops the active capture session after draining pending records to disk.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Persists and applies a new automatic activation mode.
    /// </summary>
    public async Task SetActivationModeAsync(
        OfflineCaptureActivationMode activationMode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (activationMode == OfflineCaptureActivationMode.Immediate)
        {
            await StartAsync(cancellationToken);
            activationMode = OfflineCaptureActivationMode.Disabled;
        }
        else if (activationMode == OfflineCaptureActivationMode.AlwaysOn)
        {
            await StartAsync(cancellationToken);
        }
        else if (activationMode == OfflineCaptureActivationMode.Disabled)
        {
            await StopInternalAsync(cancellationToken);
        }

        lock (stateLock)
        {
            options.ActivationMode = activationMode;
        }

        await PersistSettingsAsync(cancellationToken);
    }

    /// <summary>
    /// Mutates capture options at runtime. The root directory and queue size cannot change while actively capturing.
    /// </summary>
    public async Task UpdateOptionsAsync(
        Action<OfflineCaptureOptions> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ThrowIfDisposed();

        OfflineCaptureOptions updatedOptions;
        OfflineCaptureEffectiveOptions effectiveOptions;
        string? sessionDirectory;
        OfflineCaptureSessionManifest? manifest;
        lock (stateLock)
        {
            updatedOptions = options.Clone();
            mutate(updatedOptions);
            updatedOptions = updatedOptions.Normalize();

            if (activeSessionDirectory is not null)
            {
                if (!string.Equals(options.RootDirectory, updatedOptions.RootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Offline capture root directory cannot change while capture is active.");
                }

                if (options.MaximumQueuedRecords != updatedOptions.MaximumQueuedRecords)
                {
                    throw new InvalidOperationException("Offline capture queue size cannot change while capture is active.");
                }
            }

            options = updatedOptions;
            effectiveOptions = ResolveEffectiveOptions(updatedOptions);
            metricWriter?.UpdateSegmentDuration(effectiveOptions.SegmentDuration);
            eventWriter?.UpdateSegmentDuration(effectiveOptions.SegmentDuration);
            touchWriter?.UpdateSegmentDuration(effectiveOptions.SegmentDuration);
            screenshotIndexWriter?.UpdateSegmentDuration(effectiveOptions.SegmentDuration);
            sessionDirectory = activeSessionDirectory;
            manifest = activeManifest;
            if (manifest is not null)
            {
                ApplyEffectiveOptionsToManifest(manifest, effectiveOptions);
            }
        }

        if (sessionDirectory is not null && manifest is not null)
        {
            await WriteManifestAsync(sessionDirectory, manifest, cancellationToken);
            await ApplyRetentionAsync(cancellationToken);
        }

        await PersistSettingsAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the active session, or the latest persisted session when capture is not active.
    /// </summary>
    public Task<OfflineCaptureSessionInfo?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (stateLock)
        {
            if (activeSessionDirectory is not null)
            {
                return Task.FromResult<OfflineCaptureSessionInfo?>(CreateActiveSessionInfo());
            }
        }

        var latest = GetLatestSessionDirectory(options.RootDirectory!);
        return Task.FromResult(latest is null ? null : CreateSessionInfo(latest, isActive: false));
    }

    /// <summary>
    /// Exports a capture session to a ZIP archive file and returns the path.
    /// </summary>
    public async Task<string> ExportToFileAsync(
        string destinationArchivePath,
        OfflineCaptureExportOptions? exportOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchivePath);
        ThrowIfDisposed();

        exportOptions ??= new OfflineCaptureExportOptions();
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationArchivePath));
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var stream = new FileStream(
            destinationArchivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ExportToStreamAsync(stream, exportOptions, cancellationToken);
        return Path.GetFullPath(destinationArchivePath);
    }

    /// <summary>
    /// Exports a capture session to a caller-provided stream.
    /// </summary>
    public async Task ExportToStreamAsync(
        Stream destination,
        OfflineCaptureExportOptions? exportOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();
        exportOptions ??= new OfflineCaptureExportOptions();

        await DrainAndFlushWritersAsync(cancellationToken);
        var sessionDirectory = ResolveExportSessionDirectory(exportOptions.SessionId);
        if (sessionDirectory is null)
        {
            throw new InvalidOperationException("No offline capture session is available to export.");
        }

        await RefreshActiveSessionMetadataAsync(sessionDirectory, cancellationToken);
        await OfflineCaptureZipExporter.ExportAsync(sessionDirectory, destination, exportOptions, cancellationToken);
    }

    /// <summary>
    /// Stops capture, detaches runtime feed subscriptions, and releases writer resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await StopInternalAsync(CancellationToken.None);
        annotationWriteGate.Dispose();
    }

    /// <summary>
    /// Writes a sealed annotation bundle directly into the active offline session.
    /// </summary>
    public async ValueTask<AnnotationSinkResult> SubmitAsync(
        AnnotationBundle bundle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        await annotationWriteGate.WaitAsync(cancellationToken);
        try
        {
            string? sessionDirectory;
            OfflineCaptureSessionManifest? manifest;
            lock (stateLock)
            {
                sessionDirectory = activeSessionDirectory;
                manifest = activeManifest;
            }

            if (sessionDirectory is null || manifest is null)
            {
                return AnnotationSinkResult.Failure(Id, "Offline Capture is not currently active.");
            }

            var bundlesDirectory = OfflineCapturePaths.AnnotationBundlesDirectory(sessionDirectory);
            Directory.CreateDirectory(bundlesDirectory);
            var destinationPath = Path.Combine(bundlesDirectory, bundle.FileName);
            var temporaryPath = Path.Combine(bundlesDirectory, $".{bundle.AnnotationId:N}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(bundle.Bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var relativePath = Path.GetRelativePath(sessionDirectory, destinationPath).Replace('\\', '/');
            var indexLine = JsonSerializer.Serialize(new
            {
                id = bundle.AnnotationId.ToString("N"),
                t = bundle.CapturedAtUtc.ToUnixTimeMilliseconds(),
                p = relativePath,
                b = bundle.Bytes.Length
            }, OfflineCaptureJson.Data);
            var indexPath = OfflineCapturePaths.AnnotationIndexPath(sessionDirectory);
            await using (var indexStream = new FileStream(
                indexPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(indexStream))
            {
                await writer.WriteLineAsync(indexLine);
                await writer.FlushAsync(cancellationToken);
            }

            lock (stateLock)
            {
                manifest.AnnotationCount++;
            }
            await WriteManifestAsync(sessionDirectory, manifest, cancellationToken);
            return AnnotationSinkResult.Success(Id, "Annotation stored in the active offline capture session.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AnnotationSinkResult.Failure(Id, exception.Message);
        }
        finally
        {
            annotationWriteGate.Release();
        }
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        Channel<OfflineCaptureWriteRecord>? channel;
        CancellationTokenSource? currentWriterCts;
        CancellationTokenSource? currentScreenshotCts;
        Task? currentWriterTask;
        Task? currentScreenshotTask;
        SegmentedJsonLineWriter? currentMetricWriter;
        SegmentedJsonLineWriter? currentEventWriter;
        SegmentedJsonLineWriter? currentTouchWriter;
        SegmentedJsonLineWriter? currentScreenshotIndexWriter;
        string? sessionDirectory;
        OfflineCaptureSessionManifest? manifest;
        IDisposable? currentAnnotationSinkRegistration;

        lock (stateLock)
        {
            if (activeSessionDirectory is null)
            {
                return;
            }

            DetachRuntimeFeeds();
            channel = recordChannel;
            currentWriterCts = writerCts;
            currentScreenshotCts = screenshotCts;
            currentWriterTask = writerTask;
            currentScreenshotTask = screenshotTask;
            currentMetricWriter = metricWriter;
            currentEventWriter = eventWriter;
            currentTouchWriter = touchWriter;
            currentScreenshotIndexWriter = screenshotIndexWriter;
            sessionDirectory = activeSessionDirectory;
            manifest = activeManifest;
            currentAnnotationSinkRegistration = annotationSinkRegistration;

            recordChannel = null;
            writerCts = null;
            screenshotCts = null;
            writerTask = null;
            screenshotTask = null;
            metricWriter = null;
            eventWriter = null;
            touchWriter = null;
            screenshotIndexWriter = null;
            activeSessionDirectory = null;
            activeSessionId = null;
            activeManifest = null;
            annotationSinkRegistration = null;
        }

        currentAnnotationSinkRegistration?.Dispose();
        currentScreenshotCts?.Cancel();
        channel?.Writer.TryComplete();

        if (currentScreenshotTask is not null)
        {
            await WaitForTaskAsync(currentScreenshotTask, cancellationToken);
        }

        if (currentWriterTask is not null)
        {
            await WaitForTaskAsync(currentWriterTask, cancellationToken);
        }

        currentWriterCts?.Dispose();
        currentScreenshotCts?.Dispose();

        if (currentMetricWriter is not null)
        {
            await currentMetricWriter.DisposeAsync();
        }

        if (currentEventWriter is not null)
        {
            await currentEventWriter.DisposeAsync();
        }

        if (currentTouchWriter is not null)
        {
            await currentTouchWriter.DisposeAsync();
        }

        if (currentScreenshotIndexWriter is not null)
        {
            await currentScreenshotIndexWriter.DisposeAsync();
        }

        if (sessionDirectory is not null && manifest is not null)
        {
            await annotationWriteGate.WaitAsync(cancellationToken);
            try
            {
                manifest.StoppedAtUtc = DateTimeOffset.UtcNow;
                manifest.DroppedRecordCount = Interlocked.Read(ref droppedRecordCount);
                ApplyRuntimeSessionMetadataToManifest(manifest);
                await WriteManifestAsync(sessionDirectory, manifest, cancellationToken);
                await WriteCustomPropertiesAsync(sessionDirectory, cancellationToken);
                if (manifest.DroppedRecordCount > 0)
                {
                    Logger.Warning($"Offline capture dropped {manifest.DroppedRecordCount:N0} queued record(s) because the writer could not keep up.");
                }
            }
            finally
            {
                annotationWriteGate.Release();
            }
        }
    }

    private void AttachRuntimeFeeds()
    {
        metricsUpdatedHandler = (_, args) =>
        {
            foreach (var metric in args.Added)
            {
                TryQueue(new OfflineCaptureWriteRecord(
                    OfflineCaptureWriteKind.Metric,
                    new DateTimeOffset(DateTime.SpecifyKind(metric.CapturedAtUtc, DateTimeKind.Utc)),
                    OfflineCaptureJson.Metric(metric)));
            }
        };

        eventsUpdatedHandler = (_, args) =>
        {
            foreach (var appEvent in args.Added)
            {
                TryQueue(new OfflineCaptureWriteRecord(
                    OfflineCaptureWriteKind.Event,
                    new DateTimeOffset(DateTime.SpecifyKind(appEvent.CapturedAtUtc, DateTimeKind.Utc)),
                    OfflineCaptureJson.Event(appEvent)));
            }
        };

        runtime.DataSink.OnMetricsUpdated += metricsUpdatedHandler;
        runtime.DataSink.OnEventsUpdated += eventsUpdatedHandler;

        if (touchCaptureHub is not null)
        {
            touchCapturedHandler = (_, args) =>
            {
                TryQueue(new OfflineCaptureWriteRecord(
                    OfflineCaptureWriteKind.Touch,
                    args.Touch.CapturedAtUtc,
                    OfflineCaptureJson.Touch(args.Touch)));
            };
            touchCaptureHub.TouchCaptured += touchCapturedHandler;
        }
    }

    private void DetachRuntimeFeeds()
    {
        if (metricsUpdatedHandler is not null)
        {
            runtime.DataSink.OnMetricsUpdated -= metricsUpdatedHandler;
            metricsUpdatedHandler = null;
        }

        if (eventsUpdatedHandler is not null)
        {
            runtime.DataSink.OnEventsUpdated -= eventsUpdatedHandler;
            eventsUpdatedHandler = null;
        }

        if (touchCaptureHub is not null && touchCapturedHandler is not null)
        {
            touchCaptureHub.TouchCaptured -= touchCapturedHandler;
            touchCapturedHandler = null;
        }
    }

    private bool TryQueue(OfflineCaptureWriteRecord record)
    {
        lock (stateLock)
        {
            return recordChannel?.Writer.TryWrite(record) == true;
        }
    }

    private async Task RunWriterAsync(
        Channel<OfflineCaptureWriteRecord> channel,
        SegmentedJsonLineWriter metricSegmentWriter,
        SegmentedJsonLineWriter eventSegmentWriter,
        SegmentedJsonLineWriter touchSegmentWriter,
        SegmentedJsonLineWriter screenshotIndexSegmentWriter,
        CancellationToken cancellationToken)
    {
        while (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            var wroteAny = false;
            while (channel.Reader.TryRead(out var record))
            {
                if (record.Kind == OfflineCaptureWriteKind.Flush)
                {
                    await FlushWritersAsync(
                        metricSegmentWriter,
                        eventSegmentWriter,
                        touchSegmentWriter,
                        screenshotIndexSegmentWriter,
                        cancellationToken);
                    record.FlushCompletion?.TrySetResult();
                    continue;
                }

                await WriteRecordAsync(
                    record,
                    metricSegmentWriter,
                    eventSegmentWriter,
                    touchSegmentWriter,
                    screenshotIndexSegmentWriter,
                    cancellationToken);
                wroteAny = true;
            }

            if (wroteAny)
            {
                await FlushWritersAsync(
                    metricSegmentWriter,
                    eventSegmentWriter,
                    touchSegmentWriter,
                    screenshotIndexSegmentWriter,
                    cancellationToken);
            }

            if (DateTimeOffset.UtcNow - lastRetentionRunUtc > TimeSpan.FromSeconds(5))
            {
                await ApplyRetentionAsync(cancellationToken);
            }
        }
    }

    private static async Task WriteRecordAsync(
        OfflineCaptureWriteRecord record,
        SegmentedJsonLineWriter metricSegmentWriter,
        SegmentedJsonLineWriter eventSegmentWriter,
        SegmentedJsonLineWriter touchSegmentWriter,
        SegmentedJsonLineWriter screenshotIndexSegmentWriter,
        CancellationToken cancellationToken)
    {
        var writer = record.Kind switch
        {
            OfflineCaptureWriteKind.Metric => metricSegmentWriter,
            OfflineCaptureWriteKind.Event => eventSegmentWriter,
            OfflineCaptureWriteKind.Touch => touchSegmentWriter,
            OfflineCaptureWriteKind.Screenshot => screenshotIndexSegmentWriter,
            _ => null
        };

        if (writer is null)
        {
            return;
        }

        await writer.WriteLineAsync(record.CapturedAtUtc, record.JsonLine, cancellationToken);
    }

    private static async Task FlushWritersAsync(
        SegmentedJsonLineWriter metricSegmentWriter,
        SegmentedJsonLineWriter eventSegmentWriter,
        SegmentedJsonLineWriter touchSegmentWriter,
        SegmentedJsonLineWriter screenshotIndexSegmentWriter,
        CancellationToken cancellationToken)
    {
        await metricSegmentWriter.FlushAsync(cancellationToken);
        await eventSegmentWriter.FlushAsync(cancellationToken);
        await touchSegmentWriter.FlushAsync(cancellationToken);
        await screenshotIndexSegmentWriter.FlushAsync(cancellationToken);
    }

    private async Task RunScreenshotPumpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            OfflineCaptureEffectiveOptions currentOptions;
            string? sessionDirectory;
            lock (stateLock)
            {
                currentOptions = ResolveEffectiveOptions(options);
                sessionDirectory = activeSessionDirectory;
            }

            var captureOptions = currentOptions.SessionJpegCapture;
            if (sessionDirectory is not null && captureOptions is not null)
            {
                try
                {
                    using var frame = await SessionJpegCaptureSupport.CaptureJpegFrameAsync(captureOptions, cancellationToken);
                    if (frame is not null && !frame.JpegPayload.IsEmpty)
                    {
                        await WriteScreenshotAsync(sessionDirectory, frame, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Offline screenshot capture skipped: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(captureOptions?.IntervalMilliseconds ?? 1000),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task WriteScreenshotAsync(
        string sessionDirectory,
        SessionJpegFrame frame,
        CancellationToken cancellationToken)
    {
        var screenshotsDirectory = OfflineCapturePaths.ScreenshotsDirectory(sessionDirectory);
        Directory.CreateDirectory(screenshotsDirectory);
        var fileName = $"{frame.CapturedAtUtc:yyyyMMddHHmmssfff}.jpg";
        var filePath = Path.Combine(screenshotsDirectory, fileName);
        await using (var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.WriteAsync(frame.JpegPayload, cancellationToken);
        }

        var relativePath = Path.GetRelativePath(sessionDirectory, filePath);
        TryQueue(new OfflineCaptureWriteRecord(
            OfflineCaptureWriteKind.Screenshot,
            frame.CapturedAtUtc,
            OfflineCaptureJson.Screenshot(frame, relativePath)));
    }

    private async Task FlushWritersAsync(CancellationToken cancellationToken)
    {
        if (metricWriter is not null)
        {
            await metricWriter.FlushAsync(cancellationToken);
        }

        if (eventWriter is not null)
        {
            await eventWriter.FlushAsync(cancellationToken);
        }

        if (touchWriter is not null)
        {
            await touchWriter.FlushAsync(cancellationToken);
        }

        if (screenshotIndexWriter is not null)
        {
            await screenshotIndexWriter.FlushAsync(cancellationToken);
        }
    }

    private async Task DrainAndFlushWritersAsync(CancellationToken cancellationToken)
    {
        Channel<OfflineCaptureWriteRecord>? channel;
        lock (stateLock)
        {
            channel = recordChannel;
        }

        if (channel is null)
        {
            await FlushWritersAsync(cancellationToken);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!channel.Writer.TryWrite(new OfflineCaptureWriteRecord(
            OfflineCaptureWriteKind.Flush,
            DateTimeOffset.UtcNow,
            string.Empty,
            completion)))
        {
            await FlushWritersAsync(cancellationToken);
            return;
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task RefreshActiveSessionMetadataAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        OfflineCaptureSessionManifest? manifest;
        lock (stateLock)
        {
            manifest = activeSessionDirectory is not null
                       && activeManifest is not null
                       && string.Equals(
                           Path.GetFullPath(activeSessionDirectory),
                           Path.GetFullPath(sessionDirectory),
                           StringComparison.OrdinalIgnoreCase)
                ? activeManifest
                : null;
        }

        if (manifest is null)
        {
            return;
        }

        ApplyRuntimeSessionMetadataToManifest(manifest);
        await WriteManifestAsync(sessionDirectory, manifest, cancellationToken);
        await WriteCustomPropertiesAsync(sessionDirectory, cancellationToken);
    }

    private async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        OfflineCaptureEffectiveOptions currentOptions;
        string? sessionDirectory;
        IReadOnlyCollection<string?> activeFiles;
        lock (stateLock)
        {
            currentOptions = ResolveEffectiveOptions(options);
            sessionDirectory = activeSessionDirectory;
            activeFiles =
            [
                metricWriter?.CurrentPath,
                eventWriter?.CurrentPath,
                touchWriter?.CurrentPath,
                screenshotIndexWriter?.CurrentPath
            ];
            lastRetentionRunUtc = DateTimeOffset.UtcNow;
        }

        await OfflineCaptureRetentionManager.ApplyAsync(
            currentOptions.RootDirectory,
            sessionDirectory,
            currentOptions,
            activeFiles,
            cancellationToken);
    }

    private async Task PersistSettingsAsync(CancellationToken cancellationToken)
    {
        OfflineCaptureOptions currentOptions;
        lock (stateLock)
        {
            currentOptions = options.Clone();
        }

        Directory.CreateDirectory(currentOptions.RootDirectory!);
        var document = new OfflineCaptureSettingsDocument
        {
            ActivationMode = currentOptions.ActivationMode,
            Options = currentOptions
        };
        await WriteJsonFileAsync(
            OfflineCapturePaths.SettingsPath(currentOptions.RootDirectory!),
            document,
            OfflineCaptureJson.Metadata,
            cancellationToken);
    }

    private async Task<OfflineCaptureOptions> LoadPersistedOptionsAsync(CancellationToken cancellationToken)
    {
        var settingsPath = OfflineCapturePaths.SettingsPath(options.RootDirectory!);
        if (!File.Exists(settingsPath))
        {
            return options;
        }

        await using var stream = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<OfflineCaptureSettingsDocument>(
            stream,
            OfflineCaptureJson.Metadata,
            cancellationToken);
        if (document?.Options is null)
        {
            return options;
        }

        document.Options.ActivationMode = document.ActivationMode;
        return document.Options.Normalize();
    }

    private static OfflineCaptureOptions MergeOptions(
        OfflineCaptureOptions constructorOptions,
        OfflineCaptureOptions persistedOptions)
    {
        var merged = persistedOptions.Clone();
        if (!string.IsNullOrWhiteSpace(constructorOptions.RootDirectory))
        {
            merged.RootDirectory = constructorOptions.RootDirectory;
        }

        return merged;
    }

    private static async Task WriteManifestAsync(
        string sessionDirectory,
        OfflineCaptureSessionManifest manifest,
        CancellationToken cancellationToken)
    {
        await WriteJsonFileAsync(
            OfflineCapturePaths.ManifestPath(sessionDirectory),
            manifest,
            OfflineCaptureJson.Metadata,
            cancellationToken);
    }

    private static async Task WriteDeviceProfileAsync(
        string sessionDirectory,
        DeviceAppProfile? deviceProfile,
        CancellationToken cancellationToken)
    {
        if (deviceProfile is null)
        {
            return;
        }

        await WriteJsonFileAsync(
            OfflineCapturePaths.DeviceProfilePath(sessionDirectory),
            deviceProfile,
            Ansight.Pairing.PairingJson.Compact,
            cancellationToken);
    }

    private async Task WriteCustomPropertiesAsync(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        var customProperties = runtime is RuntimeImpl runtimeImpl
            ? runtimeImpl.CreateCustomPropertiesSnapshot().ToJsonObject()
            : new JsonObject();

        await WriteJsonFileAsync(
            OfflineCapturePaths.CustomPropertiesPath(sessionDirectory),
            customProperties,
            OfflineCaptureJson.Data,
            cancellationToken);
    }

    private static DeviceAppProfile? CreateDeviceProfile()
    {
        try
        {
            return DeviceAppProfileCollector.Create();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Offline capture device profile skipped: {ex.Message}");
            return null;
        }
    }

    private void ApplyRuntimeSessionMetadataToManifest(
        OfflineCaptureSessionManifest manifest,
        DeviceAppProfile? deviceProfile = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var appProfile = deviceProfile?.App;
        manifest.AppId = ResolveFirstNonEmpty(
            manifest.AppId,
            appProfile?.AppId,
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
            "offline-capture")!;
        manifest.ClientName = ResolveFirstNonEmpty(
            manifest.ClientName,
            appProfile?.AppName,
            appProfile?.AppId,
            manifest.AppId,
            "Offline Capture")!;
        manifest.RemoteAddress = ResolveFirstNonEmpty(manifest.RemoteAddress, "offline")!;
        manifest.ProcessSessionId = ResolveFirstNonEmpty(
            manifest.ProcessSessionId,
            global::System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        manifest.SdkVersion = ResolveFirstNonEmpty(
            manifest.SdkVersion,
            deviceProfile?.Sdk?.Version,
            ResolveSdkVersion());

        if (runtime.DataSink is IAppLifecycleStateSource lifecycleSource)
        {
            manifest.AppState = lifecycleSource.CurrentAppLifecycleState;
            manifest.AppStateChangedUtc = lifecycleSource.CurrentAppLifecycleStateChangedUtc;
        }
    }

    private static string? ResolveSdkVersion()
    {
        var assembly = typeof(Runtime).Assembly;
        return ResolveFirstNonEmpty(
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion,
            assembly.GetName().Version?.ToString());
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

    private OfflineCaptureEffectiveOptions ResolveEffectiveOptions(OfflineCaptureOptions rawOptions)
    {
        var normalizedOptions = rawOptions.Normalize();
        var runtimeOptions = runtime is RuntimeImpl runtimeImpl
            ? runtimeImpl.Options
            : Ansight.Options.Default;
        var sessionJpegCapture = ResolveSessionJpegCaptureOptions(normalizedOptions, runtimeOptions.SessionJpegCapture);

        return new OfflineCaptureEffectiveOptions
        {
            RootDirectory = normalizedOptions.RootDirectory!,
            RetentionWindow = normalizedOptions.RetentionWindowOverride
                ?? TimeSpan.FromSeconds(runtimeOptions.RetentionPeriodSeconds),
            MaximumSessionBytes = normalizedOptions.MaximumSessionBytes,
            MaximumRetainedBytes = normalizedOptions.MaximumRetainedBytes,
            SegmentDuration = normalizedOptions.SegmentDuration,
            MaximumQueuedRecords = normalizedOptions.MaximumQueuedRecords,
            SessionJpegCapture = sessionJpegCapture
        };
    }

    private static SessionJpegCaptureOptions? ResolveSessionJpegCaptureOptions(
        OfflineCaptureOptions offlineOptions,
        SessionJpegCaptureOptions? runtimeOptions)
    {
        if (offlineOptions.SessionJpegCaptureEnabledOverride == false)
        {
            return null;
        }

        var sessionJpegCapture = offlineOptions.SessionJpegCaptureOverride is not null
            ? CloneSessionJpegCaptureOptions(offlineOptions.SessionJpegCaptureOverride)
            : CloneSessionJpegCaptureOptions(runtimeOptions);

        if (sessionJpegCapture is null && offlineOptions.SessionJpegCaptureEnabledOverride == true)
        {
            sessionJpegCapture = new SessionJpegCaptureOptions();
        }

        return sessionJpegCapture is null
            ? null
            : NormalizeSessionJpegCaptureOptions(sessionJpegCapture);
    }

    private static SessionJpegCaptureOptions? CloneSessionJpegCaptureOptions(SessionJpegCaptureOptions? source)
    {
        return source is null
            ? null
            : new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = source.IntervalMilliseconds,
                Quality = source.Quality,
                MaxWidth = source.MaxWidth
            };
    }

    private static SessionJpegCaptureOptions NormalizeSessionJpegCaptureOptions(SessionJpegCaptureOptions options)
    {
        if (options.IntervalMilliseconds < 250)
        {
            options.IntervalMilliseconds = 250;
        }

        options.Quality = Math.Clamp(options.Quality, 1, 100);
        if (options.MaxWidth is <= 0)
        {
            options.MaxWidth = null;
        }
        else if (options.MaxWidth > 8192)
        {
            options.MaxWidth = 8192;
        }

        return options;
    }

    private static void ApplyEffectiveOptionsToManifest(
        OfflineCaptureSessionManifest manifest,
        OfflineCaptureEffectiveOptions effectiveOptions)
    {
        var sessionJpegCapture = effectiveOptions.SessionJpegCapture;
        manifest.RetentionWindow = effectiveOptions.RetentionWindow;
        manifest.MaximumSessionBytes = effectiveOptions.MaximumSessionBytes;
        manifest.SessionJpegCaptureEnabled = sessionJpegCapture is not null;
        manifest.SessionJpegCaptureIntervalMilliseconds = sessionJpegCapture?.IntervalMilliseconds;
        manifest.SessionJpegCaptureQuality = sessionJpegCapture?.Quality;
        manifest.SessionJpegCaptureMaxWidth = sessionJpegCapture?.MaxWidth;
    }

    private static async Task WriteChannelsAsync(
        string sessionDirectory,
        IReadOnlyList<TelemetryChannel> channels,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            v = 1,
            ch = channels.Select(OfflineCaptureJson.Channel).ToArray()
        };
        await WriteJsonFileAsync(
            OfflineCapturePaths.ChannelsPath(sessionDirectory),
            payload,
            OfflineCaptureJson.Data,
            cancellationToken);
    }

    private static async Task WriteJsonFileAsync<T>(
        string path,
        T value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, value, serializerOptions, cancellationToken);
    }

    private OfflineCaptureSessionInfo CreateActiveSessionInfo()
    {
        return activeSessionDirectory is null
            ? throw new InvalidOperationException("No active offline capture session.")
            : CreateSessionInfo(activeSessionDirectory, isActive: true);
    }

    private static OfflineCaptureSessionInfo CreateSessionInfo(string sessionDirectory, bool isActive)
    {
        var manifest = ReadManifest(sessionDirectory);
        return new OfflineCaptureSessionInfo(
            manifest?.SessionId ?? Path.GetFileName(sessionDirectory),
            sessionDirectory,
            manifest?.StartedAtUtc ?? Directory.GetCreationTimeUtc(sessionDirectory),
            manifest?.StoppedAtUtc,
            GetDirectorySize(sessionDirectory),
            isActive);
    }

    private string? ResolveExportSessionDirectory(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var requested = OfflineCapturePaths.SessionDirectory(options.RootDirectory!, sessionId);
            return Directory.Exists(requested) ? requested : null;
        }

        lock (stateLock)
        {
            if (activeSessionDirectory is not null)
            {
                return activeSessionDirectory;
            }
        }

        return GetLatestSessionDirectory(options.RootDirectory!);
    }

    private static string? GetLatestSessionDirectory(string rootDirectory)
    {
        var sessionsDirectory = OfflineCapturePaths.SessionsDirectory(rootDirectory);
        if (!Directory.Exists(sessionsDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(sessionsDirectory)
            .Select(directory => new
            {
                Directory = directory,
                Manifest = ReadManifest(directory)
            })
            .OrderByDescending(item => item.Manifest?.StartedAtUtc ?? Directory.GetCreationTimeUtc(item.Directory))
            .FirstOrDefault()
            ?.Directory;
    }

    private static OfflineCaptureSessionManifest? ReadManifest(string sessionDirectory)
    {
        var manifestPath = OfflineCapturePaths.ManifestPath(sessionDirectory);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<OfflineCaptureSessionManifest>(json, OfflineCaptureJson.Metadata);
        }
        catch
        {
            return null;
        }
    }

    private static long GetDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Sum(path =>
            {
                try
                {
                    return new FileInfo(path).Length;
                }
                catch
                {
                    return 0L;
                }
            });
    }

    private static async Task WaitForTaskAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(OfflineCaptureController));
        }
    }
}
