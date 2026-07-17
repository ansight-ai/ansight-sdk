namespace Ansight.Annotations;

internal delegate Task<AnnotationOverlayResult> AnnotationOverlayPresentation(
    AnnotationScreenshotSnapshot? screenshot,
    object? overlayHost,
    CancellationToken cancellationToken);

internal static class AnnotationOverlayPresenter
{
    internal static Task<AnnotationOverlayResult> PresentAsync(
        AnnotationScreenshotSnapshot? screenshot,
        object? overlayHost,
        CancellationToken cancellationToken)
    {
#if ANDROID
        return AndroidAnnotationOverlayPresenter.PresentAsync(screenshot, overlayHost as Android.App.Activity, cancellationToken);
#elif IOS || MACCATALYST
        return AppleAnnotationOverlayPresenter.PresentAsync(screenshot, cancellationToken);
#else
        return Task.FromResult(AnnotationOverlayResult.Unavailable(
            "The built-in annotation overlay is available on Android, iOS, and Mac Catalyst only."));
#endif
    }
}

internal sealed record AnnotationOverlayResult(
    bool IsSubmitted,
    bool IsCancelled,
    AnnotationCaptureRequest? Request,
    string? Message)
{
    internal static AnnotationOverlayResult Submitted(AnnotationCaptureRequest request) => new(true, false, request, null);

    internal static AnnotationOverlayResult Cancelled() => new(false, true, null, null);

    internal static AnnotationOverlayResult Unavailable(string message) => new(false, false, null, message);
}
