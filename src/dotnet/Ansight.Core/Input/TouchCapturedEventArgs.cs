namespace Ansight.Input;

internal sealed class TouchCapturedEventArgs : EventArgs
{
    public TouchCapturedEventArgs(CapturedTouch touch)
    {
        Touch = touch ?? throw new ArgumentNullException(nameof(touch));
    }

    public CapturedTouch Touch { get; }
}
