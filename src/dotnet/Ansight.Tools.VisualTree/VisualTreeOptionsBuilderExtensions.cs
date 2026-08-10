namespace Ansight.Tools.VisualTree;

using System;
using Ansight.Screenshot;
using System.Text.Json.Nodes;

public static class VisualTreeOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithVisualTreeTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        SessionVisualTreeCaptureRegistry.SetProvider(async cancellationToken =>
        {
            var snapshots = new List<JsonObject>();
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["includeBounds"] = "true",
                ["includeComputedStyles"] = "true",
                ["maxDepth"] = "40",
                ["maxNodes"] = "2000"
            };
            foreach (var provider in VisualTreeProviderRegistry.GetProviders())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await provider.GetVisualTreeAsync(arguments);
                if (result.IsSuccess && result.Payload is JsonObject payload)
                {
                    snapshots.Add(payload.DeepClone() as JsonObject ?? new JsonObject());
                }
            }

            return snapshots;
        });

        return builder.AddTools(new ITool[]
        {
            new GetVisualTreeTool(),
            new GetScreenshotTool(),
            new InspectNodeTool(),
            new ShowOverlayTool(),
            new GetOverlayTool(),
            new QueryOverlaysTool(),
            new UpdateOverlayTool(),
            new RemoveOverlayTool(),
            new ClearOverlaysTool()
        });
    }
}
