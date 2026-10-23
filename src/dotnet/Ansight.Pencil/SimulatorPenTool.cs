using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.Pencil;

/// <summary>Routes Simulator pen commands through the normal managed tool guard to the Apple framework.</summary>
internal sealed class SimulatorPenTool(Func<string, string> executeNative) : IJsonTool
{
    public string Id => "ui.simulate_pen";
    public string Name => "Simulator pen properties";
    public string Description => "Simulator only. Prepare an explicit drawing target and optional named idle hover recognizer, arm transport-stamped HID contacts, deliver scoped hover, or clear this run. Expires after 35 seconds.";
    public string Category => "ui";
    public ToolPolicy Policy => ToolPolicy.Write;
    public string Keywords => "pencil stylus simulator simulation";

    private static readonly ToolSchema sampleSchema = ToolSchema.Object(
        properties: new Dictionary<string, ToolSchema>
        {
            ["phase"] = ToolSchema.String(),
            ["x"] = ToolSchema.Number(),
            ["y"] = ToolSchema.Number(),
            ["pressure"] = ToolSchema.Number(),
            ["altitudeRadians"] = ToolSchema.Number(),
            ["azimuthRadians"] = ToolSchema.Number(),
            ["rollRadians"] = ToolSchema.Number(),
            ["distance"] = ToolSchema.Number(),
            ["eventTimestamp"] = ToolSchema.Number()
        }, required: ["phase", "x", "y"]);

    public ToolSchema ArgumentsSchema => ToolSchema.Object(
        properties: new Dictionary<string, ToolSchema>
        {
            ["action"] = ToolSchema.String(enumValues: ["prepare", "arm", "hover", "finish"]),
            ["runId"] = ToolSchema.String(),
            ["target"] = ToolSchema.String(),
            ["hoverRecognizer"] = ToolSchema.String(),
            ["samples"] = ToolSchema.Array(sampleSchema),
            ["sample"] = sampleSchema
        }, required: ["action", "runId"]);

    public ToolSchema ResultSchema => ToolSchema.Object(additionalProperties: true);

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = JsonNode.Parse(executeNative(invocation.Arguments.ToJsonString()))
                ?? throw new InvalidOperationException("The native pen response was empty.");
            var payload = response["result"]?.DeepClone();
            var message = response["message"]?.GetValue<string>();
            return Task.FromResult(response["success"]?.GetValue<bool>() == true
                ? ToolResult.Success(payload, message)
                : ToolResult.Failure(message ?? "Native pen simulation failed.",
                    response["errorCode"]?.GetValue<string>() ?? "pen_native_failed", payload));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, "pen_native_failed"));
        }
    }
}
