namespace Ansight.Tools.Clipboard;

public static class ClipboardOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithClipboardTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddTools([new GetTextClipboardTool(), new HasTextClipboardTool(), new SetTextClipboardTool(), new ClearClipboardTool()]);
    }
}
