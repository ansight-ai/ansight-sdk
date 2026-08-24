namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

internal static class VisualTreeToolArguments
{
    internal static Dictionary<string, string> Flatten(ToolInvocation invocation)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in invocation.Arguments)
        {
            if (property.Value is null)
            {
                continue;
            }

            arguments[property.Key] = property.Value is JsonValue value
                ? value.ToString()
                : property.Value.ToJsonString();
        }

        arguments[ToolExecutionArgumentNames.RequestId] = invocation.Context.RequestId;
        if (!string.IsNullOrWhiteSpace(invocation.Context.SessionId))
        {
            arguments[ToolExecutionArgumentNames.SessionId] = invocation.Context.SessionId!;
        }

        return arguments;
    }
}
