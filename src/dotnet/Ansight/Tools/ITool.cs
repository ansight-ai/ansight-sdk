namespace Ansight.Tools;

public interface ITool
{
    string Category { get; }
    
    string Id { get; }
    
    string Name { get; }
    
    string Description { get; }
    
    string Keywords { get; }

    Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments);
}
