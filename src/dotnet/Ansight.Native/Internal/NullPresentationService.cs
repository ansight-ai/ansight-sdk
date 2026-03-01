namespace Ansight;

/// <summary>
/// No-op presentation handler used when a platform presenter has not been registered.
/// </summary>
internal sealed class NullPresentationService : IPresentationService
{
    public bool IsPresentationEnabled => false;
    public bool IsSheetPresented => false;
    public bool IsOverlayPresented => false;

    public void PresentSheet() { }
    public void DismissSheet() { }
    public void PresentOverlay(OverlayPosition position) { }
    public void DismissOverlay() { }
    public void Dispose() { }
}
