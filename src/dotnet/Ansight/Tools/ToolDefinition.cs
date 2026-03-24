namespace Ansight.Tools;

public sealed record ToolDefinition(
    string Id,
    string Name,
    string Description,
    string Category,
    ToolScope Scope,
    string Keywords,
    ToolSchema ArgumentsSchema,
    ToolSchema ResultSchema)
{
    public ToolSecurity Security { get; init; } = ToolSecurity.Unspecified;
}
