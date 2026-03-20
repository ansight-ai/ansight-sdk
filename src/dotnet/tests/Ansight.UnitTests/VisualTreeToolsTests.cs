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
            ["ui.get_visual_tree", "ui.get_screenshot", "ui.inspect_node"],
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
}
