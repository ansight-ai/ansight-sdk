namespace Ansight.Tools.Clipboard;

internal static class ClipboardToolSchemas
{
    internal static ToolSchema Arguments(string id) => id == ClipboardToolIds.SetText
        ? ToolSchema.Object(properties: new Dictionary<string, ToolSchema> { ["text"] = ToolSchema.String("Exact plain text, at most 65536 UTF-8 bytes. Empty text is allowed.") }, required: ["text"])
        : ToolSchema.Object();

    internal static ToolSchema Result(string id)
    {
        var properties = new Dictionary<string, ToolSchema>();
        if (id == ClipboardToolIds.GetText) properties["text"] = ToolSchema.String("Exact text, or null when no plain text is available.", nullable: true);
        var field = id switch { ClipboardToolIds.SetText => "updated", ClipboardToolIds.Clear => "cleared", _ => "hasText" };
        properties[field] = ToolSchema.Boolean();
        return ToolSchema.Object(properties: properties, required: properties.Keys.ToArray());
    }
}
