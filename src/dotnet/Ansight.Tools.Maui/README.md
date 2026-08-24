# Ansight.Tools.Maui

Grouped .NET MAUI inspection and mutation tools for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

The visual tree and search tools return MAUI element ids that can be passed to the element, bindable-property, binding, layout, handler, action, wait, XAML experiment, theme, and binding-context tools. Ordinary state changes use `ToolPolicy.Write`; sensitive or code-invoking operations use `ToolPolicy.Critical` and require a guard with the matching maximum policy.

Custom controls that render their own child model, such as Mapbox, Skia, or native-hosted controls, can register visual-tree extensions. A child walker exposes additional real MAUI `Element` children to search and node resolution. A child builder exposes synthetic visual-tree nodes for drawn/native items that are not MAUI elements:

```csharp
using System.Text.Json.Nodes;
using Ansight.Tools.Maui;
using Microsoft.Maui.Graphics;

var registration = MauiVisualTreeRegistry.RegisterChildBuilder<MyMapView>((mapView, context) =>
    mapView.VisibleMarkers.Select(marker => new MauiVisualTreeNode(
        context.CreateChildId(marker.Id),
        "MyApp.MapMarker")
    {
        Label = marker.Title,
        ZIndex = marker.ZIndex,
        Bounds = new Rect(marker.X, marker.Y, marker.Width, marker.Height),
        Properties = new JsonObject
        {
            ["layer"] = marker.LayerName,
            ["selected"] = marker.IsSelected
        }
    }));
```

Keep the returned `IDisposable` for the lifetime of the registration and dispose it to unregister.
Synthetic nodes appear in `maui.get_visual_tree`; only real `Element` children returned by a child walker can be resolved later by element mutation or property tools.

`maui.inflate_xaml` creates and retains a detached MAUI element from an arbitrary XAML string using `LoadFromXaml`. The returned node id can be passed to `maui.add_element` to attach it under a live layout or content control, and `maui.remove_element` can detach it again without restarting the app.

`maui.set_app_theme` changes `Application.Current.UserAppTheme` to `light`, `dark`, or `system` at runtime.

These tools are intended for local debugging only. Broad visual-tree labels are PII-safe by default: typed `Entry`, `Editor`, and `SearchBar` text, picker selections, dates, times, toggle states, slider values, and sensitive-looking text are omitted or redacted. Resource values and binding-context property snapshots are opt-in; by default resource inspection returns keys/types and binding-context inspection returns metadata only. Explicit value-reading tools can still reveal app data, binding expressions, binding-context object state, handler metadata, and mutate live UI, app theme, or view-model state.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
