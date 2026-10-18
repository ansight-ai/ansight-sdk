using System.Text.Json.Nodes;
using Ansight.Artifacts;
using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.DomainTools.Sample;

// This sample uses a lock because its model is plain data. A real engine should
// provide an adapter that dispatches these operations to its scene/update thread.
public sealed class SceneDiagnostics
{
    private readonly Lock gate = new();
    private bool ready = true;
    private bool cubeVisible = true;

    public IReadOnlyList<ITool> CreateTools()
    {
        var snapshotSchema = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String(), ["visible"] = ToolSchema.Boolean()
        }, required: ["id", "visible"]);
        return [
            JsonTool.Create(new ToolDefinition("scene.snapshot", "Scene snapshot", "Read the current cube state.",
                "scene", ToolPolicy.Read, "scene cube", ToolSchema.Object(), snapshotSchema),
                (_, _) => Task.FromResult(ToolResult.Success(Snapshot())), Availability),
            JsonTool.Create(new ToolDefinition("scene.set_visible", "Set visibility", "Set one entity's visibility.",
                "scene", ToolPolicy.Write, "scene visibility",
                ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
                {
                    ["id"] = ToolSchema.String(), ["visible"] = ToolSchema.Boolean()
                }, required: ["id", "visible"]), snapshotSchema),
                (invocation, _) =>
                {
                    lock (gate)
                    {
                        if (!ready) return Task.FromResult(ToolResult.Failure("Scene is loading.", errorCode: "scene_not_ready"));
                        if (invocation.Arguments["id"]!.GetValue<string>() != "cube")
                            return Task.FromResult(ToolResult.Failure("Unknown entity.", errorCode: "entity_not_found"));
                        cubeVisible = invocation.Arguments["visible"]!.GetValue<bool>();
                        return Task.FromResult(ToolResult.Success(Snapshot()));
                    }
                }, Availability)
        ];
    }

    public void SetReady(bool value) { lock (gate) ready = value; }

    private ValueTask<ToolAvailability> Availability(ToolAvailabilityContext context)
    {
        lock (gate) return ValueTask.FromResult(ready ? ToolAvailability.Available :
            ToolAvailability.Unavailable("scene_not_ready", "Scene is loading.", remediation: "Wait for the scene to load."));
    }

    public JsonObject Snapshot()
    {
        lock (gate) return new JsonObject { ["id"] = "cube", ["visible"] = cubeVisible };
    }

    // Call from an app event after a live Ansight connection is established.
    public Task<OperationResult> PublishSnapshotAsync(CancellationToken cancellationToken = default)
        => ArtifactPublisher.PublishAsync(new ArtifactResult(
            new ArtifactMetadata("snapshot", "scene", "Scene snapshot", "report", "application/json", "scene.json"),
            ArtifactPayload.FromText(Snapshot().ToJsonString())), cancellationToken);
}
