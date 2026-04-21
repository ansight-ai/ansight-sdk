namespace Ansight.Tools.Maui;

using System.Collections;
using System.Text.Json.Nodes;
using System.Windows.Input;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class InvokeElementActionTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => MauiToolIds.InvokeElementAction;

    public string Name => "Invoke Element Action";

    public string Description => "Invokes controlled user-like actions or commands on a node in the current .NET MAUI visual tree.";

    public string Keywords => "maui invoke action command tap focus toggle picker";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.InvokeElementActionArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.ElementActionResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.InvokeElementAction;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var action = GetRequiredString(arguments, "action");
            var commandName = GetString(arguments, "commandName") ?? "Command";
            var parameterJson = GetString(arguments, "parameterJson");
            var valueJson = GetString(arguments, "valueJson");
            var requireCanExecute = GetBoolean(arguments, "requireCanExecute", defaultValue: true);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var element = resolution.Element;
            var normalizedAction = action.Trim().ToLowerInvariant();
            var result = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(element),
                ["action"] = normalizedAction,
                ["invoked"] = false
            };

            switch (normalizedAction)
            {
                case "focus":
                    if (element is not VisualElement focusElement)
                    {
                        return ToolResult.Failure($"The MAUI node '{nodeId}' is not a VisualElement.", errorCode: "maui_action_not_supported");
                    }

                    result["invoked"] = focusElement.Focus();
                    return ToolResult.Success(result);

                case "unfocus":
                    if (element is not VisualElement unfocusElement)
                    {
                        return ToolResult.Failure($"The MAUI node '{nodeId}' is not a VisualElement.", errorCode: "maui_action_not_supported");
                    }

                    unfocusElement.Unfocus();
                    result["invoked"] = true;
                    return ToolResult.Success(result);

                case "executecommand":
                    if (!TryResolveCommand(element, commandName, out var command, out var discoveredParameter, out var matchedPropertyName) || command == null)
                    {
                        return ToolResult.Failure($"The command '{commandName}' was not found on node '{nodeId}'.", errorCode: "maui_command_not_found");
                    }

                    var parameter = parameterJson == null ? discoveredParameter : ConvertJsonArgumentToUntyped(parameterJson);
                    if (!TryExecuteCommand(command, parameter, requireCanExecute, out var commandError))
                    {
                        return ToolResult.Failure(commandError ?? "The command could not be executed.", errorCode: "maui_command_invoke_failed");
                    }

                    result["invoked"] = true;
                    result["commandProperty"] = matchedPropertyName;
                    result["parameter"] = CreateValueSnapshot(parameter, parameter?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
                    return ToolResult.Success(result);

                case "invoketap":
                    if (element is not View view)
                    {
                        return ToolResult.Failure($"The MAUI node '{nodeId}' is not a View.", errorCode: "maui_action_not_supported");
                    }

                    foreach (var recognizer in view.GestureRecognizers.OfType<TapGestureRecognizer>())
                    {
                        var tapCommand = recognizer.Command;
                        if (tapCommand == null)
                        {
                            continue;
                        }

                        var tapParameter = parameterJson == null ? recognizer.CommandParameter : ConvertJsonArgumentToUntyped(parameterJson);
                        if (!TryExecuteCommand(tapCommand, tapParameter, requireCanExecute, out var tapError))
                        {
                            return ToolResult.Failure(tapError ?? "The tap command could not be executed.", errorCode: "maui_command_invoke_failed");
                        }

                        result["invoked"] = true;
                        result["recognizerType"] = GetTypeDisplayName(recognizer.GetType());
                        result["parameter"] = CreateValueSnapshot(tapParameter, tapParameter?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
                        return ToolResult.Success(result);
                    }

                    return ToolResult.Failure($"No TapGestureRecognizer with a command was found on node '{nodeId}'.", errorCode: "maui_tap_command_not_found");

                case "toggle":
                    if (element is CheckBox checkBox)
                    {
                        checkBox.IsChecked = !checkBox.IsChecked;
                        result["invoked"] = true;
                        result["value"] = checkBox.IsChecked;
                        return ToolResult.Success(result);
                    }

                    if (element is Switch switchElement)
                    {
                        switchElement.IsToggled = !switchElement.IsToggled;
                        result["invoked"] = true;
                        result["value"] = switchElement.IsToggled;
                        return ToolResult.Success(result);
                    }

                    return ToolResult.Failure($"The MAUI node '{nodeId}' does not support toggle.", errorCode: "maui_action_not_supported");

                case "settext":
                    if (valueJson == null)
                    {
                        return ToolResult.Failure("The valueJson argument is required for setText.", errorCode: "maui_action_value_required");
                    }

                    if (!TrySetPublicPropertyFromJson(element, "Text", valueJson, out var updatedText, out var textError))
                    {
                        return ToolResult.Failure(textError ?? "The Text property could not be set.", errorCode: "maui_action_set_text_failed");
                    }

                    result["invoked"] = true;
                    result["value"] = CreateValueSnapshot(updatedText, updatedText?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
                    return ToolResult.Success(result);

                case "selectpickeritem":
                    if (element is not Picker picker)
                    {
                        return ToolResult.Failure($"The MAUI node '{nodeId}' is not a Picker.", errorCode: "maui_action_not_supported");
                    }

                    if (valueJson == null)
                    {
                        return ToolResult.Failure("The valueJson argument is required for selectPickerItem.", errorCode: "maui_action_value_required");
                    }

                    var requestedItem = ConvertJsonArgumentToUntyped(valueJson);
                    if (requestedItem is long requestedIndex)
                    {
                        if (requestedIndex < 0 || requestedIndex >= picker.Items.Count)
                        {
                            return ToolResult.Failure($"Picker index {requestedIndex} is out of range.", errorCode: "maui_picker_index_out_of_range");
                        }

                        picker.SelectedIndex = (int)requestedIndex;
                    }
                    else
                    {
                        var requestedText = Convert.ToString(requestedItem, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                        var selected = false;
                        for (var index = 0; index < picker.Items.Count; index++)
                        {
                            if (!string.Equals(picker.Items[index], requestedText, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            picker.SelectedIndex = index;
                            selected = true;
                            break;
                        }

                        if (!selected && picker.ItemsSource is IEnumerable itemsSource)
                        {
                            foreach (var item in itemsSource)
                            {
                                if (!string.Equals(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture), requestedText, StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                picker.SelectedItem = item;
                                selected = true;
                                break;
                            }
                        }

                        if (!selected)
                        {
                            return ToolResult.Failure($"Picker item '{requestedText}' was not found.", errorCode: "maui_picker_item_not_found");
                        }
                    }

                    result["invoked"] = true;
                    result["selectedIndex"] = picker.SelectedIndex;
                    result["selectedItem"] = CreateValueSnapshot(picker.SelectedItem, picker.SelectedItem?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
                    return ToolResult.Success(result);

                default:
                    return ToolResult.Failure("The action argument must be one of: focus, unfocus, executeCommand, invokeTap, toggle, setText, selectPickerItem.", errorCode: "maui_invalid_action");
            }
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
