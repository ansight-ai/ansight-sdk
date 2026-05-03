# Ansight.Tools.Maui

Grouped .NET MAUI inspection and mutation tools for the Ansight .NET SDK.

Registered tools:

- `maui.get_current_page`
- `maui.get_visual_tree`
- `maui.find_elements`
- `maui.get_element`
- `maui.get_bindable_property`
- `maui.set_bindable_property`
- `maui.clear_bindable_property`
- `maui.inflate_xaml`
- `maui.add_element`
- `maui.remove_element`
- `maui.set_app_theme`
- `maui.get_binding_context`
- `maui.get_bindings`
- `maui.get_resource_state`
- `maui.get_navigation_state`
- `maui.invoke_element_action`
- `maui.wait_for_ui`
- `maui.get_layout_diagnostics`
- `maui.get_handler_diagnostics`
- `maui.invoke_binding_context_command`
- `maui.set_binding_context_property`

## Usage

```csharp
using Ansight;
using Ansight.Tools.Maui;

var options = Options.CreateBuilder()
    .WithMauiTools()
    .WithReadWriteToolAccess()
    .Build();
```

The visual tree and search tools return MAUI element ids that can be passed to the element, bindable-property, binding, layout, handler, action, wait, XAML experiment, theme, and binding-context tools. Write-scoped tools require `WithReadWriteToolAccess()` or a custom `ToolGuard`.

`maui.inflate_xaml` creates and retains a detached MAUI element from an arbitrary XAML string using `LoadFromXaml`. The returned node id can be passed to `maui.add_element` to attach it under a live layout or content control, and `maui.remove_element` can detach it again without restarting the app.

`maui.set_app_theme` changes `Application.Current.UserAppTheme` to `light`, `dark`, or `system` at runtime.

These tools are intended for local debugging only. Broad visual-tree labels are PII-safe by default: typed `Entry`, `Editor`, and `SearchBar` text, picker selections, dates, times, toggle states, slider values, and sensitive-looking text are omitted or redacted. Resource values and binding-context property snapshots are opt-in; by default resource inspection returns keys/types and binding-context inspection returns metadata only. Explicit value-reading tools can still reveal app data, binding expressions, binding-context object state, handler metadata, and mutate live UI, app theme, or view-model state.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
