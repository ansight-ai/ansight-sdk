namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetResourceStateTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetResourceState;

    public string Name => "Get Resource State";

    public string Description => "Inspects MAUI resource dictionaries for the application, active window, pages, and selected elements.";

    public string Keywords => "maui resources resource dictionary styles dynamicresource merged dictionaries";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetResourceStateArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.ResourceStateResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetResourceState;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetString(arguments, "nodeId");
            var scope = (GetString(arguments, "scope") ?? "effective").Trim();
            var includeValues = GetBoolean(arguments, "includeValues", defaultValue: false);
            var includeMergedDictionaries = GetBoolean(arguments, "includeMergedDictionaries", defaultValue: true);
            var maxEntries = GetInt(arguments, "maxEntries", DefaultMaxItems, minimum: 1, maximum: MaximumMaxItems);

            var normalizedScope = scope switch
            {
                "application" or "window" or "page" or "element" or "effective" => scope,
                _ when string.Equals(scope, "application", StringComparison.OrdinalIgnoreCase) => "application",
                _ when string.Equals(scope, "window", StringComparison.OrdinalIgnoreCase) => "window",
                _ when string.Equals(scope, "page", StringComparison.OrdinalIgnoreCase) => "page",
                _ when string.Equals(scope, "element", StringComparison.OrdinalIgnoreCase) => "element",
                _ when string.Equals(scope, "effective", StringComparison.OrdinalIgnoreCase) => "effective",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return ToolResult.Failure("The scope argument must be one of: effective, application, window, page, element.", errorCode: "maui_invalid_resource_scope");
            }

            var application = Application.Current;
            if (application == null)
            {
                return ToolResult.Failure("No MAUI application is available.", errorCode: "maui_application_unavailable");
            }

            var window = GetActiveWindow(application);
            if (window == null)
            {
                return ToolResult.Failure("No active MAUI window is available.", errorCode: "maui_window_unavailable");
            }

            MauiElementResolution? resolution = null;
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                resolution = ResolveElement(nodeId);
                if (resolution == null)
                {
                    return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
                }
            }

            var targets = new List<ResourceScopeTarget>();
            if (normalizedScope is "application" or "effective")
            {
                targets.Add(new ResourceScopeTarget("application", application));
            }

            if (normalizedScope is "window" or "effective")
            {
                targets.Add(new ResourceScopeTarget("window", window));
            }

            if (normalizedScope is "page" or "effective")
            {
                var page = window.Page == null ? null : ResolveDisplayedPage(window.Page) ?? window.Page;
                if (page != null)
                {
                    targets.Add(new ResourceScopeTarget("page", page));
                }
            }

            if (resolution != null && normalizedScope is "element" or "effective")
            {
                foreach (var ancestor in resolution.Ancestors)
                {
                    targets.Add(new ResourceScopeTarget("ancestor", ancestor));
                }

                targets.Add(new ResourceScopeTarget("element", resolution.Element));
            }

            var dictionaries = new JsonArray();
            foreach (var target in targets)
            {
                if (!TryReadPublicProperty(target.Owner, "Resources", out var resources, out _) || resources == null)
                {
                    continue;
                }

                dictionaries.Add(CreateResourceDictionarySnapshot(resources, target.Scope, target.Owner, includeValues, includeMergedDictionaries, maxEntries));
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["scope"] = normalizedScope,
                ["node"] = resolution == null ? null : CreateElementReference(resolution.Element),
                ["dictionaryCount"] = dictionaries.Count,
                ["dictionaries"] = dictionaries
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
