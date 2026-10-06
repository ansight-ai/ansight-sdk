namespace Ansight.Annotations;

/// <summary>Compatibility entry point for annotation capture.</summary>
[Obsolete("Use Annotate.PresentAsync, Annotate.CaptureAsync, or Annotate.RegisterSink instead.")]
public static class Feedback
{
    public static bool IsEnabled => Annotate.IsEnabled;

    public static Task<AnnotationCaptureResult> PresentAsync(CancellationToken cancellationToken = default)
        => Annotate.PresentAsync(cancellationToken);

#if ANDROID
    public static Task<AnnotationCaptureResult> PresentAsync(
        Android.App.Activity activity,
        CancellationToken cancellationToken = default)
        => Annotate.PresentAsync(activity, cancellationToken);
#endif

    public static Task<AnnotationCaptureResult> CaptureAsync(
        AnnotationCaptureRequest request,
        CancellationToken cancellationToken = default)
        => Annotate.CaptureAsync(request, cancellationToken);

    public static IDisposable RegisterSink(IAnnotationSink sink)
        => Annotate.RegisterSink(sink);
}
