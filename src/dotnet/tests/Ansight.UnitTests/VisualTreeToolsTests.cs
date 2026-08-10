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
}
