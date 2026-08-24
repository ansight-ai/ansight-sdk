using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Tools.FileSystem;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;

namespace Ansight.UnitTests;

public sealed class ToolPolicyTests
{
    [Theory]
    [InlineData(ToolPolicy.Read, ToolPolicy.Read, true)]
    [InlineData(ToolPolicy.Read, ToolPolicy.Write, true)]
    [InlineData(ToolPolicy.Write, ToolPolicy.Read, false)]
    [InlineData(ToolPolicy.Write, ToolPolicy.Write, true)]
    [InlineData(ToolPolicy.Critical, ToolPolicy.Write, false)]
    [InlineData(ToolPolicy.Critical, ToolPolicy.Critical, true)]
    public void PolicyGrant_IsHierarchical(
        ToolPolicy required,
        ToolPolicy granted,
        bool expected)
    {
        var guard = new ToolGuard(true, true, granted);
        var tool = new TestTool(required);

        Assert.Equal(expected, guard.CanExecute(tool, out _));
        Assert.Equal(expected, guard.IsToolVisible(tool));
    }

    [Fact]
    public void BuiltInTools_UseExpectedPolicyBoundaries()
    {
        Assert.Equal(ToolPolicy.Read, new GetScreenshotTool().Policy);
        Assert.Equal(ToolPolicy.Write, new PerformActionTool().Policy);
        Assert.Equal(ToolPolicy.Critical, new DeleteFileTool().Policy);
        Assert.Equal(ToolPolicy.Critical, new GetSecureStorageValueTool().Policy);
    }

    [Fact]
    public async Task QueryCatalog_EmitsOnlyTheCollapsedPolicy()
    {
        var bridge = new ToolRegistry([new GetSecureStorageValueTool()])
            .CreateBridge(ToolGuard.FullAccess);

        var response = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.QueryType,
            Id = "req_1",
            Payload = new JsonObject()
        });

        var payload = Assert.IsType<JsonObject>(response.Payload);
        var tools = Assert.IsType<JsonArray>(payload["tools"]);
        var tool = Assert.IsType<JsonObject>(Assert.Single(tools));

        Assert.Equal("critical", tool["policy"]?.GetValue<string>());
        Assert.False(tool.ContainsKey("scope"));
        Assert.False(tool.ContainsKey("security"));
    }

    private sealed class TestTool(ToolPolicy policy) : ITool
    {
        public string Category => "test";
        public ToolPolicy Policy { get; } = policy;
        public string Id => "test.policy";
        public string Name => "Policy test";
        public string Description => "Tests policy hierarchy.";
        public string Keywords => "test";
        public ToolSchema ArgumentsSchema => ToolSchema.Object();
        public ToolSchema ResultSchema => ToolSchema.Object();

        public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
            => Task.FromResult(ToolResult.Success());
    }
}
