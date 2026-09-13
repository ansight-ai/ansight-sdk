namespace Ansight.Tools.Clipboard;

#if ANDROID
using Android.Content;
using Android.OS;
using Ansight.Screenshot;

internal sealed class AndroidClipboardBackend : IClipboardBackend
{
    private static ClipboardManager Manager()
    {
        var activity = AndroidSceneCapture.GetCurrentRoot()?.Activity;
        if (activity?.Window?.DecorView?.HasWindowFocus != true)
            throw new ClipboardException("clipboard_not_focused", "Focus the app window before accessing its clipboard.");
        return activity.GetSystemService(Context.ClipboardService) as ClipboardManager
            ?? throw new ClipboardException("clipboard_unavailable", "The system clipboard service is unavailable.");
    }

    public string? GetText()
    {
        var manager = Manager();
        var clip = manager.PrimaryClip;
        if (clip is null)
        {
            if (manager.HasPrimaryClip) throw new ClipboardException("clipboard_unavailable", "Clipboard contents could not be read.");
            return null;
        }
        return clip.ItemCount > 0 ? clip.GetItemAt(0)?.Text?.ToString() : null;
    }
    public bool HasText()
    {
        var description = Manager().PrimaryClipDescription;
        return description?.HasMimeType("text/plain") == true || description?.HasMimeType("text/html") == true;
    }
    public void SetText(string text) => Manager().PrimaryClip = ClipData.NewPlainText("Ansight", text);
    public void Clear()
    {
        var manager = Manager();
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
            throw new ClipboardException("clipboard_clear_unsupported", "Clearing the clipboard requires Android API 28 or later.");
        manager.ClearPrimaryClip();
    }
}
#endif
