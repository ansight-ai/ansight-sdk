namespace Ansight.Input;

internal sealed class TouchCaptureHub
{
    private readonly TouchCaptureOptions? options;
    private volatile bool runtimeCaptureEnabled = true;
    private Func<bool>? runtimeCaptureGuard;
    private int guardExceptionLogged;

    public TouchCaptureHub(TouchCaptureOptions? options)
    {
        this.options = options?.Clone();
    }

    public bool IsEnabled => options is not null;

    public bool IsRuntimeCaptureEnabled => IsEnabled && runtimeCaptureEnabled;

    internal TouchCaptureOptions? Options => options;

    public event EventHandler<TouchCapturedEventArgs>? TouchCaptured;

    internal event EventHandler? RuntimeCaptureInterrupted;

    public void EnableRuntimeCapture()
    {
        runtimeCaptureEnabled = true;
    }

    public void DisableRuntimeCapture()
    {
        runtimeCaptureEnabled = false;
        RuntimeCaptureInterrupted?.Invoke(this, EventArgs.Empty);
    }

    public void SetRuntimeCaptureGuard(Func<bool>? guard)
    {
        Volatile.Write(ref runtimeCaptureGuard, guard);
        Interlocked.Exchange(ref guardExceptionLogged, 0);
    }

    public void Record(CapturedTouch touch)
    {
        if (!ShouldCapture())
        {
            RuntimeCaptureInterrupted?.Invoke(this, EventArgs.Empty);
            return;
        }

        TouchCaptured?.Invoke(this, new TouchCapturedEventArgs(touch));
    }

    private bool ShouldCapture()
    {
        if (!IsEnabled || !runtimeCaptureEnabled)
        {
            return false;
        }

        var guard = Volatile.Read(ref runtimeCaptureGuard);
        if (guard is null)
        {
            return true;
        }

        try
        {
            return guard();
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref guardExceptionLogged, 1) == 0)
            {
                Logger.Warning($"Touch capture guard suppressed capture after throwing: {ex.Message}");
            }

            return false;
        }
    }
}
