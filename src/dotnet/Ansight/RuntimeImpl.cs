using System.Diagnostics.CodeAnalysis;
using Ansight.Pairing;
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
    private readonly HostConnectionManager hostConnection;
    private readonly HostPairingManager hostPairing;
    private bool fpsTrackingEnabled;

    public IDataSink DataSink => mutableDataSink;
    internal Options Options => options;
    internal PairingBinaryTransferHub BinaryTransferHub { get; } = new();
    internal AppLifecycleState CurrentAppLifecycleState => mutableDataSink.CurrentAppLifecycleState;
    internal DateTimeOffset? CurrentAppLifecycleStateChangedUtc => mutableDataSink.CurrentAppLifecycleStateChangedUtc;

    public ToolProtocolBridge ToolBridge { get; }

    public IHostConnection HostConnection => hostConnection;

    public IHostPairing HostPairing => hostPairing;

    public RuntimeImpl(Options options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        Logger.Info($"Creating runtime with sample frequency {options.SampleFrequencyMilliseconds}ms, retention {options.RetentionPeriodSeconds}s, additional channels: {options.AdditionalChannels?.Count ?? 0}.");
        mutableDataSink = new MutableDataSink(options);
        Logger.Info("Mutable data sink created.");
        frameRateMonitor = FrameRateMonitorFactory.Create();
        fpsTrackingEnabled = options.EnableFramesPerSecond;
        ToolBridge = options.Tools.CreateBridge(options.ToolGuard);
        hostConnection = new HostConnectionManager(this, options.HostAutoProbe);
        hostPairing = new HostPairingManager(hostConnection, options.HostPairing);
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
                Logger.Info("Activate requested but runtime is already active.");
                return;
            }

            if (ShouldTrackFps())
            {
                frameRateMonitor.Start();
            }

            samplerThread = new MemorySamplerThread(options.SampleFrequencyMilliseconds, snapshot =>
            {
                mutableDataSink.RecordMemorySnapshot(snapshot);
                RecordFrameSample();
            });

            IsActive = true;
            Logger.Info($"Memory sampler started with frequency {options.SampleFrequencyMilliseconds}ms.");
        }

        hostConnection.OnRuntimeActivated();
        _ = Task.Run(async () =>
        {
            try
            {
                await hostPairing.HandleRuntimeActivatedAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex);
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
                Logger.Info("Deactivate requested but runtime is already inactive.");
                return;
            }

            samplerThread?.Dispose();
            samplerThread = null;
            IsActive = false;
            Logger.Info("Memory sampler disposed and activity flag cleared.");
        }

        frameRateMonitor.Stop();
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

    public void Metric(long value, byte channel)
    {
        mutableDataSink.Metric(value, channel);
    }

    public void Event(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' on detached channel.");
        mutableDataSink.Event(label);
    }

    public void Event(string label, AppEventType type)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' on detached channel.");
        mutableDataSink.Event(label, type);
    }

    public void Event(string label, AppEventType type, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' and details on detached channel.");
        mutableDataSink.Event(label, type, details);
    }

    public void Event(string label, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' on channel {channel}.");
        mutableDataSink.Event(label, channel);
    }

    public void Event(string label, AppEventType type, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' on channel {channel}.");

        mutableDataSink.Event(label, type, channel);
    }

    public void Event(string label, AppEventType type, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' and details on channel {channel}.");

        mutableDataSink.Event(label, type, channel, details);
    }

    public void ScreenViewed(string screenName)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        Logger.Info($"Recording screen view '{screenName}' on detached channel.");
        mutableDataSink.ScreenViewed(screenName);
    }

    public void ScreenViewed(string screenName, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        Logger.Info($"Recording screen view '{screenName}' with details on detached channel.");
        mutableDataSink.ScreenViewed(screenName, details);
    }

    public void ScreenViewed(string screenName, byte channel)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        Logger.Info($"Recording screen view '{screenName}' on channel {channel}.");
        mutableDataSink.ScreenViewed(screenName, channel);
    }

    public void ScreenViewed(string screenName, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(screenName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));

        Logger.Info($"Recording screen view '{screenName}' with details on channel {channel}.");
        mutableDataSink.ScreenViewed(screenName, channel, details);
    }

    public void Clear()
    {
        Logger.Info("Clearing data sink contents.");
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
