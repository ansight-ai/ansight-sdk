namespace Ansight.Tools.Clipboard;

public sealed class SetTextClipboardTool : ClipboardTool
{
    public SetTextClipboardTool() : this(null) { }
    internal SetTextClipboardTool(IClipboardBackend? backend)
        : base(ClipboardToolIds.SetText, "SetText Clipboard", "Replaces system clipboard contents with plain text (at most 65536 UTF-8 bytes).", ToolPolicy.Write, backend) { }
}
