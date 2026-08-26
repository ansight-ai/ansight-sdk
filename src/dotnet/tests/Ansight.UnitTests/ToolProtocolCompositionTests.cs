using System.Text.Json.Nodes;
using Ansight.Native;
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
        Assert.Equal("ansight.tool-catalog.v3", firstPayload["schema"]?.GetValue<string>());
        Assert.Null(tools[0]?["argumentEncoding"]);
        Assert.NotNull(tools[0]?["definitionRevision"]);
        Assert.Equal(1, firstPayload["categories"]?["test"]?.GetValue<int>());
        Assert.Null(firstPayload["manifest"]);
        Assert.Null(firstPayload["catalogHash"]);

        var second = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "catalog-2",
            new JsonObject { ["ifRevision"] = revision }));
        var secondPayload = Assert.IsType<JsonObject>(second.Payload);
        Assert.True(secondPayload["unchanged"]?.GetValue<bool>());
        Assert.Equal(3, secondPayload.Count);
        Assert.Null(secondPayload["tools"]);
        Assert.Equal(revision, secondPayload["revision"]?.GetValue<string>());
    }

    [Fact]
    public async Task Catalog_CompressesLargePayloadAndPreservesConditionalRevision()
    {
        var bridge = new ToolRegistry([
            new RecordingJsonTool(
                "large.catalog",
                description: new string('x', 64 * 1024))
        ]).CreateBridge(ToolGuard.ReadOnly);

        var first = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "large-catalog-1",
            new JsonObject()));
        var encodedPayload = Assert.IsType<JsonObject>(first.Payload);
        Assert.Equal("gzip-base64-json", encodedPayload["$ansightEncoding"]?.GetValue<string>());
        var originalByteCount = encodedPayload["originalByteCount"]!.GetValue<int>();
        var compressedByteCount = encodedPayload["compressedByteCount"]!.GetValue<int>();
        Assert.True(originalByteCount > compressedByteCount);
        Assert.True(
            ToolProtocolPayloadEncoding.TryDecode(first.Payload, out var decodedPayload, out var decodeError),
            decodeError);

        var catalogPayload = Assert.IsType<JsonObject>(decodedPayload);
        Assert.Equal(1, catalogPayload["count"]?.GetValue<int>());
        Assert.Single(Assert.IsType<JsonArray>(catalogPayload["tools"]));
        var revision = catalogPayload["revision"]!.GetValue<string>();

        var second = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "large-catalog-2",
            new JsonObject { ["ifRevision"] = revision }));
        var conditionalPayload = Assert.IsType<JsonObject>(second.Payload);
        Assert.False(conditionalPayload.ContainsKey("$ansightEncoding"));
        Assert.True(conditionalPayload["unchanged"]?.GetValue<bool>());
        Assert.Null(conditionalPayload["tools"]);
        Assert.Equal(revision, conditionalPayload["revision"]?.GetValue<string>());
    }

    [Fact]
    public async Task Catalog_SupportsCompactIndexFocusedDefinitionsAndAvailabilityChanges()
    {
        var routeTool = new RecordingJsonTool("route.open");
        var mapTool = new RecordingJsonTool(
            "map.capture",
            prerequisites: [routeTool.Id]);
        var bridge = new ToolRegistry([routeTool, mapTool]).CreateBridge(ToolGuard.ReadOnly);

        var indexResponse = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "index-1",
            new JsonObject
            {
                ["detail"] = "index",
                ["query"] = "map",
                ["limit"] = 1
            }));
        var index = Assert.IsType<JsonObject>(indexResponse.Payload);
        var indexTools = Assert.IsType<JsonArray>(index["tools"]);
        var indexTool = Assert.IsType<JsonObject>(Assert.Single(indexTools));
        Assert.Equal(mapTool.Id, indexTool["id"]?.GetValue<string>());
        Assert.Null(indexTool["argumentsSchema"]);
        Assert.Equal(routeTool.Id, indexTool["prerequisiteToolIds"]?[0]?.GetValue<string>());

        var definitionsResponse = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "definitions-1",
            new JsonObject
            {
                ["detail"] = "definitions",
                ["ids"] = new JsonArray(mapTool.Id)
            }));
        var definitions = Assert.IsType<JsonObject>(definitionsResponse.Payload);
        var definition = Assert.IsType<JsonObject>(Assert.Single(Assert.IsType<JsonArray>(definitions["tools"])));
        Assert.NotNull(definition["argumentsSchema"]);
        Assert.Null(definition["runtime"]);

        var revision = index["revision"]!.GetValue<string>();
        var availabilityRevision = index["availabilityRevision"]!.GetValue<string>();
        mapTool.Availability = ToolAvailability.Unavailable(
            "map_not_loaded",
            "Load a map before capturing it.");

        var availabilityResponse = await bridge.HandleAsync(Request(
            ToolProtocolBridge.QueryType,
            "index-2",
            new JsonObject
            {
                ["detail"] = "index",
                ["ifRevision"] = revision,
                ["ifAvailabilityRevision"] = availabilityRevision
            }));
        var availabilityPayload = Assert.IsType<JsonObject>(availabilityResponse.Payload);
        Assert.True(availabilityPayload["unchanged"]?.GetValue<bool>());
        Assert.Equal(revision, availabilityPayload["revision"]?.GetValue<string>());
        Assert.NotEqual(
            availabilityRevision,
            availabilityPayload["availabilityRevision"]?.GetValue<string>());
        var changes = Assert.IsType<JsonObject>(availabilityPayload["changes"]);
        Assert.Equal(false, changes[mapTool.Id]?["available"]?.GetValue<bool>());
        Assert.Equal("map_not_loaded", changes[mapTool.Id]?["code"]?.GetValue<string>());
        Assert.Null(changes[routeTool.Id]);
        Assert.NotNull(availabilityPayload["evaluatedAtUtc"]);
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

    [Fact]
    public async Task Call_SerializesDeepVisualTreePayloadWithinProtocolLimit()
    {
        var bridge = new ToolRegistry([new DeepJsonTool("ui.get_visual_tree", depth: 80)])
            .CreateBridge(ToolGuard.ReadOnly);

        var response = await bridge.HandleAsync(Request(
            ToolProtocolBridge.CallType,
            "deep-tree",
            new JsonObject
            {
                ["toolId"] = "ui.get_visual_tree",
                ["arguments"] = new JsonObject()
            }));

        Assert.Equal(ToolProtocolBridge.ResultType, response.Type);
        var json = bridge.SerializeEnvelope(response);
        Assert.Contains("deep-tree.response", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAdapter_ContainsResponseEncodingFailures()
    {
        var bridge = new ToolRegistry([new DeepJsonTool("ui.get_visual_tree", depth: 300)])
            .CreateBridge(ToolGuard.ReadOnly);
        var request = Request(
            ToolProtocolBridge.CallType,
            "too-deep-tree",
            new JsonObject
            {
                ["toolId"] = "ui.get_visual_tree",
                ["arguments"] = new JsonObject()
            });

        var responseJson = NativeToolProtocolAdapter.Handle(
            bridge,
            bridge.SerializeEnvelope(request));

        Assert.NotNull(responseJson);
        Assert.True(bridge.TryParseEnvelope(responseJson, out var response, out var error), error);
        Assert.Equal(ToolProtocolBridge.ErrorType, response?.Type);
        Assert.Equal(
            "tool_protocol_bridge_failed",
            response?.Payload?["code"]?.GetValue<string>());
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
        private readonly string description;

        public RecordingJsonTool(
            string id,
            bool shouldFail = false,
            string? description = null,
            IReadOnlyList<string>? prerequisites = null)
        {
            Id = id;
            this.shouldFail = shouldFail;
            this.description = description ?? "Test JSON tool.";
            PrerequisiteToolIds = prerequisites ?? Array.Empty<string>();
        }

        public int ExecutionCount { get; private set; }
        public string Category => "test";
        public ToolPolicy Policy => ToolPolicy.Read;
        public string Id { get; }
        public string Name => Id;
        public string Description => description;
        public string Keywords => "test";
        public ToolSchema ArgumentsSchema => ToolSchema.Object(additionalProperties: true);
        public ToolSchema ResultSchema => ToolSchema.Object(additionalProperties: true);
        public IReadOnlyList<string> PrerequisiteToolIds { get; }
        public ToolAvailability Availability { get; set; } = ToolAvailability.Available;

        public ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
            => ValueTask.FromResult(Availability);

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

    private sealed class DeepJsonTool : IJsonTool
    {
        private readonly int depth;

        public DeepJsonTool(string id, int depth)
        {
            Id = id;
            this.depth = depth;
        }

        public string Category => "test";
        public ToolPolicy Policy => ToolPolicy.Read;
        public string Id { get; }
        public string Name => Id;
        public string Description => "Returns a deeply nested visual-tree-shaped payload.";
        public string Keywords => "test deep tree";
        public ToolSchema ArgumentsSchema => ToolSchema.Object(additionalProperties: true);
        public ToolSchema ResultSchema => ToolSchema.Object(additionalProperties: true);

        public Task<ToolResult> ExecuteAsync(
            ToolInvocation invocation,
            CancellationToken cancellationToken)
        {
            var root = new JsonObject();
            var current = root;
            for (var index = 0; index < depth; index++)
            {
                var child = new JsonObject();
                current["children"] = new JsonArray(child);
                current = child;
            }

            return Task.FromResult(ToolResult.Success(root));
        }
    }
}
