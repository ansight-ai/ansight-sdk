using System.Text.Json.Nodes;
using Ansight.Tools.VisualTree;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaElementActions
{
    internal static JsonArray GetSupportedActions(Control control, AutomationPeer peer)
    {
        var actions = new JsonArray();

        if (!control.IsEffectivelyEnabled)
        {
            return actions;
        }

        actions.Add((JsonNode?)JsonValue.Create("bring_into_view"));

        if (control.Focusable)
        {
            actions.Add((JsonNode?)JsonValue.Create("focus"));
        }

        if (peer.GetProvider<IInvokeProvider>() is not null)
        {
            actions.Add((JsonNode?)JsonValue.Create("tap"));
        }

        var writableValue = peer.GetProvider<IValueProvider>() is { IsReadOnly: false }
            || peer.GetProvider<IRangeValueProvider>() is { IsReadOnly: false };

        if (writableValue)
        {
            actions.Add((JsonNode?)JsonValue.Create("setValue"));
        }

        if (control is TextBox { IsReadOnly: false })
        {
            actions.Add((JsonNode?)JsonValue.Create("typeText"));
        }

        if (peer.GetProvider<IToggleProvider>() is not null)
        {
            actions.Add((JsonNode?)JsonValue.Create("toggle"));
        }

        if (peer.GetProvider<ISelectionItemProvider>() is not null || control is SelectingItemsControl)
        {
            actions.Add((JsonNode?)JsonValue.Create("select"));
        }

        if (control is ScrollViewer)
        {
            actions.Add((JsonNode?)JsonValue.Create("scroll"));
        }

        return actions;
    }

    internal static ToolResult Perform(StyledElement element, VisualTreeActionRequest request)
    {
        if (element is not Control control)
        {
            return Unsupported(request.Action);
        }

        if (!control.IsEffectivelyEnabled)
        {
            return ToolResult.Failure("The control is disabled.", "avalonia_element_disabled");
        }

        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        var action = request.Action.ToLowerInvariant();

        switch (action)
        {
            case "tap":
            case "click":
            case "invoke":
                if (peer.GetProvider<IInvokeProvider>() is not { } invocation)
                {
                    return Unsupported(action);
                }

                invocation.Invoke();
                break;

            case "focus":
                if (!control.Focus())
                {
                    return ToolResult.Failure("The control could not receive focus.", "avalonia_focus_failed");
                }

                break;

            case "unfocus":
                TopLevel.GetTopLevel(control)?.FocusManager?.Focus(null);
                break;

            case "bring_into_view":
            case "scroll_into_view":
                control.BringIntoView();
                break;

            case "set_value":
            case "set_text":
            case "setvalue":
                return SetValue(peer, request);

            case "typetext":
                if (control is not TextBox textBox || textBox.IsReadOnly)
                {
                    return Unsupported(action);
                }

                if (request.Value is not JsonValue textScalar || !textScalar.TryGetValue<string>(out var enteredText))
                {
                    throw new ArgumentException("Text entry requires a string value.");
                }

                textBox.SelectedText = enteredText;
                break;

            case "toggle":
            case "set_checked":
                if (peer.GetProvider<IToggleProvider>() is not { } toggle)
                {
                    return Unsupported(action);
                }

                if (request.Value is null)
                {
                    toggle.Toggle();
                }
                else
                {
                    var target = RequireBoolean(request.Value) ? ToggleState.On : ToggleState.Off;

                    for (var attempt = 0; attempt < 3 && toggle.ToggleState != target; attempt++)
                    {
                        toggle.Toggle();
                    }

                    if (toggle.ToggleState != target)
                    {
                        return ToolResult.Failure("The control did not reach the requested toggle state.", "avalonia_action_rejected");
                    }
                }

                break;

            case "select":
            case "selecttab":
                if (request.Value is null && peer.GetProvider<ISelectionItemProvider>() is { } selection)
                {
                    selection.Select();
                    break;
                }

                if (control is not SelectingItemsControl items)
                {
                    return Unsupported(action);
                }

                var hasValidIndex = request.Value is JsonValue indexValue
                    && indexValue.TryGetValue<int>(out var selectedIndex)
                    && selectedIndex >= 0
                    && selectedIndex < items.ItemCount;

                if (!hasValidIndex)
                {
                    throw new ArgumentException("Selection requires an integer index within the item collection.");
                }

                items.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, request.Value!.GetValue<int>());
                break;

            case "scroll":
                if (control is not ScrollViewer scrollViewer)
                {
                    return Unsupported(action);
                }

                var x = OptionalNumber(request.Options, "x", scrollViewer.Offset.X);
                var y = OptionalNumber(request.Options, "y", scrollViewer.Offset.Y);
                scrollViewer.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(x, y));
                break;

            default:
                return Unsupported(action);
        }

        return Completed(request);
    }

    private static ToolResult SetValue(AutomationPeer peer, VisualTreeActionRequest request)
    {
        if (peer.GetProvider<IValueProvider>() is { } textValue)
        {
            if (textValue.IsReadOnly)
            {
                return ToolResult.Failure("The control is read-only.", "avalonia_element_read_only");
            }

            if (request.Value is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                throw new ArgumentException("This control requires a string value.");
            }

            textValue.SetValue(text);
            return Completed(request);
        }

        if (peer.GetProvider<IRangeValueProvider>() is { } rangeValue)
        {
            if (rangeValue.IsReadOnly)
            {
                return ToolResult.Failure("The control is read-only.", "avalonia_element_read_only");
            }

            var value = RequireNumber(request.Value);
            var isInRange = double.IsFinite(value)
                && value >= rangeValue.Minimum
                && value <= rangeValue.Maximum;

            if (!isInRange)
            {
                throw new ArgumentException("The numeric value is outside the control's range.");
            }

            rangeValue.SetValue(value);
            return Completed(request);
        }

        return Unsupported(request.Action);
    }

    private static bool RequireBoolean(JsonNode value)
    {
        return value is JsonValue scalar && scalar.TryGetValue<bool>(out var boolean)
            ? boolean
            : throw new ArgumentException("The toggle value must be a boolean.");
    }

    private static double RequireNumber(JsonNode? value)
    {
        if (value is not JsonValue scalar)
        {
            throw new ArgumentException("The value must be a finite number.");
        }

        // Parsing the JSON numeric representation also accepts integer JsonValue instances.
        var text = scalar.TryGetValue<string>(out var stringValue) ? stringValue : scalar.ToJsonString();
        var isFiniteNumber = double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number);

        return isFiniteNumber ? number : throw new ArgumentException("The value must be a finite number.");
    }

    private static double OptionalNumber(JsonObject options, string key, double fallback)
    {
        return options[key] is { } value ? RequireNumber(value) : fallback;
    }

    private static ToolResult Completed(VisualTreeActionRequest request)
    {
        return ToolResult.Success(new JsonObject
        {
            ["nodeId"] = request.NodeId,
            ["action"] = request.Action,
            ["executionMode"] = "avalonia-semantic-action",
            ["dispatched"] = true
        });
    }

    private static ToolResult Unsupported(string action)
    {
        return ToolResult.Failure($"This control does not support the Avalonia action '{action}'. Use device input for pointer gestures.", "avalonia_action_not_supported");
    }
}
