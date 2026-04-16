namespace Ansight.Tools.Reflection;

public sealed class SetMemberValueTool : ITool
{
    private readonly ReflectionToolsOptions options;

    public SetMemberValueTool(ReflectionToolsOptions? options = null)
    {
        this.options = options ?? ReflectionToolsOptions.Default;
    }

    public string Category => "reflect";

    public ToolScope Scope => ToolScope.Write;

    public string Id => ReflectionToolIds.SetMemberValue;

    public string Name => "Set Member Value";

    public string Description => "Writes a writable field or property reachable from a registered live object root.";

    public string Keywords => "reflection set write property field runtime";

    public ToolSchema ArgumentsSchema => ReflectionToolSchemas.SetMemberValueArguments;

    public ToolSchema ResultSchema => ReflectionToolSchemas.SetMemberValueResult;

    public ToolSecurity Security => ReflectionToolSecurityProfiles.SetMemberValue;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(ReflectionSupport.SetMemberValue(options, arguments)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "reflect_set_member_failed"));
        }
    }
}
