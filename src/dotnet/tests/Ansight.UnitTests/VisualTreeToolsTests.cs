using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Tools.VisualTree;

namespace Ansight.UnitTests;

public sealed class VisualTreeToolsTests
{
    [Fact]
    public void WithVisualTreeTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithVisualTreeTools()
            .Build();

        Assert.Equal(
            [
                VisualTreeToolIds.GetVisualTree,
                VisualTreeToolIds.GetScreenshot,
                VisualTreeToolIds.InspectNode,
                VisualTreeToolIds.QueryNodes,
                VisualTreeToolIds.PerformAction,
                VisualTreeToolIds.Wait,
                VisualTreeToolIds.ShowOverlay,
                VisualTreeToolIds.GetOverlay,
                VisualTreeToolIds.QueryOverlays,
                VisualTreeToolIds.UpdateOverlay,
                VisualTreeToolIds.RemoveOverlay,
                VisualTreeToolIds.ClearOverlays
            ],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task GetVisualTreeTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new GetVisualTreeTool().Execute(new Dictionary<string, string>());

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public void GetVisualTreeTool_ResultSchema_DeclaresCompactTypedNodes()
    {
        var resultSchema = new GetVisualTreeTool().ResultSchema;
        var rootSchema = resultSchema.Properties["root"];
        var visualSchema = rootSchema.Properties["visual"];
        var zSchema = rootSchema.Properties["z"];

        Assert.Contains("types", resultSchema.Required);
        Assert.Contains("typeId", rootSchema.Required);
        Assert.DoesNotContain("type", rootSchema.Properties);
        Assert.DoesNotContain("kind", rootSchema.Properties);
        Assert.DoesNotContain("styleId", rootSchema.Properties);
        Assert.Contains("visual", rootSchema.Required);
        Assert.DoesNotContain("z", rootSchema.Required);
        Assert.Equal(
            ["foreground", "background", "opacity", "text", "value"],
            visualSchema.Properties.Keys);
        Assert.Equal(["opacity"], visualSchema.Required);
        Assert.True(zSchema.Nullable);
    }

    [Fact]
    public async Task GetScreenshotTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new GetScreenshotTool().Execute(new Dictionary<string, string>());

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task InspectNodeTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new InspectNodeTool().Execute(new Dictionary<string, string>
        {
            ["nodeId"] = "root"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task ShowOverlayTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new ShowOverlayTool().Execute(new Dictionary<string, string>
        {
            ["x"] = "0",
            ["y"] = "0",
            ["width"] = "100",
            ["height"] = "100"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task GetOverlayTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new GetOverlayTool().Execute(new Dictionary<string, string>
        {
            ["overlayId"] = "overlay-1"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task QueryOverlaysTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new QueryOverlaysTool().Execute(new Dictionary<string, string>());

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task RemoveOverlayTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new RemoveOverlayTool().Execute(new Dictionary<string, string>
        {
            ["overlayId"] = "overlay-1"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateOverlayTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new UpdateOverlayTool().Execute(new Dictionary<string, string>
        {
            ["overlayId"] = "overlay-1",
            ["strokeColor"] = "blue"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task ClearOverlaysTool_Execute_ReturnsPlatformUnsupportedOnHost()
    {
        var result = await new ClearOverlaysTool().Execute(new Dictionary<string, string>());

        Assert.False(result.IsSuccess);
        Assert.Equal("visual_tree_platform_unsupported", result.ErrorCode);
    }

    [Fact]
    public async Task GenericAction_RejectsReferenceAfterNewerSourceSnapshot()
    {
        var source = $"test-{Guid.NewGuid():N}";
        var provider = new TestInteractionProvider(source);
        using var registration = VisualTreeProviderRegistry.Register(provider);
        var queryTool = new QueryNodesTool();
        var firstQuery = await queryTool.ExecuteAsync(
            Invocation(new JsonObject { ["source"] = source, ["automationId"] = "save" }),
            CancellationToken.None);
        var firstPayload = Assert.IsType<JsonObject>(firstQuery.Payload);
        var firstMatch = Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(firstPayload["matches"])[0]);
        var reference = Assert.IsType<JsonObject>(firstMatch["reference"]);

        _ = await queryTool.ExecuteAsync(
            Invocation(new JsonObject { ["source"] = source }),
            CancellationToken.None);

        var actionResult = await new PerformActionTool().ExecuteAsync(
            Invocation(new JsonObject
            {
                ["reference"] = reference.DeepClone(),
                ["action"] = "tap"
            }),
            CancellationToken.None);

        Assert.False(actionResult.IsSuccess);
        Assert.Equal("stale_node_reference", actionResult.ErrorCode);
        Assert.Equal(0, provider.ActionCount);
    }

    [Fact]
    public async Task InspectNode_AcceptsSnapshotReferenceObjectEncoding()
    {
        var source = $"test-{Guid.NewGuid():N}";
        using var registration = VisualTreeProviderRegistry.Register(new TestInteractionProvider(source));
        var queryResult = await new QueryNodesTool().ExecuteAsync(
            Invocation(new JsonObject { ["source"] = source, ["nodeId"] = "root" }),
            CancellationToken.None);
        var queryPayload = Assert.IsType<JsonObject>(queryResult.Payload);
        var match = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(queryPayload["matches"])[0]);
        var reference = Assert.IsType<JsonObject>(match["reference"]);

        var inspectResult = await new InspectNodeTool().Execute(new Dictionary<string, string>
        {
            ["reference"] = reference.ToJsonString()
        });

        Assert.True(inspectResult.IsSuccess);
        Assert.Equal(reference["snapshotId"]!.GetValue<string>(), inspectResult.Payload!["snapshotId"]!.GetValue<string>());
    }

    private static ToolInvocation Invocation(JsonObject arguments)
        => new(arguments, new ToolInvocationContext("test-request", "test-session", null));

    private sealed class TestInteractionProvider : IVisualTreeProvider, IVisualTreeInteractionProvider
    {
        public TestInteractionProvider(string source)
        {
            Source = source;
        }

        public string Source { get; }
        public string DisplayName => "Test interaction provider";
        public int ActionCount { get; private set; }

        public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
            => Task.FromResult(ToolResult.Success(new JsonObject
            {
                ["format"] = "ansight.test.visual-tree.compact.v2",
                ["platform"] = "test",
                ["capturedAtUtc"] = DateTimeOffset.UtcNow,
                ["types"] = new JsonArray("TestButton"),
                ["root"] = new JsonObject
                {
                    ["id"] = "root",
                    ["typeId"] = 0,
                    ["role"] = "button",
                    ["automationId"] = "save",
                    ["supportedActions"] = new JsonArray("tap"),
                    ["visible"] = true,
                    ["enabled"] = true,
                    ["children"] = new JsonArray()
                }
            }));

        public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
            => GetVisualTreeAsync(arguments);

        public Task<ToolResult> PerformActionAsync(
            VisualTreeActionRequest request,
            CancellationToken cancellationToken)
        {
            ActionCount++;
            return Task.FromResult(ToolResult.Success(new JsonObject { ["invoked"] = true }));
        }
    }
}
