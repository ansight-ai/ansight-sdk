# Ansight.Tools.VisualTree

Grouped visual tree and screenshot tool registrations for the Ansight .NET SDK.

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
