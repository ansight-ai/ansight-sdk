# AnsightToolsVisualTree

UIKit visual tree, screenshot, and diagnostic overlay tools.

## Tools

- `ui.get_visual_tree`
- `ui.get_screenshot`
- `ui.inspect_node`
- `ui.show_overlay`
- `ui.get_overlay`
- `ui.query_overlays`
- `ui.update_overlay`
- `ui.remove_overlay`
- `ui.clear_overlays`

Read tools are available with `.readOnly`. Overlay mutation requires
`.readWrite`; overlay removal and clearing require `.fullAccess`.

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
