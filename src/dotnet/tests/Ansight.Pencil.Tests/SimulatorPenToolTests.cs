using System.Text.Json.Nodes;
using Ansight.Pencil;
using Xunit;
using Ansight.Tools;

namespace Ansight.Pencil.Tests;

public sealed class SimulatorPenToolTests
{
    [Fact]
    public async Task PlaybackPreservesStructuredSamplesAcrossNativeBoundary()
    {
        JsonNode? received = null;
        var tool = new SimulatorPenTool(json =>
        {
            received = JsonNode.Parse(json);
            return "{\"success\":true,\"result\":{\"performed\":true,\"recognizerCount\":2}}";
        });
        var arguments = JsonNode.Parse("""
            {"action":"prepare","runId":"983a047d-b904-4253-bfcc-a9a271bcf464","target":"canvas","hoverRecognizer":"hover",
             "samples":[{"phase":"down","x":0.2,"y":0.4,"pressure":0.875,"rollRadians":7.25,"eventTimestamp":1234.56789},
                        {"phase":"up","x":0.2,"y":0.4}]}
            """)!.AsObject();
        var result = await tool.ExecuteAsync(new ToolInvocation(arguments,
            new ToolInvocationContext("pen-test", "session", null)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(JsonNode.DeepEquals(arguments, received));
        Assert.True(result.Payload!["performed"]!.GetValue<bool>());
        Assert.Equal(2, result.Payload["recognizerCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task ManagedReadOnlyGuardPreventsNativeInvocation()
    {
        var invoked = false;
        var bridge = new ToolRegistry([new SimulatorPenTool(_ =>
        {
            invoked = true;
            return "{\"success\":true}";
        })]).CreateBridge(ToolGuard.ReadOnly);
        Assert.Empty(bridge.GetVisibleTools());
        var response = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.CallType,
            Id = "denied-pen",
            Payload = new JsonObject
            {
                ["toolId"] = "ui.simulate_pen",
                ["arguments"] = new JsonObject { ["action"] = "finish", ["runId"] = Guid.NewGuid().ToString() }
            }
        });
        Assert.False(invoked);
        Assert.Equal(ToolProtocolBridge.ErrorType, response.Type);
        Assert.Equal("tool_execution_denied", response.Payload?["code"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"success\":false,\"errorCode\":\"pen_busy\",\"message\":\"Already running\"}", "pen_busy")]
    [InlineData("not json", "pen_native_failed")]
    public async Task NativeFailuresAreReturnedWithoutEscapingCallback(string response, string errorCode)
    {
        var tool = new SimulatorPenTool(_ => response);
        var result = await tool.ExecuteAsync(new ToolInvocation(new JsonObject(),
            new ToolInvocationContext("pen-test", null, null)), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task CancellationDoesNotEnterNativeCode()
    {
        var tool = new SimulatorPenTool(_ => throw new InvalidOperationException("Must not execute"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.ExecuteAsync(
            new ToolInvocation(new JsonObject(), new ToolInvocationContext("cancelled", null, null)),
            new CancellationToken(true)));
    }
}
