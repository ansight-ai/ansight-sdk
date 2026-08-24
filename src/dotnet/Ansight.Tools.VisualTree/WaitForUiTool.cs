namespace Ansight.Tools.VisualTree;

using System.Diagnostics;
using System.Text.Json.Nodes;

/// <summary>
/// Waits for a provider-neutral UI query condition.
/// </summary>
public sealed class WaitForUiConditionTool : IJsonTool
{
    public string Category => "ui";
    public ToolScope Scope => ToolScope.Read;
    public string Id => VisualTreeToolIds.Wait;
    public string Name => "Wait For UI";
    public string Description => "Polls generic UI snapshots until matching nodes exist, disappear, become visible, or become enabled.";
    public string Keywords => "ui wait poll condition exists visible enabled gone";
    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.WaitArguments;
    public ToolSchema ResultSchema => VisualTreeToolSchemas.WaitResult;
    public ToolSecurity Security => VisualTreeToolSecurityProfiles.Wait;

    public async Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var condition = invocation.Arguments["condition"]!.GetValue<string>();
        var timeoutMilliseconds = Math.Clamp(
            invocation.Arguments["timeoutMilliseconds"]?.GetValue<int>() ?? 5_000,
            1,
            60_000);
        var pollMilliseconds = Math.Clamp(
            invocation.Arguments["pollMilliseconds"]?.GetValue<int>() ?? 100,
            10,
            5_000);
        var stopwatch = Stopwatch.StartNew();
        JsonObject? lastQuery = null;

        while (stopwatch.ElapsedMilliseconds <= timeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryArguments = (JsonObject)invocation.Arguments.DeepClone();
            queryArguments.Remove("condition");
            queryArguments.Remove("timeoutMilliseconds");
            queryArguments.Remove("pollMilliseconds");
            queryArguments.Remove("snapshotId");
            if (condition == "visible")
            {
                queryArguments["visible"] = true;
            }
            else if (condition == "enabled")
            {
                queryArguments["enabled"] = true;
            }
            var result = await new QueryNodesTool().ExecuteAsync(
                new ToolInvocation(queryArguments, invocation.Context),
                cancellationToken);
            if (!result.IsSuccess || result.Payload is not JsonObject query)
            {
                return result;
            }

            lastQuery = query;
            var count = query["count"]!.GetValue<int>();
            var matched = condition switch
            {
                "notExists" => count == 0,
                "exists" or "visible" or "enabled" => count > 0,
                _ => false
            };
            if (matched)
            {
                return ToolResult.Success(new JsonObject
                {
                    ["condition"] = condition,
                    ["matched"] = true,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds,
                    ["query"] = query
                });
            }

            await Task.Delay(pollMilliseconds, cancellationToken);
        }

        return ToolResult.Failure(
            $"Timed out after {timeoutMilliseconds}ms waiting for UI condition '{condition}'.",
            errorCode: "ui_wait_timeout",
            payload: new JsonObject
            {
                ["condition"] = condition,
                ["matched"] = false,
                ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds,
                ["lastQuery"] = lastQuery
            });
    }
}
