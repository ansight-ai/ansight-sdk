namespace Ansight.Tools.Reflection;

public sealed class InvokeMethodTool : ITool
{
    private readonly ReflectionToolsOptions options;

    public InvokeMethodTool(ReflectionToolsOptions? options = null)
    {
        this.options = options ?? ReflectionToolsOptions.Default;
    }

    public string Category => "reflect";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => ReflectionToolIds.InvokeMethod;

    public string Name => "Invoke Method";

    public string Description => "Invokes an instance method reachable from a registered live object root.";

    public string Keywords => "reflection invoke method runtime";

    public ToolSchema ArgumentsSchema => ReflectionToolSchemas.InvokeMethodArguments;

    public ToolSchema ResultSchema => ReflectionToolSchemas.InvokeMethodResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            return Task.FromResult(ToolResult.Success(ReflectionSupport.InvokeMethod(options, arguments)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "reflect_invoke_failed"));
        }
    }
}
