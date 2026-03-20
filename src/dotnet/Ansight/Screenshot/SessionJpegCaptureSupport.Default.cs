#if !ANDROID && !IOS && !MACCATALYST
namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ISessionJpegCaptureSurface?>(null);
    }

    private static partial SessionJpegFrame? EncodeSurfaceCore(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options)
    {
        return null;
    }
}
#endif
