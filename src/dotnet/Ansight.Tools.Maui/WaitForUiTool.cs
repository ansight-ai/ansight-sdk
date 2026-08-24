namespace Ansight.Tools.Maui;

using System.Diagnostics;
using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
#endif

public sealed class WaitForUiTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => MauiToolIds.WaitForUi;

    public string Name => "Wait For UI";

    public string Description => "Polls the MAUI main thread until an element, page, property, or binding-context condition is met.";

    public string Keywords => "maui wait polling async ui condition visible property page";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.WaitForUiArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.WaitForUiResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return ExecuteAsync();

        async Task<ToolResult> ExecuteAsync()
        {
            var condition = GetRequiredString(arguments, "condition");
            var timeoutMs = GetInt(arguments, "timeoutMs", defaultValue: 5000, minimum: 1, maximum: 60000);
            var pollIntervalMs = GetInt(arguments, "pollIntervalMs", defaultValue: 100, minimum: 10, maximum: 5000);
            var rootScope = (GetString(arguments, "root") ?? "currentPage").Trim();
            var includeInactivePages = GetBoolean(arguments, "includeInactivePages", defaultValue: false);
            var maxDepth = GetInt(arguments, "maxDepth", DefaultTreeDepth, minimum: 0, maximum: MaximumTreeDepth);

            var stopwatch = Stopwatch.StartNew();
            JsonNode? lastPayload = null;

            while (stopwatch.ElapsedMilliseconds <= timeoutMs)
            {
                ToolResult evaluation;
                try
                {
                    evaluation = await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        var nodeId = GetString(arguments, "nodeId");
                        var automationId = GetString(arguments, "automationId");
                        var typeName = GetString(arguments, "typeName");
                        var labelContains = GetString(arguments, "labelContains");
                        var propertyName = GetString(arguments, "propertyName");
                        var expectedJson = GetString(arguments, "expectedJson");
                        var normalizedCondition = condition.Trim().ToLowerInvariant();

                        JsonObject CreatePayload(bool matched)
                        {
                            return new JsonObject
                            {
                                ["platform"] = CurrentPlatform,
                                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                                ["condition"] = normalizedCondition,
                                ["matched"] = matched,
                                ["elapsedMs"] = stopwatch.ElapsedMilliseconds
                            };
                        }

                        if (normalizedCondition == "currentpage")
                        {
                            var application = Application.Current;
                            var window = application == null ? null : GetActiveWindow(application);
                            var currentPage = window?.Page == null ? null : ResolveDisplayedPage(window.Page) ?? window.Page;
                            var matched = currentPage != null;

                            if (matched && !string.IsNullOrWhiteSpace(typeName))
                            {
                                matched = IsTypeNameMatch(currentPage!.GetType(), typeName) ||
                                          GetTypeDisplayName(currentPage.GetType()).Contains(typeName, StringComparison.OrdinalIgnoreCase);
                            }

                            if (matched && !string.IsNullOrWhiteSpace(labelContains))
                            {
                                matched = currentPage?.Title?.Contains(labelContains, StringComparison.OrdinalIgnoreCase) ?? false;
                            }

                            var payload = CreatePayload(matched);
                            payload["currentPage"] = currentPage == null ? null : CreateElementReference(currentPage);
                            return ToolResult.Success(payload);
                        }

                        if (normalizedCondition == "propertyequals")
                        {
                            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(propertyName) || expectedJson == null)
                            {
                                return ToolResult.Failure("propertyEquals requires nodeId, propertyName, and expectedJson.", errorCode: "maui_wait_invalid_arguments");
                            }

                            var resolution = ResolveElement(nodeId);
                            if (resolution == null || resolution.Element is not BindableObject bindable)
                            {
                                return ToolResult.Success(CreatePayload(matched: false));
                            }

                            var descriptor = ResolveBindableProperty(bindable, propertyName, declaringTypeName: null);
                            if (descriptor == null)
                            {
                                return ToolResult.Success(CreatePayload(matched: false));
                            }

                            var value = bindable.GetValue(descriptor.BindableProperty);
                            var payload = CreatePayload(AreValuesEquivalent(value, descriptor.BindableProperty.ReturnType, expectedJson));
                            payload["node"] = CreateElementReference(resolution.Element);
                            payload["property"] = CreateBindablePropertyMetadata(bindable, descriptor);
                            payload["value"] = CreateValueSnapshot(value, descriptor.BindableProperty.ReturnType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties);
                            return ToolResult.Success(payload);
                        }

                        if (normalizedCondition == "bindingcontextpropertyequals")
                        {
                            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(propertyName) || expectedJson == null)
                            {
                                return ToolResult.Failure("bindingContextPropertyEquals requires nodeId, propertyName, and expectedJson.", errorCode: "maui_wait_invalid_arguments");
                            }

                            var resolution = ResolveElement(nodeId);
                            var bindingContext = resolution?.Element.BindingContext;
                            if (bindingContext == null ||
                                !TryReadPublicProperty(bindingContext, propertyName, out var propertyValue, out var propertyType) ||
                                propertyType == null)
                            {
                                return ToolResult.Success(CreatePayload(matched: false));
                            }

                            var payload = CreatePayload(AreValuesEquivalent(propertyValue, propertyType, expectedJson));
                            payload["node"] = CreateElementReference(resolution!.Element);
                            payload["bindingContextType"] = CreateTypeMetadata(bindingContext.GetType());
                            payload["propertyName"] = propertyName;
                            payload["value"] = CreateValueSnapshot(propertyValue, propertyType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties);
                            return ToolResult.Success(payload);
                        }

                        if (normalizedCondition is not ("elementexists" or "elementvisible"))
                        {
                            return ToolResult.Failure("The condition argument must be one of: elementExists, elementVisible, propertyEquals, currentPage, bindingContextPropertyEquals.", errorCode: "maui_wait_invalid_condition");
                        }

                        var matches = new JsonArray();
                        if (!string.IsNullOrWhiteSpace(nodeId))
                        {
                            var resolution = ResolveElement(nodeId);
                            if (resolution != null &&
                                (normalizedCondition != "elementvisible" ||
                                 (resolution.Element is VisualElement visualElement && visualElement.IsVisible)))
                            {
                                matches.Add(CreateElementMatch(new MauiElementTraversalEntry(resolution.Element, resolution.Ancestors, resolution.Ancestors.Count), includeBounds: true, includeProperties: false));
                            }
                        }
                        else if (TryGetActiveRoot(rootScope, out var rootElement, out _, out _))
                        {
                            var traversalOptions = includeInactivePages ? MauiElementTraversalOptions.Full : MauiElementTraversalOptions.ActiveNavigationOnly;
                            foreach (var entry in TraverseElements(rootElement, maxDepth, traversalOptions))
                            {
                                var element = entry.Element;
                                if (!string.IsNullOrWhiteSpace(automationId) &&
                                    !string.Equals(element.AutomationId, automationId, StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(typeName) &&
                                    !IsTypeNameMatch(element.GetType(), typeName) &&
                                    !GetTypeDisplayName(element.GetType()).Contains(typeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(labelContains) &&
                                    !(GetElementLabel(element)?.Contains(labelContains, StringComparison.OrdinalIgnoreCase) ?? false))
                                {
                                    continue;
                                }

                                if (normalizedCondition == "elementvisible" &&
                                    (element is not VisualElement visibleElement || !visibleElement.IsVisible))
                                {
                                    continue;
                                }

                                matches.Add(CreateElementMatch(entry, includeBounds: true, includeProperties: false));
                                break;
                            }
                        }

                        var resultPayload = CreatePayload(matches.Count > 0);
                        resultPayload["matches"] = matches;
                        return ToolResult.Success(resultPayload);
                    });
                }
                catch (Exception exception)
                {
                    return ToolResult.Failure(exception.Message, errorCode: "maui_execution_failed");
                }

                if (!evaluation.IsSuccess)
                {
                    return evaluation;
                }

                lastPayload = evaluation.Payload;
                if (evaluation.Payload is JsonObject jsonObject &&
                    jsonObject["matched"]?.GetValue<bool>() == true)
                {
                    return evaluation;
                }

                await Task.Delay(pollIntervalMs);
            }

            return ToolResult.Failure(
                $"Timed out after {timeoutMs}ms waiting for MAUI UI condition '{condition}'.",
                errorCode: "maui_wait_timeout",
                payload: lastPayload);
        }
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
