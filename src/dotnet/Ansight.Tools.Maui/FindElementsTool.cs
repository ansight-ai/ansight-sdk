namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class FindElementsTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => MauiToolIds.FindElements;

    public string Name => "Find Elements";

    public string Description => "Searches the live .NET MAUI visual tree using common element, binding-context, and bindable-property filters.";

    public string Keywords => "maui find search query visual tree elements automationid type label";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.FindElementsArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.FindElementsResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var rootScope = (GetString(arguments, "root") ?? "currentPage").Trim();
            var rootNodeId = GetString(arguments, "rootNodeId");
            var nodeId = GetString(arguments, "nodeId");
            var automationId = GetString(arguments, "automationId");
            var styleId = GetString(arguments, "styleId");
            var classId = GetString(arguments, "classId");
            var typeName = GetString(arguments, "typeName");
            var kind = GetString(arguments, "kind");
            var labelContains = GetString(arguments, "labelContains");
            var bindingContextTypeName = GetString(arguments, "bindingContextTypeName");
            var propertyName = GetString(arguments, "propertyName");
            var propertyValueJson = GetString(arguments, "propertyValueJson");
            var includeBounds = GetBoolean(arguments, "includeBounds", defaultValue: true);
            var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: false);
            var includeInactivePages = GetBoolean(arguments, "includeInactivePages", defaultValue: false);
            var maxDepth = GetInt(arguments, "maxDepth", DefaultTreeDepth, minimum: 0, maximum: MaximumTreeDepth);
            var maxResults = GetInt(arguments, "maxResults", DefaultSearchResults, minimum: 1, maximum: MaximumSearchResults);

            bool? visibleFilter = arguments.TryGetValue("visible", out var rawVisible) && !string.IsNullOrWhiteSpace(rawVisible)
                ? GetBoolean(arguments, "visible", defaultValue: false)
                : null;
            bool? enabledFilter = arguments.TryGetValue("enabled", out var rawEnabled) && !string.IsNullOrWhiteSpace(rawEnabled)
                ? GetBoolean(arguments, "enabled", defaultValue: false)
                : null;

            if (!TryGetActiveRoot(rootScope, out var rootElement, out var error, out var normalizedRootScope))
            {
                return ToolResult.Failure(error ?? "No active MAUI visual tree root is available.", errorCode: "maui_visual_tree_unavailable");
            }

            var selectedRoot = rootElement;
            if (!string.IsNullOrWhiteSpace(rootNodeId))
            {
                var resolution = ResolveElement(rootNodeId);
                if (resolution == null)
                {
                    return ToolResult.Failure($"The MAUI node '{rootNodeId}' was not found.", errorCode: "maui_node_not_found");
                }

                selectedRoot = resolution.Element;
            }

            var matches = new JsonArray();
            var totalMatches = 0;

            var traversalOptions = includeInactivePages ? MauiElementTraversalOptions.Full : MauiElementTraversalOptions.ActiveNavigationOnly;
            foreach (var entry in TraverseElements(selectedRoot, maxDepth, traversalOptions))
            {
                var element = entry.Element;

                if (!string.IsNullOrWhiteSpace(nodeId) && !IsElementMatch(element, nodeId))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(automationId) &&
                    !string.Equals(element.AutomationId, automationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(styleId) &&
                    !string.Equals(element.StyleId, styleId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(classId) &&
                    !string.Equals(element.ClassId, classId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(typeName) &&
                    !IsTypeNameMatch(element.GetType(), typeName) &&
                    !GetTypeDisplayName(element.GetType()).Contains(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(kind) &&
                    !string.Equals(GetElementKind(element), kind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(labelContains) &&
                    !(GetElementLabel(element)?.Contains(labelContains, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(bindingContextTypeName) &&
                    (element.BindingContext == null ||
                     (!IsTypeNameMatch(element.BindingContext.GetType(), bindingContextTypeName) &&
                      !GetTypeDisplayName(element.BindingContext.GetType()).Contains(bindingContextTypeName, StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                if (visibleFilter.HasValue && (element is not VisualElement visibleElement || visibleElement.IsVisible != visibleFilter.Value))
                {
                    continue;
                }

                if (enabledFilter.HasValue && (element is not VisualElement enabledElement || enabledElement.IsEnabled != enabledFilter.Value))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    if (element is not BindableObject bindable)
                    {
                        continue;
                    }

                    var descriptor = ResolveBindableProperty(bindable, propertyName, declaringTypeName: null);
                    if (descriptor == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(propertyValueJson))
                    {
                        if (!TryGetBindablePropertyValue(bindable, descriptor.BindableProperty, out var value))
                        {
                            continue;
                        }

                        if (!AreValuesEquivalent(value, descriptor.BindableProperty.ReturnType, propertyValueJson))
                        {
                            continue;
                        }
                    }
                }

                totalMatches++;
                if (matches.Count < maxResults)
                {
                    matches.Add(CreateElementMatch(entry, includeBounds, includeProperties));
                }
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["rootScope"] = normalizedRootScope,
                ["includeInactivePages"] = includeInactivePages,
                ["matchCount"] = totalMatches,
                ["returnedCount"] = matches.Count,
                ["truncated"] = totalMatches > maxResults,
                ["matches"] = matches
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
