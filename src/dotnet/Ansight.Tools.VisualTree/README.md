# Ansight.Tools.VisualTree

Grouped visual tree and screenshot tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Usage

```csharp
using Ansight;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadOnlyToolAccess()
    .Build();
```

`WithReadOnlyToolAccess()` exposes inspection tools such as `ui.get_visual_tree`, `ui.inspect_node`, `ui.get_screenshot`, `ui.get_overlay`, and `ui.query_overlays`.

Visual-tree capture is provider-based. `VisualTreeProviderRegistry` always exposes the platform hierarchy as the `native` source, and additional UI frameworks can register independent sources. `Ansight.Tools.Maui` registers `maui`. Pass `source` to `ui.get_visual_tree` or `ui.inspect_node` to select one; omitting it preserves the native behavior. Local features such as `Ansight.Annotations` query the same registry and can capture every registered source without invoking remote tools.

> **Important:** Calling screenshot tools will result in an FPS drop while the
> current frame is captured, encoded, and transferred. Avoid screenshot-heavy
> investigations during performance measurements unless visual evidence is
> required.

Use `WithReadWriteToolAccess()` or a custom `ToolGuard` when a connected host should be allowed to draw diagnostic overlays:

```csharp
var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadWriteToolAccess()
    .Build();
```

Overlay tools render input-transparent highlight rectangles over the active native window. They never participate in hit testing, so they should not intercept taps, clicks, focus, or gestures.

- `ui.show_overlay` creates one or more highlight rectangles from explicit window coordinates, visual-tree coordinates, or a `nodeId`.
- `ui.update_overlay` edits an existing overlay by id. Omitted fields keep their current values; supplied geometry replaces the current rectangles.
- `ui.get_overlay` returns a single overlay by id.
- `ui.query_overlays` lists overlays and can filter by metadata key/value.
- `ui.remove_overlay` removes one overlay by id.
- `ui.clear_overlays` removes all overlays, or only overlays matching a metadata key/value filter.

Overlays default to no fill, a red stroke, and a 5 second lifetime. Pass `durationMs=0` for an overlay that remains until removed. `fillColor=none` or `fillColor=transparent` clears fill. Each overlay can carry a small scalar `metadata` dictionary so MCP clients can record why the overlay exists, for example a target node id, assertion name, or investigation step.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
