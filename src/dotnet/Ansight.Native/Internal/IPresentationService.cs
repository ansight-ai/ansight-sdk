namespace Ansight;

/// <summary>
/// Handles presentation concerns (sheet + overlay) for the runtime so platform-specific
/// dependencies stay out of the core runtime implementation.
/// </summary>
public interface IPresentationService : IDisposable
{
    bool IsPresentationEnabled { get; }
    bool IsSheetPresented { get; }
    bool IsOverlayPresented { get; }

    void PresentSheet();
    void DismissSheet();

    void PresentOverlay(OverlayPosition position);
    void DismissOverlay();
}
