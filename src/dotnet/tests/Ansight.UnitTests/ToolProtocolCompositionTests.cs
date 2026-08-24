using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class ToolProtocolCompositionTests
{
    [Fact]
    public async Task Catalog_UsesStableRevisionAndSupportsConditionalQuery()
    {
        var bridge = new ToolRegistry([new RecordingJsonTool("json.echo")])
            .CreateBridge(ToolGuard.ReadOnly);

        var first = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "catalog-1",
            new JsonObject()));
        var firstPayload = Assert.IsType<JsonObject>(first.Payload);
        var revision = firstPayload["revision"]!.GetValue<string>();
        var tools = Assert.IsType<JsonArray>(firstPayload["tools"]);
        Assert.Equal("ansight.tool-catalog.v2", firstPayload["schema"]?.GetValue<string>());
        Assert.Equal("json", tools[0]?["argumentEncoding"]?.GetValue<string>());

        var second = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "catalog-2",
            new JsonObject { ["ifRevision"] = revision }));
        var secondPayload = Assert.IsType<JsonObject>(second.Payload);
        Assert.True(secondPayload["unchanged"]?.GetValue<bool>());
        Assert.Empty(Assert.IsType<JsonArray>(secondPayload["tools"]));
        Assert.Equal(revision, secondPayload["revision"]?.GetValue<string>());
    }

    [Fact]
    public async Task Batch_PreservesOrderAndStopsAfterFailure()
    {
        var firstTool = new RecordingJsonTool("step.first");
        var failingTool = new RecordingJsonTool("step.fail", shouldFail: true);
        var skippedTool = new RecordingJsonTool("step.skipped");
        var bridge = new ToolRegistry([firstTool, failingTool, skippedTool])
            .CreateBridge(ToolGuard.ReadOnly);

        var response = await bridge.HandleAsync(Request(
            ToolProtocolBridge.BatchType,
            "batch-1",
            new JsonObject
            {
                ["calls"] = new JsonArray
                {
                    Call("first", firstTool.Id),
                    Call("fail", failingTool.Id),
                    Call("skipped", skippedTool.Id)
                }
            }));

        Assert.Equal(ToolProtocolBridge.BatchResultType, response.Type);
        var payload = Assert.IsType<JsonObject>(response.Payload);
        var results = Assert.IsType<JsonArray>(payload["results"]);
        Assert.Equal(2, results.Count);
        Assert.Equal("first", results[0]?["callId"]?.GetValue<string>());
        Assert.Equal("fail", results[1]?["callId"]?.GetValue<string>());
        Assert.True(payload["stoppedEarly"]?.GetValue<bool>());
        Assert.Equal(0, skippedTool.ExecutionCount);
    }

    [Fact]
    public async Task Call_CapturesRequestedEvidenceInTheSameResponse()
    {
        var actionTool = new RecordingJsonTool("ui.action");
        var treeTool = new RecordingJsonTool("ui.get_visual_tree");
        var bridge = new ToolRegistry([actionTool, treeTool])
            .CreateBridge(ToolGuard.ReadOnly);

        var response = await bridge.HandleAsync(Request(
            ToolProtocolBridge.CallType,
            "call-with-evidence",
            new JsonObject
            {
                ["toolId"] = actionTool.Id,
                ["arguments"] = new JsonObject { ["value"] = "save" },
                ["after"] = new JsonObject
                {
                    ["include"] = new JsonArray("visualTree")
                }
            }));

        var payload = Assert.IsType<JsonObject>(response.Payload);
        var evidence = Assert.IsType<JsonObject>(payload["evidence"]);
        Assert.True(evidence["visualTree"]?["success"]?.GetValue<bool>());
        Assert.Equal(1, treeTool.ExecutionCount);
    }

    private static ToolProtocolEnvelope Request(string type, string id, JsonObject payload)
        => new()
        {
            Type = type,
            Id = id,
            Payload = payload
        };

    private static JsonObject Call(string callId, string toolId)
        => new()
        {
            ["callId"] = callId,
            ["toolId"] = toolId,
            ["arguments"] = new JsonObject()
        };

    private sealed class RecordingJsonTool : IJsonTool
    {
        private readonly bool shouldFail;

        public RecordingJsonTool(string id, bool shouldFail = false)
        {
            Id = id;
            this.shouldFail = shouldFail;
        }

        public int ExecutionCount { get; private set; }
        public string Category => "test";
        public ToolPolicy Policy => ToolPolicy.Read;
        public string Id { get; }
        public string Name => Id;
        public string Description => "Test JSON tool.";
        public string Keywords => "test";
        public ToolSchema ArgumentsSchema => ToolSchema.Object(additionalProperties: true);
        public ToolSchema ResultSchema => ToolSchema.Object(additionalProperties: true);

        public Task<ToolResult> ExecuteAsync(
            ToolInvocation invocation,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(shouldFail
                ? ToolResult.Failure("Expected failure.", "expected_failure")
                : ToolResult.Success(new JsonObject
                {
                    ["execution"] = ExecutionCount,
                    ["arguments"] = invocation.Arguments.DeepClone()
                }));
        }
    }
}
