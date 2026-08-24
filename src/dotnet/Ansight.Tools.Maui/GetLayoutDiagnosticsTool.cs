namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetLayoutDiagnosticsTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => MauiToolIds.GetLayoutDiagnostics;

    public string Name => "Get Layout Diagnostics";

    public string Description => "Returns layout measurements, attached layout values, visibility, and input-related diagnostics for one MAUI element.";

    public string Keywords => "maui layout diagnostics bounds margin grid absolute flex visibility input";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.NodeOnlyArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.DiagnosticsResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            if (resolution.Element is not VisualElement visualElement)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' is not a VisualElement.", errorCode: "maui_node_not_visual");
            }

            var ancestorOffsetX = visualElement.X;
            var ancestorOffsetY = visualElement.Y;
            foreach (var ancestor in resolution.Ancestors.OfType<VisualElement>())
            {
                ancestorOffsetX += ancestor.X;
                ancestorOffsetY += ancestor.Y;
            }

            var layout = new JsonObject
            {
                ["bounds"] = CreateBoundsSnapshot(visualElement),
                ["ancestorOffsetBounds"] = new JsonObject
                {
                    ["x"] = ancestorOffsetX,
                    ["y"] = ancestorOffsetY,
                    ["width"] = visualElement.Width,
                    ["height"] = visualElement.Height
                },
                ["coordinateSpace"] = "ancestorOffset",
                ["visible"] = visualElement.IsVisible,
                ["enabled"] = visualElement.IsEnabled,
                ["inputTransparent"] = visualElement.InputTransparent,
                ["opacity"] = visualElement.Opacity,
                ["zIndex"] = visualElement.ZIndex,
                ["widthRequest"] = visualElement.WidthRequest,
                ["heightRequest"] = visualElement.HeightRequest,
                ["minimumWidthRequest"] = visualElement.MinimumWidthRequest,
                ["minimumHeightRequest"] = visualElement.MinimumHeightRequest,
                ["translationX"] = visualElement.TranslationX,
                ["translationY"] = visualElement.TranslationY,
                ["scale"] = visualElement.Scale,
                ["rotation"] = visualElement.Rotation
            };

            if (TryReadPublicProperty(visualElement, "DesiredSize", out var desiredSize, out var desiredSizeType))
            {
                layout["desiredSize"] = CreateValueSnapshot(desiredSize, desiredSizeType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
            }

            if (TryReadPublicProperty(visualElement, "Clip", out var clip, out var clipType))
            {
                layout["clip"] = CreateValueSnapshot(clip, clipType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
            }

            if (visualElement is View view)
            {
                layout["margin"] = view.Margin.ToString();
                layout["horizontalOptions"] = view.HorizontalOptions.ToString();
                layout["verticalOptions"] = view.VerticalOptions.ToString();
            }

            if (visualElement is Page page)
            {
                layout["padding"] = page.Padding.ToString();
            }

            var attached = new JsonObject();
            if (visualElement is BindableObject bindable)
            {
                attached["grid"] = new JsonObject
                {
                    ["row"] = Grid.GetRow(bindable),
                    ["column"] = Grid.GetColumn(bindable),
                    ["rowSpan"] = Grid.GetRowSpan(bindable),
                    ["columnSpan"] = Grid.GetColumnSpan(bindable)
                };

                attached["absoluteLayout"] = new JsonObject
                {
                    ["bounds"] = AbsoluteLayout.GetLayoutBounds(bindable).ToString(),
                    ["flags"] = AbsoluteLayout.GetLayoutFlags(bindable).ToString()
                };

                attached["flexLayout"] = new JsonObject
                {
                    ["grow"] = FlexLayout.GetGrow(bindable),
                    ["shrink"] = FlexLayout.GetShrink(bindable),
                    ["order"] = FlexLayout.GetOrder(bindable),
                    ["alignSelf"] = FlexLayout.GetAlignSelf(bindable).ToString(),
                    ["basis"] = FlexLayout.GetBasis(bindable).ToString()
                };
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["parent"] = resolution.Element.Parent == null ? null : CreateElementReference(resolution.Element.Parent),
                ["path"] = CreateElementPath(resolution.Ancestors, resolution.Element),
                ["layout"] = layout,
                ["attached"] = attached
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
