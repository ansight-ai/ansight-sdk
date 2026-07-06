# ansight-tools-visualtree-android

Android visual tree, screenshot, and diagnostic overlay tools.

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

Read tools are available with `AnsightToolGuard.ReadOnly`. Overlay mutation
requires `ReadWrite`; overlay removal and clearing require `FullAccess`.

> **Important:** Calling screenshot tools will result in an FPS drop while the
> current frame is captured, encoded, and transferred. Avoid screenshot-heavy
> investigations during performance measurements unless visual evidence is
> required.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.visualtree.AndroidVisualTreeTools

val options = AnsightOptions(
    initialTools = AndroidVisualTreeTools.create(),
    toolGuard = AnsightToolGuard.FullAccess,
)
```

The standard native provider is registered by the tool suite. Custom providers
can be registered by source:

```kotlin
AndroidVisualTreeProviderRegistry.register(myProvider, replaceExisting = true)
```
