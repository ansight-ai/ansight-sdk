namespace Ansight.Tools.Reflection;

public sealed class DescribeTypeTool : ITool
{
    private readonly ReflectionToolsOptions options;

    public DescribeTypeTool(ReflectionToolsOptions? options = null)
    {
        this.options = options ?? ReflectionToolsOptions.Default;
    }

    public string Category => "reflect";

    public ToolScope Scope => ToolScope.Read;

    public string Id => ReflectionToolIds.DescribeType;

    public string Name => "Describe Type";

    public string Description => "Returns metadata about a runtime type without reading live object values.";

    public string Keywords => "reflection type members methods metadata runtime";

    public ToolSchema ArgumentsSchema => ReflectionToolSchemas.DescribeTypeArguments;

    public ToolSchema ResultSchema => ReflectionToolSchemas.DescribeTypeResult;

    public ToolSecurity Security => ReflectionToolSecurityProfiles.DescribeType;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(ReflectionSupport.DescribeType(options, arguments)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "reflect_describe_type_failed"));
        }
    }
}
