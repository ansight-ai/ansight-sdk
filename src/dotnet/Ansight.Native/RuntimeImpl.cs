using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Ansight;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class RuntimeImpl : IRuntime
{
    private readonly Options options;
    
    private readonly IPresentationService presentationService;
    private MemorySamplerThread? samplerThread;
    private readonly Lock samplerLock = new Lock();
    
    private readonly MutableDataSink MutableDataSink;
    private readonly IFrameRateMonitor frameRateMonitor;
    private bool fpsTrackingEnabled;
    private AppEventRenderingBehaviour eventRenderingBehaviour;
    private ChartTheme chartTheme;
    
    private readonly ShakeGestureListener shakeGestureListener;

    public IDataSink DataSink => MutableDataSink;

    internal SaveSnapshotAction? SaveSnapshotAction => options.SaveSnapshotAction;

    public RuntimeImpl(Options options, IPresentationService? presentationService = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        Logger.Info($"Creating runtime with sample frequency {options.SampleFrequencyMilliseconds}ms, retention {options.RetentionPeriodSeconds}s, shake gesture allowed: {options.AllowShakeGesture}, additional channels: {options.AdditionalChannels?.Count ?? 0}.");
        MutableDataSink = new MutableDataSink(options);
        Logger.Info("Mutable data sink created.");
        frameRateMonitor = FrameRateMonitorFactory.Create();
        fpsTrackingEnabled = options.EnableFramesPerSecond;
        eventRenderingBehaviour = options.AppEventRenderingBehaviour;
        chartTheme = options.ChartTheme;
        shakeGestureListener = new ShakeGestureListener(this, options);
        Logger.Info("Shake gesture listener initialised.");
        this.presentationService = presentationService
            ?? RuntimePlatform.CreatePresentationService(this.options, MutableDataSink)
            ?? new NullPresentationService();
    }
    
    public bool IsActive { get; private set; }
    
    public bool IsSheetPresented => presentationService.IsSheetPresented;

    public bool IsPresentationEnabled => presentationService.IsPresentationEnabled;

    public bool IsFramesPerSecondEnabled => fpsTrackingEnabled;
    
    public AppEventRenderingBehaviour AppEventRenderingBehaviour
    {
        get => eventRenderingBehaviour;
        set => eventRenderingBehaviour = value;
    }

    public ChartTheme ChartTheme
    {
        get => chartTheme;
        set => chartTheme = value;
    }
    
    public bool IsOverlayPresented => presentationService.IsOverlayPresented;
    
    public event EventHandler? OnActivated;
    
    public event EventHandler? OnDeactivated;

    internal async Task ExecuteSaveSnapshotActionAsync()
    {
        var action = options.SaveSnapshotAction;
        if (action == null)
        {
            return;
        }

        Snapshot snapshot;
        try
        {
            snapshot = MutableDataSink.Snapshot();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to capture snapshot for save action.");
            Logger.Exception(ex);
            return;
        }

        try
        {
            await action.CopyDelegate(snapshot);
        }
        catch (Exception ex)
        {
            Logger.Error("Save snapshot action delegate threw an exception.");
            Logger.Exception(ex);
        }
    }

    public void Activate()
    {
        lock (samplerLock)
        {
            if (IsActive)
            {
                Logger.Info("Activate requested but runtime is already active.");
                EnableShakeGesture();
                return;
            }

            if (ShouldTrackFps())
            {
                frameRateMonitor.Start();
            }
            samplerThread = new MemorySamplerThread(options.SampleFrequencyMilliseconds, snapshot =>
            {
                MutableDataSink.RecordMemorySnapshot(snapshot);
                RecordFrameSample();
            });
            
            IsActive = true;
            Logger.Info($"Memory sampler started with frequency {options.SampleFrequencyMilliseconds}ms.");
        }

        EnableShakeGesture();
        OnActivated?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        lock (samplerLock)
        {
            if (!IsActive)
            {
                Logger.Info("Deactivate requested but runtime is already inactive.");
                DisableShakeGesture();
                return;
            }

            samplerThread?.Dispose();
            samplerThread = null;
            IsActive = false;
            Logger.Info("Memory sampler disposed and activity flag cleared.");
        }

        frameRateMonitor.Stop();
        DisableShakeGesture();
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

        MutableDataSink.Metric(fps, Constants.ReservedChannels.FramesPerSecond_Id);
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

    public void PresentSheet() => presentationService.PresentSheet();

    public void DismissSheet() => presentationService.DismissSheet();

    public void PresentOverlay()
    {
        PresentOverlay(options.DefaultOverlayPosition);
    }

    public void PresentOverlay(OverlayPosition position)
    {
        presentationService.PresentOverlay(position);
    }

    public void DismissOverlay()
    {
        presentationService.DismissOverlay();
    }
    
    public void EnableShakeGesture()
    {
        if (!options.AllowShakeGesture)
        {
            Logger.Info("Shake gesture enable requested but gestures are disabled in options; disabling listener.");
            shakeGestureListener.Disable();
            return;
        }
        
        if (!IsActive)
        {
            Logger.Info("Shake gesture enable requested while runtime is inactive; disabling instead.");
            shakeGestureListener.Disable();
            return;
        }
        
        shakeGestureListener.Enable();
        Logger.Info("Shake gesture listener enabled.");
    }

    public void DisableShakeGesture()
    {
        shakeGestureListener.Disable();
        Logger.Info("Shake gesture listener disabled.");
    }

    public void Metric(long value, byte channel)
    {
        MutableDataSink.Metric(value, channel);
    }
    
    public void Event(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        
        Logger.Info($"Recording event '{label}' on detached channel.");
        MutableDataSink.Event(label);
    }

    public void Event(string label, AppEventType type)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        
        Logger.Info($"Recording event '{label}' with type '{type}' on detached channel.");
        MutableDataSink.Event(label, type);
    }

    public void Event(string label, AppEventType type, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' and details on detached channel.");
        MutableDataSink.Event(label, type, details);
    }

    public void Event(string label, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        
        Logger.Info($"Recording event '{label}' on channel {channel}.");
        MutableDataSink.Event(label, channel);
    }

    public void Event(string label, AppEventType type, byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        
        Logger.Info($"Recording event '{label}' with type '{type}' on channel {channel}.");
        
        MutableDataSink.Event(label, type, channel);
    }

    public void Event(string label, AppEventType type, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Logger.Info($"Recording event '{label}' with type '{type}' and details on channel {channel}.");

        MutableDataSink.Event(label, type, channel, details);
    }

    public void Clear()
    {
        Logger.Info("Clearing data sink contents.");
        MutableDataSink.Clear();
    }
}
