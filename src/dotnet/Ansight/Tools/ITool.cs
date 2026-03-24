namespace Ansight.Tools;

public interface ITool
{
    string Category { get; }

    ToolScope Scope { get; }

    string Id { get; }

    string Name { get; }

    string Description { get; }

    string Keywords { get; }

    ToolSchema ArgumentsSchema { get; }

    ToolSchema ResultSchema { get; }

    ToolSecurity Security => ToolSecurity.Unspecified;

    ToolDefinition Definition => new(
        Id,
        Name,
        Description,
        Category,
        Scope,
        Keywords,
        ArgumentsSchema,
        ResultSchema)
    {
        Security = Security
    };

    Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments);
}
