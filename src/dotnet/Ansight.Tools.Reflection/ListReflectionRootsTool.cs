namespace Ansight.Tools.Reflection;

public sealed class ListReflectionRootsTool : ITool
{
    private readonly ReflectionToolsOptions options;

    public ListReflectionRootsTool(ReflectionToolsOptions? options = null)
    {
        this.options = options ?? ReflectionToolsOptions.Default;
    }

    public string Category => "reflect";

    public ToolScope Scope => ToolScope.Read;

    public string Id => ReflectionToolIds.ListRoots;

    public string Name => "List Reflection Roots";

    public string Description => "Lists the registered live object roots available for reflection tools.";

    public string Keywords => "reflection runtime inspect roots objects";

    public ToolSchema ArgumentsSchema => ReflectionToolSchemas.ListRootsArguments;

    public ToolSchema ResultSchema => ReflectionToolSchemas.ListRootsResult;

    public ToolSecurity Security => ReflectionToolSecurityProfiles.ListRoots;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(ReflectionSupport.ListRoots(options)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "reflect_list_roots_failed"));
        }
    }
}
