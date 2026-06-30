#if !ANDROID && !IOS && !MACCATALYST
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ISessionJpegCaptureSurface?>(null);
    }

    private static partial Task<OperationResult> SendSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(OperationResult.FromFailure("Session JPEG capture is unavailable on this platform."));
    }

    private static partial Task<SessionJpegFrame?> EncodeSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<SessionJpegFrame?>(null);
    }
}
#endif
