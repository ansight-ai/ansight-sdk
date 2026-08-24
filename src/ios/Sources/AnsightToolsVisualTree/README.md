# AnsightToolsVisualTree

UIKit visual tree, screenshot, and diagnostic overlay tools.

## Tools

- `ui.get_visual_tree`
- `ui.query_nodes`
- `ui.perform_action`
- `ui.wait`
- `ui.get_screenshot`
- `ui.inspect_node`
- `ui.show_overlay`
- `ui.get_overlay`
- `ui.query_overlays`
- `ui.update_overlay`
- `ui.remove_overlay`
- `ui.clear_overlays`

Read tools are available with `.readOnly`. `ui.query_nodes` returns
snapshot-scoped references and stale references fail with
`stale_node_reference`. `ui.perform_action` and all overlay mutation require
`.readWrite`.

> **Important:** Calling screenshot tools will result in an FPS drop while the
> current frame is captured, encoded, and transferred. Avoid screenshot-heavy
> investigations during performance measurements unless visual evidence is
> required.

## Usage

```swift
import AnsightCore
import AnsightToolsVisualTree

try AnsightRuntime.shared.registerVisualTreeTools()
```

Custom providers can be registered by source:

```swift
try AnsightVisualTreeProviderRegistry.register(myProvider, replaceExisting: true)
```
