namespace Ansight.Tools.Maui;

using Ansight.Tools;

internal static class MauiToolSchemas
{
    private static readonly ToolSchema GenericObjectSchema = ToolSchema.Object(
        description: "Arbitrary object with implementation-specific fields.",
        additionalProperties: true,
        nullable: true);

    private static readonly ToolSchema TypeMetadataSchema = ToolSchema.Object(
        description: "Runtime type metadata.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Short type name."),
            ["fullName"] = ToolSchema.String("Fully-qualified type name."),
            ["namespace"] = ToolSchema.String("Type namespace.", nullable: true),
            ["assemblyName"] = ToolSchema.String("Assembly simple name.")
        },
        required: new[] { "name", "fullName", "assemblyName" },
        nullable: true);

    private static readonly ToolSchema BoundsSchema = ToolSchema.Object(
        description: "MAUI layout bounds for an element.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["x"] = ToolSchema.Number("Horizontal origin relative to the parent."),
            ["y"] = ToolSchema.Number("Vertical origin relative to the parent."),
            ["width"] = ToolSchema.Number("Element width."),
            ["height"] = ToolSchema.Number("Element height.")
        },
        required: new[] { "x", "y", "width", "height" },
        nullable: true);

    private static readonly ToolSchema MauiElementReferenceSchema = ToolSchema.Object(
        description: "Reference to a MAUI element.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Stable node id for the element."),
            ["type"] = ToolSchema.String("Element runtime type."),
            ["kind"] = ToolSchema.String("Broad MAUI element kind."),
            ["automationId"] = ToolSchema.String("MAUI AutomationId, when present.", nullable: true),
            ["styleId"] = ToolSchema.String("MAUI StyleId, when present.", nullable: true),
            ["classId"] = ToolSchema.String("MAUI ClassId, when present.", nullable: true),
            ["label"] = ToolSchema.String("Best-effort PII-safe visible or semantic label. Typed input values and sensitive-looking text are omitted or redacted.", nullable: true),
            ["title"] = ToolSchema.String("PII-safe page title, when present.", nullable: true)
        },
        required: new[] { "id", "type", "kind" });

    private static readonly ToolSchema MauiElementNodeSchema = ToolSchema.Object(
        description: "A MAUI visual tree node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Stable node id for the element."),
            ["type"] = ToolSchema.String("Element runtime type."),
            ["kind"] = ToolSchema.String("Broad MAUI element kind."),
            ["automationId"] = ToolSchema.String("MAUI AutomationId, when present.", nullable: true),
            ["styleId"] = ToolSchema.String("MAUI StyleId, when present.", nullable: true),
            ["classId"] = ToolSchema.String("MAUI ClassId, when present.", nullable: true),
            ["label"] = ToolSchema.String("Best-effort PII-safe visible or semantic label. Typed input values and sensitive-looking text are omitted or redacted.", nullable: true),
            ["title"] = ToolSchema.String("PII-safe page title, when present.", nullable: true),
            ["visible"] = ToolSchema.Boolean("Whether the element is visible.", nullable: true),
            ["enabled"] = ToolSchema.Boolean("Whether the element is enabled.", nullable: true),
            ["childCount"] = ToolSchema.Integer("Number of direct visual children."),
            ["bounds"] = BoundsSchema,
            ["bindingContextType"] = TypeMetadataSchema,
            ["properties"] = GenericObjectSchema,
            ["bindableProperties"] = ToolSchema.Array(GenericObjectSchema, "Bindable property metadata for this element.", nullable: true),
            ["children"] = ToolSchema.Array(GenericObjectSchema, "Nested child nodes.", nullable: true)
        },
        required: new[] { "id", "type", "kind", "childCount" });

    private static readonly ToolSchema ValueSnapshotSchema = ToolSchema.Object(
        description: "A bounded runtime value snapshot.",
        additionalProperties: true,
        nullable: true);

    private static readonly ToolSchema BindablePropertyMetadataSchema = ToolSchema.Object(
        description: "Bindable property metadata.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Bindable property name."),
            ["memberName"] = ToolSchema.String("Static member name that declares the bindable property."),
            ["declaringType"] = TypeMetadataSchema,
            ["valueType"] = TypeMetadataSchema,
            ["defaultBindingMode"] = ToolSchema.String("Default binding mode."),
            ["isSet"] = ToolSchema.Boolean("Whether this bindable property has a local value set."),
            ["hasBinding"] = ToolSchema.Boolean("Whether this bindable property has an active binding.")
        },
        required: new[] { "name", "memberName", "declaringType", "valueType", "defaultBindingMode", "isSet", "hasBinding" });

    internal static ToolSchema GetCurrentPageArguments { get; } = ToolSchema.Object(
        description: "Arguments for querying the current MAUI page.");

    internal static ToolSchema CurrentPageResult { get; } = ToolSchema.Object(
        description: "Current MAUI page payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["windowCount"] = ToolSchema.Integer("Number of known MAUI windows."),
            ["window"] = MauiElementReferenceSchema,
            ["rootPage"] = MauiElementReferenceSchema,
            ["currentPage"] = MauiElementReferenceSchema,
            ["navigation"] = GenericObjectSchema
        },
        required: new[] { "platform", "capturedAtUtc", "windowCount", "window", "rootPage", "currentPage" });

    internal static ToolSchema GetVisualTreeArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving the current MAUI visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String(
                "Capture root.",
                enumValues: new[] { "currentPage", "rootPage", "window" }),
            ["rootNodeId"] = ToolSchema.String("Optional node id to use as the subtree root.", nullable: true),
            ["includeBounds"] = ToolSchema.Boolean("Include MAUI layout bounds in the result."),
            ["includeProperties"] = ToolSchema.Boolean("Include common MAUI element properties."),
            ["includeBindableProperties"] = ToolSchema.Boolean("Include bindable property metadata for each node."),
            ["includeBindingContexts"] = ToolSchema.Boolean("Include binding-context type metadata for each node."),
            ["maxDepth"] = ToolSchema.Integer("Maximum child depth to include.")
        });

    internal static ToolSchema FindElementsArguments { get; } = ToolSchema.Object(
        description: "Arguments for searching the current MAUI visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Capture root.", enumValues: new[] { "currentPage", "rootPage", "window" }),
            ["rootNodeId"] = ToolSchema.String("Optional node id to use as the subtree root.", nullable: true),
            ["nodeId"] = ToolSchema.String("Optional exact node id or AutomationId to match.", nullable: true),
            ["automationId"] = ToolSchema.String("Optional AutomationId to match.", nullable: true),
            ["styleId"] = ToolSchema.String("Optional StyleId to match.", nullable: true),
            ["classId"] = ToolSchema.String("Optional ClassId to match.", nullable: true),
            ["typeName"] = ToolSchema.String("Optional runtime type name or fully-qualified type name to match.", nullable: true),
            ["kind"] = ToolSchema.String("Optional broad MAUI element kind to match.", nullable: true),
            ["labelContains"] = ToolSchema.String("Optional case-insensitive label/title/text substring to match.", nullable: true),
            ["bindingContextTypeName"] = ToolSchema.String("Optional binding-context type name to match.", nullable: true),
            ["visible"] = ToolSchema.Boolean("Optional visible-state filter.", nullable: true),
            ["enabled"] = ToolSchema.Boolean("Optional enabled-state filter.", nullable: true),
            ["propertyName"] = ToolSchema.String("Optional bindable property that must exist on the element.", nullable: true),
            ["propertyValueJson"] = ToolSchema.String("Optional JSON value that the bindable property must equal.", nullable: true),
            ["includeBounds"] = ToolSchema.Boolean("Include bounds in each match."),
            ["includeProperties"] = ToolSchema.Boolean("Include common element properties in each match."),
            ["maxDepth"] = ToolSchema.Integer("Maximum child depth to search."),
            ["maxResults"] = ToolSchema.Integer("Maximum matches to return.")
        });

    internal static ToolSchema FindElementsResult { get; } = ToolSchema.Object(
        description: "MAUI visual tree search payload.",
        additionalProperties: true);

    internal static ToolSchema GetElementArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving one focused MAUI element snapshot.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["includeBounds"] = ToolSchema.Boolean("Include MAUI layout bounds."),
            ["includeProperties"] = ToolSchema.Boolean("Include common MAUI element properties."),
            ["includeBindableProperties"] = ToolSchema.Boolean("Include bindable property metadata."),
            ["includeBindingContext"] = ToolSchema.Boolean("Include binding-context metadata."),
            ["includeChildren"] = ToolSchema.Boolean("Include direct child references.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema ElementResult { get; } = ToolSchema.Object(
        description: "Focused MAUI element payload.",
        additionalProperties: true);

    internal static ToolSchema VisualTreeResult { get; } = ToolSchema.Object(
        description: "MAUI visual tree payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["rootScope"] = ToolSchema.String("Requested capture root."),
            ["root"] = MauiElementNodeSchema
        },
        required: new[] { "platform", "capturedAtUtc", "rootScope", "root" });

    internal static ToolSchema GetBindablePropertyArguments { get; } = ToolSchema.Object(
        description: "Arguments for reading a MAUI bindable property.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["propertyName"] = ToolSchema.String("Bindable property name, such as Text or TextProperty."),
            ["declaringTypeName"] = ToolSchema.String("Optional declaring type name to disambiguate duplicate property names.", nullable: true)
        },
        required: new[] { "nodeId", "propertyName" });

    internal static ToolSchema SetBindablePropertyArguments { get; } = ToolSchema.Object(
        description: "Arguments for writing a MAUI bindable property.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["propertyName"] = ToolSchema.String("Bindable property name, such as Text or TextProperty."),
            ["declaringTypeName"] = ToolSchema.String("Optional declaring type name to disambiguate duplicate property names.", nullable: true),
            ["valueJson"] = ToolSchema.String("New value encoded as JSON. Unquoted scalar text is accepted for string-like properties.")
        },
        required: new[] { "nodeId", "propertyName", "valueJson" });

    internal static ToolSchema ClearBindablePropertyArguments { get; } = ToolSchema.Object(
        description: "Arguments for clearing a MAUI bindable property local value or binding.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["propertyName"] = ToolSchema.String("Bindable property name, such as Text or TextProperty."),
            ["declaringTypeName"] = ToolSchema.String("Optional declaring type name to disambiguate duplicate property names.", nullable: true),
            ["mode"] = ToolSchema.String("Clear operation.", enumValues: new[] { "value", "binding", "both" })
        },
        required: new[] { "nodeId", "propertyName" });

    internal static ToolSchema BindablePropertyResult { get; } = ToolSchema.Object(
        description: "MAUI bindable property read payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["node"] = MauiElementReferenceSchema,
            ["property"] = BindablePropertyMetadataSchema,
            ["binding"] = GenericObjectSchema,
            ["value"] = ValueSnapshotSchema
        },
        required: new[] { "platform", "capturedAtUtc", "node", "property", "value" });

    internal static ToolSchema BindablePropertyMutationResult { get; } = ToolSchema.Object(
        description: "MAUI bindable property mutation payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["node"] = MauiElementReferenceSchema,
            ["property"] = BindablePropertyMetadataSchema,
            ["binding"] = GenericObjectSchema,
            ["updated"] = ToolSchema.Boolean("Whether the mutation completed."),
            ["mode"] = ToolSchema.String("Clear operation mode, when returned.", nullable: true),
            ["hadBinding"] = ToolSchema.Boolean("Whether the property had a binding before a clear operation.", nullable: true),
            ["removedBinding"] = ToolSchema.Boolean("Whether a binding removal operation was invoked.", nullable: true),
            ["hasBinding"] = ToolSchema.Boolean("Whether the property has a binding after a clear operation.", nullable: true),
            ["hadLocalValue"] = ToolSchema.Boolean("Whether the property had a local value before a clear operation.", nullable: true),
            ["clearedValue"] = ToolSchema.Boolean("Whether a local value clear operation was invoked.", nullable: true),
            ["value"] = ValueSnapshotSchema
        },
        required: new[] { "platform", "capturedAtUtc", "node", "property", "updated", "value" });

    internal static ToolSchema InflateXamlArguments { get; } = ToolSchema.Object(
        description: "Arguments for inflating a MAUI control from XAML.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["xaml"] = ToolSchema.String("XAML markup to inflate with LoadFromXaml."),
            ["rootTypeName"] = ToolSchema.String("Optional CLR type name to instantiate before LoadFromXaml. When omitted, the root XML element is resolved from MAUI or clr-namespace XML namespaces.", nullable: true)
        },
        required: new[] { "xaml" });

    internal static ToolSchema InflateXamlResult { get; } = ToolSchema.Object(
        description: "Inflated MAUI XAML element payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["node"] = MauiElementReferenceSchema,
            ["rootType"] = TypeMetadataSchema,
            ["registered"] = ToolSchema.Boolean("Whether the element was retained for later add/remove calls."),
            ["childCount"] = ToolSchema.Integer("Number of direct visual children.")
        },
        required: new[] { "platform", "capturedAtUtc", "node", "rootType", "registered", "childCount" });

    internal static ToolSchema AddElementArguments { get; } = ToolSchema.Object(
        description: "Arguments for adding a MAUI element to the live visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["parentNodeId"] = ToolSchema.String("Node id or AutomationId for the parent already in the live MAUI visual tree."),
            ["elementNodeId"] = ToolSchema.String("Node id returned by maui.inflate_xaml, a live visual-tree node id, or an AutomationId."),
            ["index"] = ToolSchema.Integer("Optional insertion index for layout children.", nullable: true),
            ["replaceContent"] = ToolSchema.Boolean("Replace an existing Content property value when adding to a content control."),
            ["detachFromCurrentParent"] = ToolSchema.Boolean("Detach the element from its current parent before adding it to the requested parent.")
        },
        required: new[] { "parentNodeId", "elementNodeId" });

    internal static ToolSchema RemoveElementArguments { get; } = ToolSchema.Object(
        description: "Arguments for removing a MAUI element from the live visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, maui.inflate_xaml, or an AutomationId."),
            ["forget"] = ToolSchema.Boolean("Forget a retained inflated element after detaching it.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema ElementTreeMutationResult { get; } = ToolSchema.Object(
        description: "MAUI visual-tree mutation payload.",
        additionalProperties: true);

    internal static ToolSchema GetBindingContextArguments { get; } = ToolSchema.Object(
        description: "Arguments for reading a MAUI element binding context.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["includeProperties"] = ToolSchema.Boolean("Include public property snapshots from the binding-context object. Defaults to false; enabling this can read app data."),
            ["maxDepth"] = ToolSchema.Integer("Maximum object graph depth to include."),
            ["maxProperties"] = ToolSchema.Integer("Maximum public properties to include per object.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema BindingContextResult { get; } = ToolSchema.Object(
        description: "MAUI binding-context payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["node"] = MauiElementReferenceSchema,
            ["hasBindingContext"] = ToolSchema.Boolean("Whether the node has a non-null effective BindingContext."),
            ["bindingContext"] = ValueSnapshotSchema
        },
        required: new[] { "platform", "capturedAtUtc", "node", "hasBindingContext", "bindingContext" });

    internal static ToolSchema GetBindingsArguments { get; } = ToolSchema.Object(
        description: "Arguments for enumerating MAUI bindings on an element.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["propertyName"] = ToolSchema.String("Optional bindable property name to limit the result.", nullable: true),
            ["includeUnbound"] = ToolSchema.Boolean("Include bindable properties without active bindings."),
            ["includeValues"] = ToolSchema.Boolean("Include current target property values."),
            ["maxProperties"] = ToolSchema.Integer("Maximum matching binding entries to return.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema BindingsResult { get; } = ToolSchema.Object(
        description: "MAUI binding diagnostic payload.",
        additionalProperties: true);

    internal static ToolSchema GetResourceStateArguments { get; } = ToolSchema.Object(
        description: "Arguments for inspecting MAUI resource dictionaries.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Optional node id returned by the MAUI visual tree, or an AutomationId.", nullable: true),
            ["scope"] = ToolSchema.String("Resource scope.", enumValues: new[] { "effective", "application", "window", "page", "element" }),
            ["includeValues"] = ToolSchema.Boolean("Include shallow value snapshots."),
            ["includeMergedDictionaries"] = ToolSchema.Boolean("Include merged dictionary summaries."),
            ["maxEntries"] = ToolSchema.Integer("Maximum entries per resource dictionary.")
        });

    internal static ToolSchema ResourceStateResult { get; } = ToolSchema.Object(
        description: "MAUI resource dictionary diagnostic payload.",
        additionalProperties: true);

    internal static ToolSchema GetNavigationStateArguments { get; } = ToolSchema.Object(
        description: "Arguments for inspecting MAUI navigation state.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["includeWindows"] = ToolSchema.Boolean("Include all application windows."),
            ["includeShellItems"] = ToolSchema.Boolean("Include Shell item, section, and content summaries.")
        });

    internal static ToolSchema NavigationStateResult { get; } = ToolSchema.Object(
        description: "MAUI navigation state payload.",
        additionalProperties: true);

    internal static ToolSchema InvokeElementActionArguments { get; } = ToolSchema.Object(
        description: "Arguments for invoking a controlled action on a MAUI element.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["action"] = ToolSchema.String("Action to invoke.", enumValues: new[] { "focus", "unfocus", "executeCommand", "invokeTap", "toggle", "setText", "selectPickerItem" }),
            ["commandName"] = ToolSchema.String("Command property name for executeCommand.", nullable: true),
            ["parameterJson"] = ToolSchema.String("Optional command parameter encoded as JSON.", nullable: true),
            ["valueJson"] = ToolSchema.String("Optional action value encoded as JSON.", nullable: true),
            ["requireCanExecute"] = ToolSchema.Boolean("Fail when a command returns false from CanExecute.")
        },
        required: new[] { "nodeId", "action" });

    internal static ToolSchema ElementActionResult { get; } = ToolSchema.Object(
        description: "MAUI element action payload.",
        additionalProperties: true);

    internal static ToolSchema WaitForUiArguments { get; } = ToolSchema.Object(
        description: "Arguments for waiting on a MAUI UI condition.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["condition"] = ToolSchema.String("Condition to evaluate.", enumValues: new[] { "elementExists", "elementVisible", "propertyEquals", "currentPage", "bindingContextPropertyEquals" }),
            ["timeoutMs"] = ToolSchema.Integer("Maximum wait time in milliseconds."),
            ["pollIntervalMs"] = ToolSchema.Integer("Polling interval in milliseconds."),
            ["nodeId"] = ToolSchema.String("Optional node id or AutomationId.", nullable: true),
            ["automationId"] = ToolSchema.String("Optional AutomationId filter.", nullable: true),
            ["typeName"] = ToolSchema.String("Optional element or page type name filter.", nullable: true),
            ["labelContains"] = ToolSchema.String("Optional label/title/text substring filter.", nullable: true),
            ["propertyName"] = ToolSchema.String("Bindable or binding-context property name for value conditions.", nullable: true),
            ["expectedJson"] = ToolSchema.String("Expected value encoded as JSON for value conditions.", nullable: true),
            ["root"] = ToolSchema.String("Capture root for element searches.", enumValues: new[] { "currentPage", "rootPage", "window" }),
            ["maxDepth"] = ToolSchema.Integer("Maximum child depth to search.")
        },
        required: new[] { "condition" });

    internal static ToolSchema WaitForUiResult { get; } = ToolSchema.Object(
        description: "MAUI UI wait result payload.",
        additionalProperties: true);

    internal static ToolSchema NodeOnlyArguments { get; } = ToolSchema.Object(
        description: "Arguments for a tool that targets one MAUI node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema GetHandlerDiagnosticsArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving MAUI handler diagnostics.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["includePlatformViewProperties"] = ToolSchema.Boolean("Include shallow public property snapshots from the native platform view."),
            ["maxProperties"] = ToolSchema.Integer("Maximum platform-view properties to include.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema DiagnosticsResult { get; } = ToolSchema.Object(
        description: "MAUI diagnostic payload.",
        additionalProperties: true);

    internal static ToolSchema InvokeBindingContextCommandArguments { get; } = ToolSchema.Object(
        description: "Arguments for invoking an ICommand on an element binding context.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["commandName"] = ToolSchema.String("Public ICommand property name. The Command suffix is optional."),
            ["parameterJson"] = ToolSchema.String("Optional command parameter encoded as JSON.", nullable: true),
            ["requireCanExecute"] = ToolSchema.Boolean("Fail when the command returns false from CanExecute.")
        },
        required: new[] { "nodeId", "commandName" });

    internal static ToolSchema BindingContextCommandResult { get; } = ToolSchema.Object(
        description: "MAUI binding-context command invocation payload.",
        additionalProperties: true);

    internal static ToolSchema SetBindingContextPropertyArguments { get; } = ToolSchema.Object(
        description: "Arguments for mutating a writable public property on an element binding context.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Node id returned by the MAUI visual tree, or an AutomationId."),
            ["propertyName"] = ToolSchema.String("Writable public binding-context property name."),
            ["valueJson"] = ToolSchema.String("New value encoded as JSON.")
        },
        required: new[] { "nodeId", "propertyName", "valueJson" });

    internal static ToolSchema BindingContextMutationResult { get; } = ToolSchema.Object(
        description: "MAUI binding-context property mutation payload.",
        additionalProperties: true);
}
