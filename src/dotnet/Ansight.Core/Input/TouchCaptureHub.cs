namespace Ansight.Input;

internal sealed class TouchCaptureHub
{
    private readonly TouchCaptureOptions? options;

    public TouchCaptureHub(TouchCaptureOptions? options)
    {
        this.options = options?.Clone();
    }

    public bool IsEnabled => options is not null;

    internal TouchCaptureOptions? Options => options;

    public event EventHandler<TouchCapturedEventArgs>? TouchCaptured;

    public void Record(CapturedTouch touch)
    {
        if (!IsEnabled)
        {
            return;
        }

        TouchCaptured?.Invoke(this, new TouchCapturedEventArgs(touch));
    }
}
