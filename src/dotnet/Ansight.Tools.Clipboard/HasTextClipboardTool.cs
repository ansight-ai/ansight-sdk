namespace Ansight.Tools.Clipboard;

public sealed class HasTextClipboardTool : ClipboardTool
{
    public HasTextClipboardTool() : this(null) { }
    internal HasTextClipboardTool(IClipboardBackend? backend)
        : base(ClipboardToolIds.HasText, "HasText Clipboard", "Checks for text on the system clipboard.", ToolPolicy.Read, backend) { }
}
