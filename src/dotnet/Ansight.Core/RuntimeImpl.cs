using System.Diagnostics.CodeAnalysis;
using Ansight.Pairing;
using Ansight.Native;
using Ansight.Telemetry.Battery;
using Ansight.Telemetry.Memory;
using Ansight.Tools;
using System.Text.Json.Nodes;

namespace Ansight;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class RuntimeImpl : IRuntime
{
    private readonly Options options;
    private MemorySamplerThread? samplerThread;
    private readonly Lock samplerLock = new Lock();
    private readonly Lock appLifecycleLock = new();
    private readonly Lock nativeTelemetrySyncLock = new();
    private long appLifecycleVersion;
    private long lastNativeMetricSequence;
    private long lastNativeEventSequence;

    private readonly MutableDataSink mutableDataSink;
    private readonly IDataSink dataSink;
    private readonly IFrameRateMonitor frameRateMonitor;
    private readonly IBatteryLevelMonitor batteryLevelMonitor;
    private readonly ITouchCaptureSession touchCaptureSession;
    private readonly HostSessionManager? managedHostConnection;
    private readonly IHostConnection hostConnection;
    private readonly SessionCustomProperties customProperties;
    private readonly INativeRuntimeBridge nativeRuntime;
    private readonly bool usesNativeRuntime;
    private bool fpsTrackingEnabled;
    private readonly bool batteryLevelTrackingEnabled;

    public IDataSink DataSink => dataSink;
    internal Options Options => options;
    internal PairingBinaryTransferHub BinaryTransferHub { get; } = new();
    internal TouchCaptureHub TouchCaptureHub { get; }
    internal AppLifecycleState CurrentAppLifecycleState => mutableDataSink.CurrentAppLifecycleState;
    internal DateTimeOffset? CurrentAppLifecycleStateChangedUtc => mutableDataSink.CurrentAppLifecycleStateChangedUtc;
    internal string ProcessSessionId => nativeRuntime.ProcessSessionId;

    public ToolProtocolBridge ToolBridge { get; }

    public IHostConnection HostConnection => hostConnection;

    public RuntimeImpl(Options options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        mutableDataSink = new MutableDataSink(options);
        dataSink = new RuntimeDataSink(mutableDataSink, this);
        frameRateMonitor = FrameRateMonitorFactory.Create();
        batteryLevelMonitor = BatteryLevelMonitorFactory.Create();
        TouchCaptureHub = new TouchCaptureHub(options.TouchCapture);
        touchCaptureSession = TouchCaptureSupport.CreateSession(TouchCaptureHub);
        customProperties = options.CustomProperties?.Clone() ?? new SessionCustomProperties();
        nativeRuntime = NativeRuntimeBridgeFactory.Create(options);
        usesNativeRuntime = nativeRuntime.IsAvailable;
        fpsTrackingEnabled = options.EnableFramesPerSecond;
        batteryLevelTrackingEnabled = options.EnableBatteryLevel && batteryLevelMonitor.IsSupported;
        ToolBridge = options.Tools.CreateBridge(options.ToolGuard);
        if (usesNativeRuntime)
        {
            nativeRuntime.ConfigureToolProtocol(ToolBridge);
            hostConnection = new NativeHostConnection(nativeRuntime, options.HostConnection);
            BinaryTransferHub.AttachTransport(new NativePairingBinaryTransport(nativeRuntime));
        }
        else
        {
            managedHostConnection = new HostSessionManager(
                this,
                options.HostAutoProbe,
                cachedProfileRetention: options.HostConnection.ConnectionProfileRetention);
            hostConnection = new HostPairingManager(managedHostConnection, options.HostConnection);
        }
        InitializeRuntimeFeatures();
    }

    public bool IsActive { get; private set; }

    public bool IsFramesPerSecondEnabled => fpsTrackingEnabled;

    public bool IsTouchCaptureEnabled => TouchCaptureHub.IsRuntimeCaptureEnabled;

    public event EventHandler? OnActivated;

    public event EventHandler? OnDeactivated;

    public Task<OperationResult> SendSessionEventAsync(
        string type,
        JsonObject payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        return managedHostConnection is not null
            ? managedHostConnection.SendSessionEventAsync(type, payload, cancellationToken)
            : nativeRuntime.SendSessionEventAsync(
                type,
                payload.ToJsonString(PairingJson.Compact),
                cancellationToken);
    }

    private void InitializeRuntimeFeatures()
    {
        foreach (var feature in options.RuntimeFeatures ?? Array.Empty<IRuntimeFeature>())
        {
            try
            {
                feature.Initialize(this);
            }
            catch (Exception exception)
            {
                Logger.Warning($"Runtime feature '{feature.Id}' could not be initialized: {exception.Message}");
            }
        }
    }

    internal event EventHandler? CustomPropertiesChanged;

    internal async Task<OperationResult> SendBinaryExtensionAsync(
        string action,
        JsonObject payload,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (managedHostConnection is not null)
        {
            return await managedHostConnection.SendBinaryExtensionAsync(
                action,
                payload,
                fileName,
                mimeType,
                content,
                cancellationToken);
        }

        const int chunkBytes = 64 * 1024;
        var transferId = Guid.NewGuid();
        var requestPayload = payload.DeepClone().AsObject();
        requestPayload["transfer"] = new JsonObject
        {
            ["transferId"] = transferId.ToString("N"),
            ["fileName"] = fileName,
            ["mimeType"] = mimeType,
            ["sizeBytes"] = content.Length,
            ["chunkBytes"] = chunkBytes,
            ["wireProtocol"] = PairingFileTransferWireProtocol.ProtocolName
        };

        var readyResult = await nativeRuntime.SendControlRequestAsync(
            action.Trim(),
            requestPayload.ToJsonString(PairingJson.Compact),
            cancellationToken);
        if (!readyResult.Success)
        {
            return readyResult;
        }

        var sequence = 0;
        var offset = 0;
        while (offset < content.Length)
        {
            var length = Math.Min(chunkBytes, content.Length - offset);
            var frame = PairingFileTransferWireProtocol.CreateFrame(
                transferId,
                PairingFileTransferFrameType.Chunk,
                sequence,
                offset,
                content.Span.Slice(offset, length));
            var sendResult = await nativeRuntime.SendBinaryAsync(frame, cancellationToken);
            if (!sendResult.Success)
            {
                return sendResult;
            }

            sequence++;
            offset += length;
        }

        var completeFrame = PairingFileTransferWireProtocol.CreateFrame(
            transferId,
            PairingFileTransferFrameType.Complete,
            sequence,
            offset,
            ReadOnlySpan<byte>.Empty);
        var completeResult = await nativeRuntime.SendBinaryAsync(completeFrame, cancellationToken);
        return completeResult.Success
            ? OperationResult.FromSuccess("Extension payload sent.")
            : completeResult;
    }

    public void Activate()
    {
        lock (samplerLock)
        {
            if (IsActive)
            {
                return;
            }

            if (!usesNativeRuntime && ShouldTrackFps())
            {
                frameRateMonitor.Start();
            }

            if (!usesNativeRuntime && ShouldTrackBatteryLevel())
            {
                batteryLevelMonitor.Start();
            }

            samplerThread = new MemorySamplerThread(
                options.SampleFrequencyMilliseconds,
                snapshot =>
                {
                    if (usesNativeRuntime)
                    {
                        RecordManagedRuntimeSamples(snapshot);
                        SyncNativeTelemetry();
                    }
                    else
                    {
                        mutableDataSink.RecordMemorySnapshot(snapshot);
                        RecordFrameSample();
                        RecordBatteryLevelSample();
                    }
                },
                sampleJniReferenceCount: options.EnableJniReferenceCountTracking,
                sampleOpenFileHandleCount: options.EnableOpenFileHandleTracking && !usesNativeRuntime);

            if (!usesNativeRuntime)
            {
                touchCaptureSession.Start();
            }

            if (usesNativeRuntime)
            {
                nativeRuntime.Activate();
                SyncNativeTelemetry();
            }
            IsActive = true;
        }

        managedHostConnection?.OnRuntimeActivated();
        OnActivated?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        lock (samplerLock)
        {
            if (!IsActive)
            {
                return;
            }

            samplerThread?.Dispose();
            samplerThread = null;
            if (!usesNativeRuntime)
            {
                touchCaptureSession.Stop();
            }
            else
            {
                nativeRuntime.Deactivate();
                SyncNativeTelemetry();
            }
            IsActive = false;
        }

        if (!usesNativeRuntime)
        {
            frameRateMonitor.Stop();
            batteryLevelMonitor.Stop();
        }
        managedHostConnection?.OnRuntimeDeactivated();
        OnDeactivated?.Invoke(this, EventArgs.Empty);
    }

    private void RecordManagedRuntimeSamples(MemorySnapshot snapshot)
    {
        if (options.DefaultMemoryChannels.HasFlag(DefaultMemoryChannels.ManagedHeap))
        {
            nativeRuntime.Metric(
                snapshot.ManagedHeapBytes,
                Constants.ReservedChannels.ClrMemoryUsage_Id);
        }

#if ANDROID
        if (options.EnableJniReferenceCountTracking && snapshot.JniReferenceCount is long jniReferenceCount)
        {
            nativeRuntime.Metric(
                jniReferenceCount,
                Constants.ReservedChannels.JniReferenceCount_Id);
        }

#endif
    }

    private void SyncNativeTelemetry()
    {
        if (!usesNativeRuntime)
        {
            return;
        }

        lock (nativeTelemetrySyncLock)
        {
            try
            {
                var snapshot = nativeRuntime.ReadTelemetrySnapshot(
                    lastNativeMetricSequence,
                    lastNativeEventSequence);
                foreach (var metric in snapshot.Metrics.OrderBy(metric => metric.Sequence))
                {
                    mutableDataSink.ImportNativeMetric(
                        metric.Value,
                        metric.Channel,
                        metric.CapturedAtUtc);
                    lastNativeMetricSequence = Math.Max(lastNativeMetricSequence, metric.Sequence);
                }

                foreach (var nativeEvent in snapshot.Events.OrderBy(nativeEvent => nativeEvent.Sequence))
                {
                    mutableDataSink.ImportNativeEvent(
                        nativeEvent.Label,
                        nativeEvent.Type,
                        nativeEvent.Channel,
                        nativeEvent.Details,
                        nativeEvent.CapturedAtUtc);
                    lastNativeEventSequence = Math.Max(lastNativeEventSequence, nativeEvent.Sequence);
                }
            }
            catch (Exception exception)
            {
                Logger.Warning($"Could not refresh the .NET view of native telemetry: {exception.Message}");
            }
        }
    }

    private void RecordFrameSample()
    {
        if (!ShouldTrackFps())
        {
            return;
        }

        var fps = frameRateMonitor.ConsumeFramesPerSecond();

        // Skip recording until we have a meaningful sample.
        if (fps <= 0)
        {
            return;
        }

        mutableDataSink.Metric(fps, Constants.ReservedChannels.FramesPerSecond_Id);
    }

    public void EnableFramesPerSecond()
    {
        fpsTrackingEnabled = true;
        if (usesNativeRuntime)
        {
            nativeRuntime.EnableFramesPerSecond();
        }
        else if (IsActive)
        {
            frameRateMonitor.Start();
        }
    }

    public void DisableFramesPerSecond()
    {
        fpsTrackingEnabled = false;
        if (usesNativeRuntime)
        {
            nativeRuntime.DisableFramesPerSecond();
        }
        else
        {
            frameRateMonitor.Stop();
        }
    }

    public void EnableTouchCapture()
    {
        TouchCaptureHub.EnableRuntimeCapture();
        if (usesNativeRuntime)
        {
            nativeRuntime.EnableTouchCapture();
        }
    }

    public void DisableTouchCapture()
    {
        TouchCaptureHub.DisableRuntimeCapture();
        if (usesNativeRuntime)
        {
            nativeRuntime.DisableTouchCapture();
        }
    }

    public void SetTouchCaptureGuard(Func<bool>? guard)
    {
        TouchCaptureHub.SetRuntimeCaptureGuard(guard);
        if (usesNativeRuntime)
        {
            nativeRuntime.SetTouchCaptureGuard(guard);
        }
    }

    private bool ShouldTrackFps() => fpsTrackingEnabled;

    private void RecordBatteryLevelSample()
    {
        if (!ShouldTrackBatteryLevel())
        {
            return;
        }

        var batteryLevelPercentage = batteryLevelMonitor.ReadBatteryLevelPercentage();
        if (!batteryLevelPercentage.HasValue)
        {
            return;
        }

        mutableDataSink.RecordBatteryLevel(batteryLevelPercentage.Value);
    }

    private bool ShouldTrackBatteryLevel() => batteryLevelTrackingEnabled;

    public void Metric(long value, byte channel)
    {
        if (usesNativeRuntime)
        {
            nativeRuntime.Metric(value, channel);
            SyncNativeTelemetry();
        }
        else
        {
            mutableDataSink.Metric(value, channel);
        }
    }

    public void Event(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(
            label,
            AppEventType.Info,
            details: null,
            Constants.ReservedChannels.ChannelNotSpecified_Id);
    }

    public void Event(string label, AppEventType type)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(
            label,
            type,
            details: null,
            Constants.ReservedChannels.ChannelNotSpecified_Id);
    }

    public void Event(string label, AppEventType type, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(
            label,
            type,
            details,
            Constants.ReservedChannels.ChannelNotSpecified_Id);
    }

    public void Event(string label, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(label, AppEventType.Info, details: null, channel);
    }

    public void Event(string label, AppEventType type, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(label, type, details: null, channel);
    }

    public void Event(string label, AppEventType type, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        RecordEvent(label, type, details, channel);
    }

    private void RecordEvent(
        string label,
        AppEventType type,
        string? details,
        byte channel)
    {
        if (usesNativeRuntime)
        {
            nativeRuntime.Event(label, type, details, channel);
            SyncNativeTelemetry();
            return;
        }

        mutableDataSink.Event(label, type, channel, details ?? string.Empty);
    }

    public void ScreenViewed(string screenName)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        RecordScreenView(screenName, details: null, Constants.ReservedChannels.ChannelNotSpecified_Id);
    }

    public void ScreenViewed(string screenName, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        RecordScreenView(screenName, details, Constants.ReservedChannels.ChannelNotSpecified_Id);
    }

    public void ScreenViewed(string screenName, byte channel)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        RecordScreenView(screenName, details: null, channel);
    }

    public void ScreenViewed(string screenName, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        RecordScreenView(screenName, details, channel);
    }

    private void RecordScreenView(string screenName, string? details, byte channel)
    {
        if (usesNativeRuntime)
        {
            nativeRuntime.ScreenViewed(screenName, details, channel);
            SyncNativeTelemetry();
            return;
        }

        mutableDataSink.ScreenViewed(screenName, channel, details ?? string.Empty);
    }

    public void RegisterCustomProperty(string group, string key, object? value)
    {
        customProperties.Register(group, key, value);
        if (usesNativeRuntime)
        {
            nativeRuntime.RegisterCustomProperty(group, key, value);
        }
        CustomPropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveCustomProperty(string group, string key)
    {
        var removed = customProperties.Remove(group, key);
        if (removed)
        {
            if (usesNativeRuntime)
            {
                nativeRuntime.RemoveCustomProperty(group, key);
            }
            CustomPropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void ClearCustomProperties()
    {
        if (customProperties.IsEmpty)
        {
            return;
        }

        customProperties.Clear();
        if (usesNativeRuntime)
        {
            nativeRuntime.ClearCustomProperties();
        }
        CustomPropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    internal SessionCustomProperties CreateCustomPropertiesSnapshot()
    {
        return customProperties.Clone();
    }

    public void Clear()
    {
        if (usesNativeRuntime)
        {
            nativeRuntime.Clear();
            lock (nativeTelemetrySyncLock)
            {
                lastNativeMetricSequence = 0;
                lastNativeEventSequence = 0;
                mutableDataSink.Clear();
            }
        }
        else
        {
            mutableDataSink.Clear();
        }
    }

    internal string? RecordCrashCandidate(
        string runtime,
        string kind,
        string? message,
        string? stack,
        bool fatal,
        string? metadataJson = null)
    {
        return nativeRuntime.RecordCrashCandidate(runtime, kind, message, stack, fatal, metadataJson);
    }

    internal string PendingCrashReportsJson() => nativeRuntime.PendingCrashReportsJson();

    internal void AssociateOfflineCaptureSession(string sessionId, string? directory)
    {
        nativeRuntime.AssociateOfflineCaptureSession(sessionId, directory);
    }

    internal void CompleteOfflineCaptureSession(string sessionId)
    {
        nativeRuntime.CompleteOfflineCaptureSession(sessionId);
    }

    internal bool MarkCrashReportPersistedToOfflineCapture(string reportId)
    {
        return nativeRuntime.MarkCrashReportPersistedToOfflineCapture(reportId);
    }

    internal bool SetAppLifecycleState(
        AppLifecycleState state,
        DateTimeOffset? changedAtUtc,
        bool emitTransitionEvent,
        long version)
    {
        lock (appLifecycleLock)
        {
            if (version < appLifecycleVersion)
            {
                return false;
            }

            appLifecycleVersion = version;
        }

        var effectiveChangedAtUtc = changedAtUtc ?? DateTimeOffset.UtcNow;
        var changed = mutableDataSink.SetAppLifecycleState(
            state,
            effectiveChangedAtUtc,
            usesNativeRuntime ? false : emitTransitionEvent);
        if (changed)
        {
            if (usesNativeRuntime)
            {
                nativeRuntime.SetAppLifecycleState(state, effectiveChangedAtUtc);
                SyncNativeTelemetry();
            }
        }

        return changed;
    }
}
