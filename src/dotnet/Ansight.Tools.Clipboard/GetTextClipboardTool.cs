namespace Ansight.Tools.Clipboard;

public sealed class GetTextClipboardTool : ClipboardTool
{
    public GetTextClipboardTool() : this(null) { }
    internal GetTextClipboardTool(IClipboardBackend? backend)
        : base(ClipboardToolIds.GetText, "GetText Clipboard", "Reads plain text from the system clipboard. Platform paste permission may be required.", ToolPolicy.Read, backend) { }
}
