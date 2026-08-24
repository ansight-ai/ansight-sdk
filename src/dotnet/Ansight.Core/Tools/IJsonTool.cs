namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Tool contract that receives protocol arguments as structured JSON instead of flattened strings.
/// </summary>
public interface IJsonTool : ITool
{
    /// <summary>
    /// Executes the tool with validated JSON arguments and request context.
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken);

    async Task<ToolResult> ITool.Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var jsonArguments = new JsonObject();
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, ToolExecutionArgumentNames.RequestId, StringComparison.Ordinal)
                || string.Equals(argument.Key, ToolExecutionArgumentNames.SessionId, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                jsonArguments[argument.Key] = JsonNode.Parse(argument.Value);
            }
            catch
            {
                jsonArguments[argument.Key] = argument.Value;
            }
        }

        arguments.TryGetValue(ToolExecutionArgumentNames.RequestId, out var requestId);
        arguments.TryGetValue(ToolExecutionArgumentNames.SessionId, out var sessionId);
        return await ExecuteAsync(
            new ToolInvocation(
                jsonArguments,
                new ToolInvocationContext(
                    string.IsNullOrWhiteSpace(requestId) ? "legacy" : requestId,
                    sessionId,
                    CallId: null)),
            CancellationToken.None);
    }
}
