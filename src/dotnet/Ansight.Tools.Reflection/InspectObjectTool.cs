namespace Ansight.Tools.Reflection;

public sealed class InspectObjectTool : ITool
{
    private readonly ReflectionToolsOptions options;

    public InspectObjectTool(ReflectionToolsOptions? options = null)
    {
        this.options = options ?? ReflectionToolsOptions.Default;
    }

    public string Category => "reflect";

    public ToolScope Scope => ToolScope.Read;

    public string Id => ReflectionToolIds.InspectObject;

    public string Name => "Inspect Object";

    public string Description => "Inspects a registered live object and returns a stateless expandable snapshot.";

    public string Keywords => "reflection inspect object runtime properties fields methods";

    public ToolSchema ArgumentsSchema => ReflectionToolSchemas.InspectObjectArguments;

    public ToolSchema ResultSchema => ReflectionToolSchemas.InspectObjectResult;

    public ToolSecurity Security => ReflectionToolSecurityProfiles.InspectObject;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(ReflectionSupport.InspectObject(options, arguments)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "reflect_inspect_failed"));
        }
    }
}
