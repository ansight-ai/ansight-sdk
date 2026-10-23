using System.Text.Json.Nodes;
using Ansight.Pencil;
using Ansight.Tools;
using Xunit;

namespace Ansight.Pencil.Tests;

public sealed class ScopedPermissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedGrantAllowsPencilWithoutAllowingOtherWrites(bool setGuardLast)
    {
        var calls = 0;
        var pencil = new SimulatorPenTool(_ => { calls++; return "{\"success\":true,\"result\":{}}"; });
        var builder = Options.CreateBuilder().AddTool(pencil);
        if (!setGuardLast) builder.WithReadOnlyToolAccess();
        builder.WithToolGrant(pencil.Id, ToolPolicy.Write);
        if (setGuardLast) builder.WithReadOnlyToolAccess();
        var options = Options.CreateBuilder(builder.Build()).Build();
        Assert.Equal(ToolPolicy.Read, options.ToolGuard.MaxPolicy);
        Assert.True(options.ToolGuard.CanExecute(pencil, out _));
        Assert.False(options.ToolGuard.CanExecute(new OtherTool("other.write", ToolPolicy.Write), out _));
        Assert.False(options.ToolGuard.CanExecute(new OtherTool(pencil.Id, ToolPolicy.Critical), out _));
        Assert.False(ToolGuard.ReadOnly.CanExecute(pencil, out _));
        var bridge = options.Tools.CreateBridge(options.ToolGuard);
        Assert.Single(bridge.GetVisibleTools());
        var response = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.CallType, Id = "pencil-scoped",
            Payload = new JsonObject { ["toolId"] = pencil.Id, ["arguments"] = new JsonObject { ["action"] = "finish", ["runId"] = Guid.NewGuid().ToString() } }
        });
        Assert.True(response.Type == ToolProtocolBridge.ResultType, response.Payload?.ToJsonString());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalDisableWinsRegardlessOfBuilderOrder(bool disableLast)
    {
        var pencil = new SimulatorPenTool(_ => throw new Exception("Must not execute"));
        var builder = Options.CreateBuilder().AddTool(pencil);
        if (!disableLast) builder.WithToolsDisabled();
        builder.WithToolGrant(pencil.Id, ToolPolicy.Write);
        if (disableLast) builder.WithToolsDisabled();
        var options = Options.CreateBuilder(builder.Build()).Build();
        Assert.False(options.ToolGuard.CanExecute(pencil, out _));
        Assert.Empty(options.Tools.CreateBridge(options.ToolGuard).GetVisibleTools());
    }

    [Fact]
    public void DefaultOptionsCanOptIntoOneToolWithoutGrantingUnrelatedReads()
    {
        var pencil = new SimulatorPenTool(_ => "{\"success\":true}");
        var options = Options.CreateBuilder().WithToolGrant(pencil.Id, ToolPolicy.Write).Build();
        Assert.True(options.ToolGuard.CanExecute(pencil, out _));
        Assert.False(options.ToolGuard.CanExecute(new OtherTool("other.read", ToolPolicy.Read), out _));
        Assert.False(Options.Default.ToolGuard.CanExecute(pencil, out _));
    }

    private sealed class OtherTool(string id, ToolPolicy policy) : ITool
    {
        public string Id => id;
        public string Name => id;
        public string Description => id;
        public string Category => "test";
        public string Keywords => "test";
        public ToolPolicy Policy => policy;
        public ToolSchema ArgumentsSchema => ToolSchema.Object();
        public ToolSchema ResultSchema => ToolSchema.Object();
        public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments) => Task.FromResult(ToolResult.Success());
    }
}
