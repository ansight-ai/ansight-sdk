namespace Ansight;

/// <summary>
/// Entry point for initialising and recording telemetry data with Ansight.
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
                    Logger.Error("Attempted to access Runtime before it was initialized.");
                    throw new InvalidOperationException("You must call 'Runtime.Initialize(Options)' before accessing the Runtime Instance.");
                }

                return runtime;
            }
        }
    }

    private static void InitializeInternal(bool activateImmediately, Options? options)
    {
        Logger.Info($"Initialising Runtime (activateImmediately: {activateImmediately}).");

        lock (runtimeLock)
        {
            if (runtime != null)
            {
                throw new InvalidOperationException("The Runtime has already been initialized.");
            }

            options ??= Options.Default;
            PlatformBootstrapper.EnsureConfigured();
            Logger.Info($"Using options: sample frequency {options.SampleFrequencyMilliseconds}ms, retention {options.RetentionPeriodSeconds}s, additional channels {options.AdditionalChannels?.Count ?? 0}.");

            if (options.AdditionalLogger != null)
            {
                Logger.RegisterCallback(options.AdditionalLogger);
            }

            runtime = new RuntimeImpl(options);
            Logger.Info("Runtime initialisation complete.");
        }

        if (activateImmediately)
        {
            Logger.Info("Activate immediately requested post initialisation.");
            Activate();
        }
    }

    /// <summary>
    /// Initialises the <see cref="IRuntime"/> using the provided <paramref name="options"/> and immediately begins monitoring.
    /// </summary>
    public static void InitializeAndActivate(Options? options = null)
    {
        InitializeInternal(activateImmediately: true, options);
    }

    /// <summary>
    /// Initialises the <see cref="IRuntime"/> using the provided <paramref name="options"/>.
    /// <para/>
    /// Does not start telemetry tracking; use <see cref="Activate"/> to start tracking.
    /// </summary>
    public static void Initialize(Options? options = null)
    {
        InitializeInternal(activateImmediately: false, options);
    }

    /// <summary>
    /// Starts sampling and raises OnActivated when complete.
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
    /// Stops sampling and raises OnDeactivated when complete.
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
    /// If Ansight is currently performing telemetry sampling.
    /// </summary>
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
    /// Enables frames-per-second tracking.
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
    /// Captures a new metric using the given <paramref name="value"/> against the <paramref name="channel"/>.
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
    /// Captures a new event using the given <paramref name="label"/> against the unspecified channel.
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
    /// Captures a new event using the given <paramref name="label"/> against the unspecified channel using the provided type.
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
    /// Captures a new event using the given <paramref name="label"/>, <paramref name="type"/>, and <paramref name="details"/> against the unspecified channel.
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
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/>.
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
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/> using the given type.
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
