using System.Diagnostics.CodeAnalysis;
using Ansight.Pairing;
using Ansight.Telemetry.Battery;
using Ansight.Telemetry.Memory;
using Ansight.Tools;

namespace Ansight;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class RuntimeImpl : IRuntime
{
    private readonly Options options;
    private MemorySamplerThread? samplerThread;
    private readonly Lock samplerLock = new Lock();
    private readonly Lock appLifecycleLock = new();
    private long appLifecycleVersion;

    private readonly MutableDataSink mutableDataSink;
    private readonly IFrameRateMonitor frameRateMonitor;
    private readonly IBatteryLevelMonitor batteryLevelMonitor;
    private readonly ITouchCaptureSession touchCaptureSession;
    private readonly HostSessionManager hostConnection;
    private readonly HostPairingManager hostPairing;
    private bool fpsTrackingEnabled;
    private readonly bool batteryLevelTrackingEnabled;

    public IDataSink DataSink => mutableDataSink;
    internal Options Options => options;
    internal PairingBinaryTransferHub BinaryTransferHub { get; } = new();
    internal TouchCaptureHub TouchCaptureHub { get; }
    internal AppLifecycleState CurrentAppLifecycleState => mutableDataSink.CurrentAppLifecycleState;
    internal DateTimeOffset? CurrentAppLifecycleStateChangedUtc => mutableDataSink.CurrentAppLifecycleStateChangedUtc;

    public ToolProtocolBridge ToolBridge { get; }

    public IHostConnection HostConnection => hostPairing;

    public RuntimeImpl(Options options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        mutableDataSink = new MutableDataSink(options);
        frameRateMonitor = FrameRateMonitorFactory.Create();
        batteryLevelMonitor = BatteryLevelMonitorFactory.Create();
        TouchCaptureHub = new TouchCaptureHub(options.TouchCapture);
        touchCaptureSession = TouchCaptureSupport.CreateSession(TouchCaptureHub);
        fpsTrackingEnabled = options.EnableFramesPerSecond;
        batteryLevelTrackingEnabled = options.EnableBatteryLevel && batteryLevelMonitor.IsSupported;
        ToolBridge = options.Tools.CreateBridge(options.ToolGuard);
        hostConnection = new HostSessionManager(
            this,
            options.HostAutoProbe,
            cachedProfileRetention: options.HostConnection.ConnectionProfileRetention);
        hostPairing = new HostPairingManager(hostConnection, options.HostConnection);
    }

    public bool IsActive { get; private set; }

    public bool IsFramesPerSecondEnabled => fpsTrackingEnabled;

    public event EventHandler? OnActivated;

    public event EventHandler? OnDeactivated;

    public void Activate()
    {
        lock (samplerLock)
        {
            if (IsActive)
            {
                return;
            }

            if (ShouldTrackFps())
            {
                frameRateMonitor.Start();
            }

            if (ShouldTrackBatteryLevel())
            {
                batteryLevelMonitor.Start();
            }

            samplerThread = new MemorySamplerThread(options.SampleFrequencyMilliseconds, snapshot =>
            {
                mutableDataSink.RecordMemorySnapshot(snapshot);
                RecordFrameSample();
                RecordBatteryLevelSample();
            });

            IsActive = true;
            touchCaptureSession.Start();
        }

        hostConnection.OnRuntimeActivated();
        _ = Task.Run(async () =>
        {
            try
            {
                await hostPairing.HandleRuntimeActivatedAsync(CancellationToken.None);
            }
            catch
            {
            }
        }, CancellationToken.None);
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
            touchCaptureSession.Stop();
            IsActive = false;
        }

        frameRateMonitor.Stop();
        batteryLevelMonitor.Stop();
        hostConnection.OnRuntimeDeactivated();
        OnDeactivated?.Invoke(this, EventArgs.Empty);
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
        if (IsActive)
        {
            frameRateMonitor.Start();
        }
    }

    public void DisableFramesPerSecond()
    {
        fpsTrackingEnabled = false;
        frameRateMonitor.Stop();
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
        mutableDataSink.Metric(value, channel);
    }

    public void Event(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label);
    }

    public void Event(string label, AppEventType type)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label, type);
    }

    public void Event(string label, AppEventType type, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label, type, details);
    }

    public void Event(string label, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label, channel);
    }

    public void Event(string label, AppEventType type, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label, type, channel);
    }

    public void Event(string label, AppEventType type, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        mutableDataSink.Event(label, type, channel, details);
    }

    public void ScreenViewed(string screenName)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        mutableDataSink.ScreenViewed(screenName);
    }

    public void ScreenViewed(string screenName, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        mutableDataSink.ScreenViewed(screenName, details);
    }

    public void ScreenViewed(string screenName, byte channel)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        mutableDataSink.ScreenViewed(screenName, channel);
    }

    public void ScreenViewed(string screenName, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        mutableDataSink.ScreenViewed(screenName, channel, details);
    }

    public void Clear()
    {
        mutableDataSink.Clear();
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

        return mutableDataSink.SetAppLifecycleState(state, changedAtUtc, emitTransitionEvent);
    }
}
