using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class JsonToolTests
{
    private static ToolDefinition Definition(ToolPolicy policy = ToolPolicy.Read) => new(
        "scene.select", "Select entity", "Select a scene entity", "scene", policy, "entity",
        ToolSchema.Object(properties: new Dictionary<string, ToolSchema> { ["id"] = ToolSchema.String() }, required: ["id"]),
        ToolSchema.Object(properties: new Dictionary<string, ToolSchema> { ["selected"] = ToolSchema.String() }, required: ["selected"]));

    private static ToolProtocolEnvelope Call(JsonNode args) => new()
    {
        Type = ToolProtocolBridge.CallType, Id = "call-1", SessionId = "session-1",
        Payload = new JsonObject { ["toolId"] = "scene.select", ["arguments"] = args }
    };

    [Fact]
    public async Task FactoryUsesJsonSchemaValidationAndPreservesContext()
    {
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        var tool = JsonTool.Create(Definition(), (invocation, token) =>
        {
            calls++;
            Assert.Equal(cancellation.Token, token);
            Assert.Equal("call-1", invocation.Context.RequestId);
            return Task.FromResult(ToolResult.Success(new JsonObject { ["selected"] = invocation.Arguments["id"]!.DeepClone() }));
        });
        var bridge = new ToolRegistry([tool]).CreateBridge(ToolGuard.ReadOnly);
        var invalid = await bridge.HandleAsync(Call(new JsonObject { ["id"] = 10 }), cancellation.Token);
        Assert.Equal(ToolProtocolBridge.ErrorType, invalid.Type);
        Assert.Equal(0, calls);
        var valid = await bridge.HandleAsync(Call(new JsonObject { ["id"] = "cube" }), cancellation.Token);
        Assert.True(valid.Payload!["success"]!.GetValue<bool>());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FactoryRetainsPolicyAvailabilityAndResultValidation()
    {
        var calls = 0;
        var available = false;
        var tool = JsonTool.Create(Definition(ToolPolicy.Write), (_, _) =>
        {
            calls++;
            return Task.FromResult(ToolResult.Success(new JsonObject()));
        }, _ => ValueTask.FromResult(available ? ToolAvailability.Available : ToolAvailability.Unavailable("scene_loading", "Scene loading")));
        var readOnly = new ToolRegistry([tool]).CreateBridge(ToolGuard.ReadOnly);
        Assert.Equal(ToolProtocolBridge.ErrorType, (await readOnly.HandleAsync(Call(new JsonObject { ["id"] = "cube" }))).Type);
        Assert.Equal(0, calls);
        var bridge = new ToolRegistry([tool]).CreateBridge(ToolGuard.FullAccess);
        Assert.Equal(ToolProtocolBridge.ErrorType, (await bridge.HandleAsync(Call(new JsonObject { ["id"] = "cube" }))).Type);
        Assert.Equal(0, calls);
        available = true;
        Assert.Equal(ToolProtocolBridge.ErrorType, (await bridge.HandleAsync(Call(new JsonObject { ["id"] = "cube" }))).Type);
        Assert.Equal(1, calls); // A malformed handler result is rejected by the existing bridge.
    }

    [Fact]
    public async Task JsonToolInheritsLegacyAdapter()
    {
        ITool tool = JsonTool.Create(Definition(), (invocation, _) => Task.FromResult(
            ToolResult.Success(new JsonObject { ["selected"] = invocation.Arguments["id"]!.DeepClone() })));
        var result = await tool.Execute(new Dictionary<string, string> { ["id"] = "cube" });
        Assert.Equal("cube", result.Payload!["selected"]!.GetValue<string>());
    }
}
