using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class ToolAvailabilityTests
{
    [Fact]
    public async Task CatalogAndCall_ReportRuntimePreconditions()
    {
        var tool = new UnavailableTool();
        var bridge = new ToolRegistry([tool]).CreateBridge(ToolGuard.ReadOnly);

        var catalog = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.QueryType,
            Id = "query_1",
            SessionId = "session_1",
            Payload = new JsonObject()
        });

        var catalogPayload = Assert.IsType<JsonObject>(catalog.Payload);
        var tools = Assert.IsType<JsonArray>(catalogPayload["tools"]);
        var entry = Assert.IsType<JsonObject>(Assert.Single(tools));
        var runtime = Assert.IsType<JsonObject>(entry["runtime"]);
        Assert.False(entry["executable"]?.GetValue<bool>());
        Assert.Equal("screen_not_registered", runtime["reasonCode"]?.GetValue<string>());
        Assert.Equal("MapWorkScreen registered", runtime["requiredState"]?.GetValue<string>());

        var call = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.CallType,
            Id = "call_1",
            SessionId = "session_1",
            Payload = new JsonObject
            {
                ["toolId"] = tool.Id,
                ["arguments"] = new JsonObject()
            }
        });

        Assert.Equal(ToolProtocolBridge.ErrorType, call.Type);
        var error = Assert.IsType<JsonObject>(call.Payload);
        Assert.Equal("screen_not_registered", error["code"]?.GetValue<string>());
        Assert.True(error["retryable"]?.GetValue<bool>());
        Assert.False(tool.ExecuteCalled);
    }

    private sealed class UnavailableTool : ITool
    {
        public string Category => "test";

        public ToolPolicy Policy => ToolPolicy.Read;

        public string Id => "mapwork.open";

        public string Name => "Open Map Work";

        public string Description => "Opens the active map work screen.";

        public string Keywords => "map work";

        public ToolSchema ArgumentsSchema => ToolSchema.Object();

        public ToolSchema ResultSchema => ToolSchema.Object();

        public bool ExecuteCalled { get; private set; }

        public ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
            => ValueTask.FromResult(ToolAvailability.Unavailable(
                "screen_not_registered",
                "No active MapWorkScreen is registered.",
                requiredState: "MapWorkScreen registered",
                remediation: "Navigate to the map screen and retry."));

        public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
        {
            ExecuteCalled = true;
            return Task.FromResult(ToolResult.Success());
        }
    }
}
