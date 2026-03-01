namespace Ansight;

/// <summary>
/// Entry point for initialising, presenting, and recording data with Ansight.
/// </summary>
public static class Runtime
{
    private static readonly Lock runtimeLock = new Lock();

    private static RuntimeImpl? runtime;

    /// <summary>
    /// The singleton runtime instance; throws if <see cref="Initialize(Options)"/> has not been called first.
    /// </summary>
    public static IRuntime Instance => MutableInstance;

    /// <summary>
    /// Indicates whether the runtime has been initialised via <see cref="Initialize(Options)"/> or <see cref="InitializeAndActivate(Options)"/>.
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (runtimeLock)
            {
                return runtime != null;
            }
        }
    }

    internal static RuntimeImpl MutableInstance
    {
        get
        {
            lock (runtimeLock)
            {
                if (runtime == null)
                {
                    Logger.Error($"Attempted to access Runtime before it was initialized.");
                    throw new InvalidOperationException("You must call 'Runtime.Initialize(Options)' before accessing the Runtime Instance.");
                }
                
                return runtime;
            }
        }
    }
    
    
    private static void Initialize_Internal(bool activateImmediately, 
                                            Options? options,
                                            IPresentationService? presentationService)
    {
        Logger.Info($"Initialising Runtime (activateImmediately: {activateImmediately}).");
        
        lock (runtimeLock)
        {
            if (runtime != null)
            {
                throw new InvalidOperationException("The Runtime has already been initialized.");
            }

            options = options ?? Options.Default;
            PlatformBootstrapper.EnsureConfigured(options);
            Logger.Info($"Using options: sample frequency {options.SampleFrequencyMilliseconds}ms, retention {options.RetentionPeriodSeconds}s, additional channels {options.AdditionalChannels?.Count ?? 0}.");
            
            if (options.AdditionalLogger != null)
            {
                Logger.RegisterCallback(options.AdditionalLogger);
            }

            runtime = new RuntimeImpl(options, presentationService);
            Logger.Info($"Runtime initialisation complete.");
        }

        if (activateImmediately)
        {
            Logger.Info($"Activate immediately requested post initialisation.");
            Activate();
        }
    }

    /// <summary>
    /// Initialises the <see cref="IRuntime"/> using the provided <paramref name="options"/> and immediately begins monitoring memory usage.
    /// </summary>
    public static void InitializeAndActivate(Options? options = null, IPresentationService? presentationService = null)
    {
        Initialize_Internal(activateImmediately: true, options, presentationService);
    }

    /// <summary>
    /// Initialises the <see cref="IRuntime"/> using the provided <paramref name="options"/>.
    /// <para/>
    /// Does not start the memory tracker, use <see cref="Activate"/> to start tracking.
    /// </summary>
    public static void Initialize(Options? options = null, IPresentationService? presentationService = null)
    {   
        Initialize_Internal(activateImmediately: false, options, presentationService);
    }
    
    /// <summary>
    /// Starts memory sampling and raises OnActivated when complete.
    /// </summary>
    public static void Activate()
    {
        if (!IsInitialized)
        {
            return;
        }         
        
        Instance.Activate();
    }

    /// <summary>
    /// Stops memory sampling and raises OnDeactivated when complete.
    /// </summary>
    public static void Deactivate()
    {
        if (!IsInitialized)
        {
            return;
        }         
        
        Instance.Deactivate();
    }

    /// <summary>
    /// Clears the backing data sink, removing all recorded metrics and events.
    /// </summary>
    public static void Clear()
    {
        if (!IsInitialized)
        {
            return;
        }         
        
        Instance.Clear();
    }

    /// <summary>
    /// If Ansight is currently performing memory tracking. 
    /// </summary>
    /// <returns></returns>
    public static bool IsActive
    {
        get
        {
            if (!IsInitialized)
            {
                return false;
            }            
            
            return Instance.IsActive;
        }
    }

    /// <summary>
    /// Returns true when the slide-in sheet UI is currently presented.
    /// </summary>
    public static bool IsSheetPresented
    {
        get
        {

            if (!IsInitialized)
            {
                return false;
            }            
            
            return Instance.IsSheetPresented;
        }
    }
    
    /// <summary>
    /// Returns true when the slide-in sheet UI is currently presented.
    /// </summary>
    public static bool IsOverlayPresented
    {
        get
        {
            if (!IsInitialized)
            {
                return false;
            }            
            
            return Instance.IsOverlayPresented;
        }
    }

    /// <summary>
    /// Indicates whether FPS tracking is currently enabled.
    /// </summary>
    public static bool IsFramesPerSecondEnabled
    {
        get
        {
            if (!IsInitialized)
            {
                return false;
            }

            return Instance.IsFramesPerSecondEnabled;
        }
    }
    
    /// <summary>
    /// Gets or sets how annotated events should render on the chart.
    /// </summary>
    public static AppEventRenderingBehaviour AppEventRenderingBehaviour
    {
        get
        {
            if (!IsInitialized)
            {
                return AppEventRenderingBehaviour.IconsOnly;
            }

            return Instance.AppEventRenderingBehaviour;
        }
        set
        {
            if (!IsInitialized)
            {
                return;
            }

            Instance.AppEventRenderingBehaviour = value;
        }
    }

    /// <summary>
    /// Gets or sets the chart theme applied during rendering.
    /// </summary>
    public static ChartTheme ChartTheme
    {
        get
        {
            if (!IsInitialized)
            {
                return ChartTheme.Dark;
            }

            return Instance.ChartTheme;
        }
        set
        {
            if (!IsInitialized)
            {
                return;
            }

            Instance.ChartTheme = value;
        }
    }

    /// <summary>
    /// Should the slide sheet or overlay be allowed to be presented?
    /// </summary>
    public static bool IsPresentationEnabled
    {
        get
        {

            if (!IsInitialized)
            {
                return false;
            }            
            
            return Instance.IsPresentationEnabled;
        }
    }

    /// <summary>
    /// Returns true when the chart overlay is currently presented.
    /// </summary>
    public static bool IsChartOverlayPresented
    {
        get
        {

            if (!IsInitialized)
            {
                return false;
            }            
            
            return Instance.IsOverlayPresented;
        }
    }

    /// <summary>
    /// Opens the charting presentation as a slide in sheet.
    /// </summary>
    public static void PresentSheet()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.PresentSheet();
    }

    /// <summary>
    /// Dismisses the presented Ansight slide in sheet.
    /// </summary>
    public static void DismissSheet()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.DismissSheet();
    }

    /// <summary>
    /// Shows the chart overlay on the current window using the default overlay position from <see cref="Options"/>.
    /// </summary>
    public static void PresentOverlay()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.PresentOverlay();
    }

    /// <summary>
    /// Shows the chart overlay on the current window at the given position.
    /// </summary>
    public static void PresentOverlay(OverlayPosition position)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.PresentOverlay(position);
    }

    /// <summary>
    /// Enables shake gesture handling if it is configured in <see cref="Options"/>.
    /// </summary>
    public static void EnableShakeGesture()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.EnableShakeGesture();
    }

    /// <summary>
    /// Disables shake gesture handling if previously enabled.
    /// </summary>
    public static void DisableShakeGesture()
    {
        if (!IsInitialized)
        {
            return;
        }
        
        Instance.DisableShakeGesture();
    }

    /// <summary>
    /// Enables frames-per-second tracking when allowed by options.
    /// </summary>
    public static void EnableFramesPerSecond()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.EnableFramesPerSecond();
    }

    /// <summary>
    /// Disables frames-per-second tracking.
    /// </summary>
    public static void DisableFramesPerSecond()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.DisableFramesPerSecond();
    }

    /// <summary>
    /// Hides and disposes the chart overlay.
    /// </summary>
    public static void DismissOverlay()
    {
        if (!IsInitialized)
        {
            return;
        }
        
        Instance.DismissOverlay();
    }
    
    /// <summary>
    /// Captures a new metric using the given <paramref name="value"/> against the <paramref name="channel"/>
    /// <para/>
    /// The provided <paramref name="channel"/> <b>must</b> be a built-in channel or predefined during your setup of Ansight via <see cref="Runtime.Initialize"/>.
    /// <para/>
    /// Metrics recorded against unknown channels will be discarded.
    /// </summary>
    public static void Metric(long value, byte channel)
    {
        if (!IsInitialized)
        {
            return;
        }
        
        Instance.Metric(value, channel);
    }
    
    
    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <see cref="Constants.ReservedChannels.ChannelNotSpecified_Id"/> channel using the default event type.
    /// </summary>
    public static void Event(string label)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.Event(label);
    }

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <see cref="Constants.ReservedChannels.ChannelNotSpecified_Id"/> channel using the provided type.
    /// </summary>
    public static void Event(string label, AppEventType type)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.Event(label, type);
    }

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> and <paramref name="type"/> with the provided <paramref name="details"/> against the unspecified channel.
    /// </summary>
    public static void Event(string label, AppEventType type, string details)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        }

        Instance.Event(label, type, details);
    }
    
    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/> using the default event type.
    /// <para/>
    /// The provided <paramref name="channel"/> <b>must</b> be a built-in channel or predefined during your setup of Ansight via <see cref="Runtime.Initialize"/>.
    /// <para/>
    /// Event's recorded against unknown channels will be discarded.
    /// </summary>
    public static void Event(string label, byte channel)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        }
        
        Instance.Event(label, channel);
    }
    
    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/> using the given 
    /// <para/>
    /// The provided <paramref name="channel"/> <b>must</b> be a built-in channel or predefined during your setup of Ansight via <see cref="Runtime.Initialize"/>.
    /// <para/>
    /// Event's recorded against unknown channels will be discarded.
    /// </summary>
    public static void Event(string label, AppEventType type, byte channel)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        }
        
        Instance.Event(label, type, channel);
    }
    
    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/> using the provided <paramref name="type"/> with the additional <paramref name="details"/>.
    /// <para/>
    /// The provided <paramref name="channel"/> <b>must</b> be a built-in channel or predefined during your setup of Ansight via <see cref="Runtime.Initialize"/>.
    /// <para/>
    /// Event's recorded against unknown channels will be discarded.
    /// </summary>
    public static void Event(string label, AppEventType type, byte channel, string details)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));
        }
        
        Instance.Event(label, type, channel, details);
    }
}
