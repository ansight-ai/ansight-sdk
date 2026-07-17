namespace Ansight.Annotations;

/// <summary>
/// Triggers in-app feedback annotation capture.
/// </summary>
public static partial class Feedback
{
    private static readonly Lock gate = new();
    private static readonly List<SinkEntry> sinkEntries = [];
    private static AnnotationService? service;
    private static string disabledReason = "Annotated feedback has not been enabled. Register it with WithAnnotatedFeedback().";

    /// <summary>
    /// True when annotation capture was explicitly registered and the host application is a Debug build.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            lock (gate)
            {
                return service is not null;
            }
        }
    }

    /// <summary>
    /// Captures and submits supplied feedback without presenting the built-in overlay.
    /// </summary>
    public static Task<AnnotationCaptureResult> CaptureAsync(
        AnnotationCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentService = GetService();
        return currentService is null
            ? Task.FromResult(CreateDisabledResult())
            : currentService.CaptureAsync(request, cancellationToken);
    }

    /// <summary>
    /// Timestamps the request, captures the current app surface and visual trees, then presents the built-in feedback overlay.
    /// </summary>
    public static Task<AnnotationCaptureResult> PresentAsync(CancellationToken cancellationToken = default)
        => PresentWithHostAsync(null, cancellationToken);

    private static Task<AnnotationCaptureResult> PresentWithHostAsync(
        object? overlayHost,
        CancellationToken cancellationToken)
    {
        var currentService = GetService();
        return currentService is null
            ? Task.FromResult(CreateDisabledResult())
            : currentService.PresentAsync(overlayHost, cancellationToken);
    }

    /// <summary>
    /// Registers a live or offline destination for sealed annotation bundles.
    /// </summary>
    public static IDisposable RegisterSink(IAnnotationSink sink)
        => RegisterSinkCore(null, sink);

    internal static IDisposable RegisterSinkForRuntime(IRuntime runtime, IAnnotationSink sink)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return RegisterSinkCore(runtime, sink);
    }

    private static IDisposable RegisterSinkCore(IRuntime? runtime, IAnnotationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(sink.Id);

        var entry = new SinkEntry(runtime, sink);
        lock (gate)
        {
            sinkEntries.RemoveAll(existing =>
                ReferenceEquals(existing.Runtime, runtime) &&
                string.Equals(existing.Sink.Id, sink.Id, StringComparison.OrdinalIgnoreCase));
            sinkEntries.Add(entry);
        }

        return new SinkRegistration(entry);
    }

    internal static void Initialize(AnnotationService annotationService)
    {
        lock (gate)
        {
            service = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
            disabledReason = string.Empty;
        }
    }

    internal static void InitializeDisabled(string reason)
    {
        lock (gate)
        {
            service = null;
            disabledReason = reason;
        }
    }

    internal static IReadOnlyList<IAnnotationSink> GetSinks(IRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (gate)
        {
            return sinkEntries
                .Where(entry => entry.Runtime is null || ReferenceEquals(entry.Runtime, runtime))
                .Select(entry => entry.Sink)
                .ToArray();
        }
    }

    private static AnnotationService? GetService()
    {
        lock (gate)
        {
            return service;
        }
    }

    private static AnnotationCaptureResult CreateDisabledResult()
    {
        lock (gate)
        {
            return new AnnotationCaptureResult(AnnotationCaptureStatus.Disabled, message: disabledReason);
        }
    }

    private static void UnregisterSink(SinkEntry entry)
    {
        lock (gate)
        {
            sinkEntries.RemoveAll(existing => ReferenceEquals(existing, entry));
        }
    }

    private sealed class SinkRegistration(SinkEntry entry) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnregisterSink(entry);
        }
    }

    private sealed class SinkEntry(IRuntime? runtime, IAnnotationSink sink)
    {
        internal IRuntime? Runtime { get; } = runtime;

        internal IAnnotationSink Sink { get; } = sink;
    }
}
