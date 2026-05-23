namespace Ansight.Input;

internal interface ITouchCaptureSession : IDisposable
{
    void Start();

    void Stop();
}

internal static class TouchCaptureSupport
{
    public static ITouchCaptureSession CreateSession(TouchCaptureHub touchCaptureHub)
    {
        ArgumentNullException.ThrowIfNull(touchCaptureHub);

        if (!touchCaptureHub.IsEnabled || touchCaptureHub.Options is null)
        {
            return NullTouchCaptureSession.Instance;
        }

#if ANDROID
        return new AndroidTouchCaptureSession(touchCaptureHub.Options, touchCaptureHub.Record);
#elif IOS || MACCATALYST
        return new AppleTouchCaptureSession(touchCaptureHub.Options, touchCaptureHub.Record);
#else
        return NullTouchCaptureSession.Instance;
#endif
    }

    private sealed class NullTouchCaptureSession : ITouchCaptureSession
    {
        public static NullTouchCaptureSession Instance { get; } = new();

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
