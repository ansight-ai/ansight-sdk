namespace Ansight.Tools.Maui;

public static class MauiToolSecurityProfiles
{
    public static ToolSecurity GetCurrentPage { get; } = new(
        ToolSecurityLevel.High,
        "Reveals the currently displayed MAUI page and navigation metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity GetVisualTree { get; } = new(
        ToolSecurityLevel.High,
        "Reveals the live MAUI visual tree, including element identifiers and PII-safe labels.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity FindElements { get; } = new(
        ToolSecurityLevel.High,
        "Searches the live MAUI visual tree and can reveal element identifiers, PII-safe labels, and layout metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity GetElement { get; } = new(
        ToolSecurityLevel.High,
        "Reveals detailed metadata for one MAUI element, including ancestors, children, bindable properties, and binding-context type.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity GetBindableProperty { get; } = new(
        ToolSecurityLevel.High,
        "Reads live MAUI bindable property values from UI elements.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.InspectsRuntimeState,
        ToolSecurityImplications.ReadsAppData);

    public static ToolSecurity SetBindableProperty { get; } = new(
        ToolSecurityLevel.Critical,
        "Mutates live MAUI bindable property values on UI elements.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity ClearBindableProperty { get; } = new(
        ToolSecurityLevel.Critical,
        "Clears local MAUI bindable property values or bindings on live UI elements.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity GetBindingContext { get; } = new(
        ToolSecurityLevel.Critical,
        "Reveals live MAUI binding-context objects and selected runtime state.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity GetBindings { get; } = new(
        ToolSecurityLevel.Critical,
        "Reveals active MAUI binding expressions, binding sources, and selected target values.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsRuntimeState,
        ToolSecurityImplications.ReadsAppData);

    public static ToolSecurity GetResourceState { get; } = new(
        ToolSecurityLevel.High,
        "Reveals MAUI resource dictionary keys, merged dictionaries, value types, and explicitly requested values.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity GetNavigationState { get; } = new(
        ToolSecurityLevel.High,
        "Reveals MAUI window, page, navigation stack, modal stack, and Shell navigation metadata.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity InvokeElementAction { get; } = new(
        ToolSecurityLevel.Critical,
        "Invokes user-like actions or commands on live MAUI elements.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.MutatesRuntimeState,
        ToolSecurityImplications.InvokesAppCode);

    public static ToolSecurity WaitForUi { get; } = new(
        ToolSecurityLevel.High,
        "Polls live MAUI UI state until an element, page, property, or binding-context condition is met.",
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity GetLayoutDiagnostics { get; } = new(
        ToolSecurityLevel.High,
        "Reveals MAUI layout measurements, attached layout values, visibility, and input-related diagnostics.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi);

    public static ToolSecurity GetHandlerDiagnostics { get; } = new(
        ToolSecurityLevel.High,
        "Reveals MAUI handler and platform-view metadata for a live UI element.",
        ToolSecurityImplications.MetadataDisclosure,
        ToolSecurityImplications.InspectsUi,
        ToolSecurityImplications.InspectsRuntimeState);

    public static ToolSecurity InvokeBindingContextCommand { get; } = new(
        ToolSecurityLevel.Critical,
        "Invokes ICommand members on live MAUI binding-context objects.",
        ToolSecurityImplications.InspectsRuntimeState,
        ToolSecurityImplications.InvokesAppCode,
        ToolSecurityImplications.MutatesRuntimeState);

    public static ToolSecurity SetBindingContextProperty { get; } = new(
        ToolSecurityLevel.Critical,
        "Mutates writable public properties on live MAUI binding-context objects.",
        ToolSecurityImplications.InspectsRuntimeState,
        ToolSecurityImplications.MutatesRuntimeState,
        ToolSecurityImplications.WritesAppData);
}
