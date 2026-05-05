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

Overlay tools such as `ui.show_overlay`, `ui.update_overlay`, `ui.query_overlays`, `ui.get_overlay`, `ui.remove_overlay`, and `ui.clear_overlays` require write-capable tool access when they mutate the live window by adding, editing, or removing input-transparent diagnostic highlights. Overlays default to no fill, a red stroke, and a 5 second lifetime unless `durationMs` is supplied. Pass `durationMs=0` for an overlay that remains until removed.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
