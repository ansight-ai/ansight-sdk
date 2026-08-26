namespace Ansight;

using Ansight.Pairing;
using Ansight.Platforms;
using Ansight.Tools;
using Ansight.Network;
using System.Text.Json;

/// <summary>
/// Entry point for initialising and recording telemetry data with Ansight.
/// </summary>
public static class Runtime
{
    private static readonly Lock runtimeLock = new Lock();

    private static RuntimeImpl? runtime;
    private static AppLifecycleState currentAppLifecycleState = AppLifecycleState.Unknown;
    private static DateTimeOffset? currentAppLifecycleStateChangedUtc;
    private static long appLifecycleStateVersion;
    private static int unhandledExceptionHandlerInstalled;

    internal static event EventHandler<AppLifecycleStateChangedEventArgs>? AppLifecycleStateChanged;

    /// <summary>
    /// The singleton runtime instance; throws if <see cref="Initialize(Options)"/> has not been called first.
    /// </summary>
    public static IRuntime Instance => MutableInstance;

    /// <summary>
    /// Controls the unified runtime-owned host connection surface.
    /// Returns a no-op controller until the runtime is initialized.
    /// </summary>
    public static IHostConnection HostConnection => IsInitialized
        ? MutableInstance.HostConnection
        : NullHostConnection.Instance;

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

        RuntimeImpl createdRuntime;
        AppLifecycleState initialAppLifecycleState;
        DateTimeOffset? initialAppLifecycleStateChangedUtc;
        long initialAppLifecycleStateVersion;

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
            createdRuntime = runtime;
            EnsureUnhandledExceptionHandler(options);
            initialAppLifecycleState = currentAppLifecycleState;
            initialAppLifecycleStateChangedUtc = currentAppLifecycleStateChangedUtc;
            initialAppLifecycleStateVersion = appLifecycleStateVersion;
            Logger.Info("Runtime initialisation complete.");
        }

        createdRuntime.SetAppLifecycleState(
            initialAppLifecycleState,
            initialAppLifecycleStateChangedUtc,
            emitTransitionEvent: initialAppLifecycleState is AppLifecycleState.Foreground or AppLifecycleState.Background,
            initialAppLifecycleStateVersion);

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
    /// Identifies this operating-system process for live, offline, and crash-session correlation.
    /// </summary>
    public static string? ProcessSessionId => IsInitialized ? MutableInstance.ProcessSessionId : null;

    /// <summary>
    /// Records one completed HTTP request for live host streaming and active offline captures.
    /// The record is sanitized again at ingestion, even when it was produced by a built-in handler.
    /// </summary>
    /// <param name="request">Completed request metadata with optional bounded request and response bodies.</param>
    public static void RecordNetworkRequest(NetworkRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsInitialized || !MutableInstance.HostConnection.IsConnected)
        {
            return;
        }

        MutableInstance.RecordNetworkRequest(request);
    }

    /// <summary>
    /// Enables or disables sensitive-value redaction for subsequent network captures without
    /// restarting the runtime. Redaction is enabled by default.
    /// </summary>
    public static void SetNetworkCaptureRedactionEnabled(bool enabled)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.SetNetworkCaptureRedactionEnabled(enabled);
    }

    /// <summary>
    /// Adds framework-specific context to the durable native crash outbox.
    /// Non-fatal candidates are retained only as context for independently confirmed native exits.
    /// </summary>
    public static string? RecordCrashCandidate(
        Exception exception,
        bool fatal = true,
        string runtimeName = "dotnet")
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!IsInitialized)
        {
            return null;
        }

        return MutableInstance.RecordCrashCandidate(
            runtimeName,
            "unhandled_exception",
            exception.Message,
            exception.ToString(),
            fatal,
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
            }));
    }

    private static void EnsureUnhandledExceptionHandler(Options options)
    {
        if (!options.CrashCapture.Enabled ||
            Interlocked.Exchange(ref unhandledExceptionHandlerInstalled, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            try
            {
                var exception = eventArgs.ExceptionObject as Exception;
                if (exception is not null)
                {
                    RecordCrashCandidate(exception, eventArgs.IsTerminating);
                }
                else
                {
                    Volatile.Read(ref runtime)?.RecordCrashCandidate(
                        "dotnet",
                        "unhandled_exception",
                        Convert.ToString(eventArgs.ExceptionObject),
                        stack: null,
                        fatal: eventArgs.IsTerminating);
                }
            }
            catch
            {
                // A crash hook must never replace the app's original termination path.
            }
        };
    }

    /// <summary>
    /// Sends a single client log line to the connected host over the live pairing session.
    /// </summary>
    public static Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return HostConnection.SendClientLogAsync(logLine, progress, cancellationToken);
    }

    /// <summary>
    /// The current app lifecycle state tracked by Ansight.
    /// </summary>
    public static AppLifecycleState CurrentAppLifecycleState
    {
        get
        {
            lock (runtimeLock)
            {
                return currentAppLifecycleState;
            }
        }
    }

    /// <summary>
    /// The UTC timestamp when <see cref="CurrentAppLifecycleState"/> last changed.
    /// </summary>
    public static DateTimeOffset? CurrentAppLifecycleStateChangedUtc
    {
        get
        {
            lock (runtimeLock)
            {
                return currentAppLifecycleStateChangedUtc;
            }
        }
    }

    /// <summary>
    /// Sets the current app lifecycle state.
    /// </summary>
    public static void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset? changedAtUtc = null)
    {
        RuntimeImpl? currentRuntime;
        AppLifecycleStateChangedEventArgs? eventArgs;
        long version;
        var effectiveChangedAtUtc = (changedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        lock (runtimeLock)
        {
            if (currentAppLifecycleState == state)
            {
                return;
            }

            currentAppLifecycleState = state;
            currentAppLifecycleStateChangedUtc = effectiveChangedAtUtc;
            version = ++appLifecycleStateVersion;
            currentRuntime = runtime;
            eventArgs = new AppLifecycleStateChangedEventArgs(state, effectiveChangedAtUtc);
        }

        currentRuntime?.SetAppLifecycleState(
            state,
            effectiveChangedAtUtc,
            emitTransitionEvent: true,
            version);
        AppLifecycleStateChanged?.Invoke(null, eventArgs);
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
    /// Indicates whether touch capture is configured and allowed by the runtime-level capture toggle.
    /// </summary>
    public static bool IsTouchCaptureEnabled
    {
        get
        {
            if (!IsInitialized)
            {
                return false;
            }

            return Instance.IsTouchCaptureEnabled;
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
    /// Enables runtime-level touch capture emission when touch capture was configured at initialization.
    /// </summary>
    public static void EnableTouchCapture()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.EnableTouchCapture();
    }

    /// <summary>
    /// Disables runtime-level touch capture emission without changing the runtime activation state.
    /// </summary>
    public static void DisableTouchCapture()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.DisableTouchCapture();
    }

    /// <summary>
    /// Sets an optional runtime-level guard that is evaluated before each captured touch is emitted.
    /// Return <see langword="true"/> to allow capture, or <see langword="false"/> to suppress it.
    /// Pass <see langword="null"/> to clear the guard.
    /// </summary>
    public static void SetTouchCaptureGuard(Func<bool>? guard)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.SetTouchCaptureGuard(guard);
    }

    /// <summary>
    /// Returns the runtime tool protocol bridge for querying and executing registered tools.
    /// </summary>
    public static ToolProtocolBridge ToolBridge => MutableInstance.ToolBridge;

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

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> against the unspecified channel.
    /// </summary>
    public static void ScreenViewed(string screenName)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(screenName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));
        }

        Instance.ScreenViewed(screenName);
    }

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> and <paramref name="details"/> against the unspecified channel.
    /// </summary>
    public static void ScreenViewed(string screenName, string details)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(screenName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));
        }

        Instance.ScreenViewed(screenName, details);
    }

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> against the <paramref name="channel"/>.
    /// </summary>
    public static void ScreenViewed(string screenName, byte channel)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(screenName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));
        }

        Instance.ScreenViewed(screenName, channel);
    }

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/>, <paramref name="channel"/>, and <paramref name="details"/>.
    /// </summary>
    public static void ScreenViewed(string screenName, byte channel, string details)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(screenName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(screenName));
        }

        Instance.ScreenViewed(screenName, channel, details);
    }

    /// <summary>
    /// Registers or replaces a custom grouped property for current and future live pairing sessions.
    /// </summary>
    public static void RegisterCustomProperty(string group, string key, object? value)
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.RegisterCustomProperty(group, key, value);
    }

    /// <summary>
    /// Removes a custom grouped property from current and future live pairing sessions.
    /// </summary>
    public static bool RemoveCustomProperty(string group, string key)
    {
        if (!IsInitialized)
        {
            return false;
        }

        return Instance.RemoveCustomProperty(group, key);
    }

    /// <summary>
    /// Clears all custom grouped properties from current and future live pairing sessions.
    /// </summary>
    public static void ClearCustomProperties()
    {
        if (!IsInitialized)
        {
            return;
        }

        Instance.ClearCustomProperties();
    }
}
