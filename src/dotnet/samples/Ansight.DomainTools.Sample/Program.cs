using System.Text.Json.Nodes;
using Ansight.DomainTools.Sample;
using Ansight.Tools;

var scene = new SceneDiagnostics();
var registry = new ToolRegistry(scene.CreateTools());
var readOnly = registry.CreateBridge(ToolGuard.ReadOnly);
var writable = registry.CreateBridge(ToolGuard.ReadWrite);

async Task Check(ToolProtocolBridge bridge, string id, JsonObject args, bool expected)
{
    var response = await bridge.HandleAsync(new ToolProtocolEnvelope
    {
        Type = ToolProtocolBridge.CallType, Id = Guid.NewGuid().ToString("N"),
        Payload = new JsonObject { ["toolId"] = id, ["arguments"] = args }
    });
    var success = response.Type == ToolProtocolBridge.ResultType && response.Payload?["success"]?.GetValue<bool>() == true;
    if (success != expected) throw new Exception(response.Payload?.ToJsonString());
}

await Check(readOnly, "scene.snapshot", new(), true);
await Check(readOnly, "scene.set_visible", new() { ["id"] = "cube", ["visible"] = false }, false);
await Check(writable, "scene.set_visible", new() { ["id"] = "cube", ["visible"] = false }, true);
await Check(writable, "scene.set_visible", new() { ["id"] = "cube", ["visible"] = "false" }, false);
await Check(writable, "scene.set_visible", new() { ["id"] = "missing", ["visible"] = false }, false);
scene.SetReady(false);
await Check(writable, "scene.snapshot", new(), false);
Console.WriteLine("Domain-tool sample: read, write, schema, policy, unknown entity, and availability checks passed.");
