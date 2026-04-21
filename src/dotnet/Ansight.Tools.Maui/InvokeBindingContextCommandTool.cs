namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class InvokeBindingContextCommandTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => MauiToolIds.InvokeBindingContextCommand;

    public string Name => "Invoke MAUI Binding Context Command";

    public string Description => "Invokes a public ICommand property on a MAUI element binding-context object.";

    public string Keywords => "maui binding context command icommand invoke viewmodel";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.InvokeBindingContextCommandArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindingContextCommandResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.InvokeBindingContextCommand;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var commandName = GetRequiredString(arguments, "commandName");
            var parameterJson = GetString(arguments, "parameterJson");
            var requireCanExecute = GetBoolean(arguments, "requireCanExecute", defaultValue: true);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var bindingContext = resolution.Element.BindingContext;
            if (bindingContext == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' does not have a binding context.", errorCode: "maui_binding_context_unavailable");
            }

            if (!TryResolveCommand(bindingContext, commandName, out var command, out var discoveredParameter, out var matchedPropertyName) || command == null)
            {
                return ToolResult.Failure($"The command '{commandName}' was not found on the binding context.", errorCode: "maui_binding_context_command_not_found");
            }

            var parameter = parameterJson == null ? discoveredParameter : ConvertJsonArgumentToUntyped(parameterJson);
            if (!TryExecuteCommand(command, parameter, requireCanExecute, out var error))
            {
                return ToolResult.Failure(error ?? "The binding-context command could not be executed.", errorCode: "maui_binding_context_command_invoke_failed");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["bindingContextType"] = CreateTypeMetadata(bindingContext.GetType()),
                ["commandProperty"] = matchedPropertyName,
                ["invoked"] = true,
                ["parameter"] = CreateValueSnapshot(parameter, parameter?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
