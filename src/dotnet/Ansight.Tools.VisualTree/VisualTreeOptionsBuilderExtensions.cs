namespace Ansight.Tools.VisualTree;

using System;

public static class VisualTreeOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithVisualTreeTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTools(new ITool[]
        {
            new GetVisualTreeTool(),
            new GetScreenshotTool(),
            new InspectNodeTool()
        });
    }
}
