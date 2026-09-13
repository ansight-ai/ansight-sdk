namespace Ansight.Tools.Clipboard;

public sealed class ClearClipboardTool : ClipboardTool
{
    public ClearClipboardTool() : this(null) { }
    internal ClearClipboardTool(IClipboardBackend? backend)
        : base(ClipboardToolIds.Clear, "Clear Clipboard", "Clears all contents of the system clipboard.", ToolPolicy.Write, backend) { }
}
