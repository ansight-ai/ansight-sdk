using Ansight.Tools;
using Ansight.Tools.Clipboard;
using System.Text.Json.Nodes;
using Xunit;

namespace Ansight.UnitTests;

public sealed class ClipboardToolsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  hello\n世界 🧗\t")]
    public async Task RoundTripPreservesExactTextAndClearRemovesIt(string text)
    {
        var backend = new FakeClipboard();
        var write = await new SetTextClipboardTool(backend).Execute(new Dictionary<string, string> { ["text"] = text });
        Assert.True(write.IsSuccess);
        var read = await new GetTextClipboardTool(backend).Execute(new Dictionary<string, string>());
        Assert.Equal(text, read.Payload?["text"]?.GetValue<string>());
        Assert.True(read.Payload?["hasText"]?.GetValue<bool>());
        Assert.True((await new ClearClipboardTool(backend).Execute(new Dictionary<string, string>())).IsSuccess);
        read = await new GetTextClipboardTool(backend).Execute(new Dictionary<string, string>());
        Assert.Null(read.Payload?["text"]);
        Assert.False(read.Payload?["hasText"]?.GetValue<bool>());
    }

    [Fact]
    public async Task ValidationRejectsMissingAndOversizedTextBeforeMutation()
    {
        var backend = new FakeClipboard { Text = "original" };
        var tool = new SetTextClipboardTool(backend);
        Assert.Equal("clipboard_invalid_arguments", (await tool.Execute(new Dictionary<string, string>())).ErrorCode);
        Assert.Equal("clipboard_text_too_large", (await tool.Execute(new Dictionary<string, string> { ["text"] = new string('界', 21846) })).ErrorCode);
        Assert.Equal("original", backend.Text);
        backend.Text = new string('x', 65537);
        Assert.Equal("clipboard_text_too_large", (await new GetTextClipboardTool(backend).Execute(new Dictionary<string, string>())).ErrorCode);
    }

    [Fact]
    public async Task AccessFailureIsNotAnEmptyClipboard()
    {
        var backend = new FakeClipboard { Unavailable = true };
        var result = await new GetTextClipboardTool(backend).Execute(new Dictionary<string, string>());
        Assert.False(result.IsSuccess);
        Assert.Equal("clipboard_not_focused", result.ErrorCode);
        Assert.Null(result.Payload);
        Assert.False((await new HasTextClipboardTool(backend).Execute(new Dictionary<string, string>())).IsSuccess);
    }

    [Fact]
    public async Task HostFrameworkReportsUnsupportedAndPoliciesMatchMutations()
    {
        Assert.Equal("clipboard_platform_unsupported", (await new GetTextClipboardTool().Execute(new Dictionary<string, string>())).ErrorCode);
        Assert.Equal(ToolPolicy.Read, new GetTextClipboardTool().Policy);
        Assert.Equal(ToolPolicy.Read, new HasTextClipboardTool().Policy);
        Assert.Equal(ToolPolicy.Write, new SetTextClipboardTool().Policy);
        Assert.Equal(ToolPolicy.Write, new ClearClipboardTool().Policy);
    }

    [Fact]
    public async Task ProtocolAcceptsNullableTextButRejectsNonStringInput()
    {
        var backend = new FakeClipboard();
        var bridge = new ToolRegistry([new GetTextClipboardTool(backend), new SetTextClipboardTool(backend)])
            .CreateBridge(ToolGuard.ReadWrite);
        var read = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = "tool.call", Id = "read", SessionId = "test",
            Payload = new JsonObject { ["toolId"] = ClipboardToolIds.GetText, ["arguments"] = new JsonObject() }
        });
        Assert.Equal("tool.result", read.Type);
        Assert.True(((JsonObject)read.Payload!["result"]!).ContainsKey("text"));
        Assert.Null(read.Payload!["result"]!["text"]);
        var invalid = await bridge.HandleAsync(new ToolProtocolEnvelope
        {
            Type = "tool.call", Id = "invalid", SessionId = "test",
            Payload = new JsonObject { ["toolId"] = ClipboardToolIds.SetText, ["arguments"] = new JsonObject { ["text"] = 42 } }
        });
        Assert.Equal("tool.error", invalid.Type);
        Assert.Null(backend.Text);
    }

    private sealed class FakeClipboard : IClipboardBackend
    {
        internal string? Text { get; set; }
        internal bool Unavailable { get; set; }
        public string? GetText() => Unavailable ? throw new ClipboardException("clipboard_not_focused", "Focus the app.") : Text;
        public bool HasText() => GetText() is not null;
        public void SetText(string text) => Text = text;
        public void Clear() => Text = null;
    }
}
