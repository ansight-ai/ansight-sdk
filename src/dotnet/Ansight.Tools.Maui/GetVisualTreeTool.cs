namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class GetVisualTreeTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetVisualTree;

    public string Name => "Get Visual Tree";

    public string Description => "Returns the live .NET MAUI visual tree for the active window or page.";

    public string Keywords => "maui visual tree ui hierarchy elements xaml";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetVisualTreeArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.VisualTreeResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetVisualTree;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var rootScope = (GetString(arguments, "root") ?? "currentPage").Trim();
            var includeBounds = GetBoolean(arguments, "includeBounds", defaultValue: true);
            var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: false);
            var includeBindableProperties = GetBoolean(arguments, "includeBindableProperties", defaultValue: false);
            var includeBindingContexts = GetBoolean(arguments, "includeBindingContexts", defaultValue: false);
            var maxDepth = GetInt(arguments, "maxDepth", DefaultTreeDepth, minimum: 0, maximum: MaximumTreeDepth);
            var maxNodes = GetInt(arguments, "maxNodes", DefaultTreeMaxNodes, minimum: 1, maximum: MaximumTreeMaxNodes);
            var rootNodeId = GetString(arguments, "rootNodeId");

            if (!TryGetActiveRootContext(rootScope, out var rootContext, out var error))
            {
                return ToolResult.Failure(error ?? "No active MAUI visual tree root is available.", errorCode: "maui_visual_tree_unavailable");
            }

            var selectedRoot = rootContext.Root;
            if (!string.IsNullOrWhiteSpace(rootNodeId))
            {
                var resolution = ResolveElement(rootNodeId);
                if (resolution == null)
                {
                    return ToolResult.Failure($"The MAUI node '{rootNodeId}' was not found.", errorCode: "maui_node_not_found");
                }

                selectedRoot = resolution.Element;
            }

            var options = new MauiTreeBuildOptions(
                includeBounds,
                includeProperties,
                includeBindableProperties,
                includeBindingContexts,
                maxNodes,
                rootContext.CurrentPage == null ? null : GetElementId(rootContext.CurrentPage));
            var state = new MauiTreeBuildState(maxNodes);
            var selectedRootIsInCurrentPage = rootContext.CurrentPage != null && IsElementDescendantOrSelf(rootContext.CurrentPage, selectedRoot);

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["rootScope"] = rootContext.NormalizedRootScope,
                ["rootPage"] = rootContext.RootPage == null ? null : CreateElementReference(rootContext.RootPage),
                ["currentPage"] = rootContext.CurrentPage == null ? null : CreateElementReference(rootContext.CurrentPage),
                ["coordinateSpace"] = CreateCoordinateSpaceSnapshot(rootContext.RootPage),
                ["root"] = BuildElementNode(
                    selectedRoot,
                    options,
                    maxDepth,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    state,
                    selectedRootIsInCurrentPage),
                ["nodeCount"] = state.NodeCount,
                ["truncated"] = state.Truncated
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
