#if ANDROID
namespace Ansight.Annotations;

using Android.App;
using Ansight.Screenshot;

public static partial class Feedback
{
    /// <summary>
    /// Captures the supplied Android activity and presents the built-in feedback overlay.
    /// </summary>
    /// <remarks>
    /// Native Android apps should use this overload when the current activity cannot be
    /// resolved through .NET MAUI.
    /// </remarks>
    public static Task<AnnotationCaptureResult> PresentAsync(
        Activity activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        AndroidSceneCapture.SetCurrentActivity(activity);
        return PresentWithHostAsync(activity, cancellationToken);
    }
}
#endif
