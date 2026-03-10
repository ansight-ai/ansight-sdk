using System.Diagnostics.CodeAnalysis;

namespace Ansight;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class RuntimeImpl : IRuntime
{
    private readonly Options options;
    private MemorySamplerThread? samplerThread;
    private readonly Lock samplerLock = new Lock();

    private readonly MutableDataSink mutableDataSink;
    private readonly IFrameRateMonitor frameRateMonitor;
    private bool fpsTrackingEnabled;

    public IDataSink DataSink => mutableDataSink;

    public RuntimeImpl(Options options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        Logger.Info($"Creating runtime with sample frequency {options.SampleFrequencyMilliseconds}ms, retention {options.RetentionPeriodSeconds}s, additional channels: {options.AdditionalChannels?.Count ?? 0}.");
        mutableDataSink = new MutableDataSink(options);
        Logger.Info("Mutable data sink created.");
        frameRateMonitor = FrameRateMonitorFactory.Create();
        fpsTrackingEnabled = options.EnableFramesPerSecond;
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

    public void Clear()
    {
        Logger.Info("Clearing data sink contents.");
        mutableDataSink.Clear();
    }
}
