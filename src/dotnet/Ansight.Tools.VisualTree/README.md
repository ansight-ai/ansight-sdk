# Ansight.Tools.VisualTree

Grouped visual tree and screenshot tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[PolyForm Shield 1.0.0](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It permits use, modification and redistribution for purposes allowed by the
[licence](https://polyformproject.org/licenses/shield/1.0.0), which excludes providing competing products.
Local development features require no Ansight account or subscription.

## Usage

```csharp
using Ansight;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadOnlyToolAccess()
    .Build();
```

`WithReadOnlyToolAccess()` exposes inspection tools such as `ui.get_visual_tree`, `ui.query_nodes`, `ui.inspect_node`, `ui.wait`, `ui.get_screenshot`, `ui.get_overlay`, and `ui.query_overlays`. `ui.query_nodes` returns snapshot-scoped references with `source`, `snapshotId`, `revision`, and `nodeId`. A superseded reference fails explicitly with `stale_node_reference`.

Visual-tree capture is provider-based. `VisualTreeProviderRegistry` always exposes the platform hierarchy as the `native` source, and additional UI frameworks can register independent sources. `Ansight.Tools.Maui` registers `maui`. Pass `source` to `ui.get_visual_tree` or `ui.inspect_node` to select one; omitting it preserves the native behavior. Local features such as `Ansight.Annotations` query the same registry and can capture every registered source without invoking remote tools.

> **Important:** Calling screenshot tools will result in an FPS drop while the
> current frame is captured, encoded, and transferred. Avoid screenshot-heavy
> investigations during performance measurements unless visual evidence is
> required.

Use `WithReadWriteToolAccess()` or a custom `ToolGuard` when a connected host should be allowed to invoke `ui.perform_action` or draw diagnostic overlays:

```csharp
var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadWriteToolAccess()
    .Build();
```

Overlay tools render input-transparent highlight rectangles over the active native window. They never participate in hit testing, so they should not intercept taps, clicks, focus, or gestures.

`ui.dismiss_keyboard` requires write access and dismisses the foreground app's keyboard through Android's input-method service or UIKit's editing API. It never injects a Back key. Its `requested` result describes the platform request; the host verifies keyboard visibility using fresh evidence afterward. Hosts refuse unsafe Back-key fallback when this tool is unavailable, so update the app SDK to use safe keyboard dismissal.

- `ui.show_overlay` creates one or more highlight rectangles from explicit window coordinates, visual-tree coordinates, or a `nodeId`.
- `ui.update_overlay` edits an existing overlay by id. Omitted fields keep their current values; supplied geometry replaces the current rectangles.
- `ui.get_overlay` returns a single overlay by id.
- `ui.query_overlays` lists overlays and can filter by metadata key/value.
- `ui.remove_overlay` removes one overlay by id.
- `ui.clear_overlays` removes all overlays, or only overlays matching a metadata key/value filter.

Overlays default to no fill, a red stroke, and a 5 second lifetime. Pass `durationMs=0` for an overlay that remains until removed. `fillColor=none` or `fillColor=transparent` clears fill. Each overlay can carry a small scalar `metadata` dictionary so clients can record why the overlay exists, for example a target node id, assertion name, or investigation step.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.


`ui.submit_keyboard` requires write access and a focused editor. Android dispatches the
configured IME action; UIKit dispatches the text field's Return callback and its default
editing action when permitted. Unsupported or newline-only editors fail explicitly.
Use `ansight.keyboard.submit()` in tasks or `ansight keyboard submit --session <id>`.
For a literal Enter key, use `ansight.keyboard.press({ key: "Enter" })` or
`ansight keyboard press --key Enter --session <id>`. Assert the resulting app state.
