namespace Ansight.Tools.Clipboard;

#if IOS || MACCATALYST
using UIKit;

internal sealed class AppleClipboardBackend : IClipboardBackend
{
    private static UIPasteboard Pasteboard()
    {
        if (UIApplication.SharedApplication.ApplicationState != UIApplicationState.Active)
            throw new ClipboardException("clipboard_not_focused", "Activate the app before accessing its clipboard.");
        return UIPasteboard.General;
    }

    public string? GetText()
    {
        var board = Pasteboard();
        if (!board.HasStrings) return null;
        return board.String ?? throw new ClipboardException("clipboard_unavailable", "Clipboard text is unavailable; platform paste permission may be required.");
    }
    public bool HasText() => Pasteboard().HasStrings;
    public void SetText(string text) => Pasteboard().String = text;
    public void Clear() => Pasteboard().Items = [];
}
#endif
