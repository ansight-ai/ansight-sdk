# Clipboard remote tools

Clipboard tools operate on the connected app device's system clipboard through the existing Ansight remote-tool transport. They do not access the developer workstation clipboard or require simctl, ADB, or an emulator service.

| Tool ID | Arguments | Successful result | Policy |
| --- | --- | --- | --- |
| `clipboard.get_text` | `{}` | `{ "text": string or null, "hasText": boolean }` | Read |
| `clipboard.has_text` | `{}` | `{ "hasText": boolean }` | Read |
| `clipboard.set_text` | `{ "text": string }` | `{ "updated": true }` | Write |
| `clipboard.clear` | `{}` | `{ "cleared": true }` | Write |

Text is preserved exactly, including whitespace, Unicode and empty strings. Reads and writes reject text larger than 65,536 UTF-8 bytes with `clipboard_text_too_large`; contents are never truncated. A missing or non-string write argument is invalid. An empty string is text; it is not the same operation as clearing all clipboard items. Reads return null when no plain text is present. Rich content, images and URI coercion are outside this API.

## Registration

The all-in-one SDK remote-tool suites include clipboard tools. Existing tool guards still apply; read-only connections cannot set or clear the clipboard.

- .NET/MAUI: reference `Ansight.Tools.Clipboard` and call `builder.WithClipboardTools()` for standalone registration. `WithAnsightSdk()` and `WithAnsightMaui()` include it.
- Swift: add the `AnsightToolsClipboard` SwiftPM product or CocoaPod, then call `try runtime.registerClipboardTools()`, or use `AnsightClipboardTools.tools()` when building a custom tool list. The `Ansight` aggregate includes it. Set `AnsightRemoteToolOptions(clipboard: false)` to omit it.
- Kotlin: add `ai.ansight:ansight-tools-clipboard-android` and call `builder.withClipboardTools()`. The `ansight-android` aggregate includes it.
- React Native and Capacitor: the native bridge registers clipboard by default. Use `withClipboardTools(false)` or `remoteTools: { clipboard: false }` to omit the suite.
- Flutter: clipboard is included in native remote tools by default. Use `AnsightRemoteToolsOptions(clipboard: false)` to omit it.

## TypeScript tasks and triggers

The Ansight host exposes the same tools as `app.clipboard`:

```ts
await app.clipboard.setText({ text: "  Hello 世界\n" });
const response = await app.clipboard.getText();
if (response.responseType === "tool.result") {
  expect(response.payload.result.text, { id: "clipboard.text" })
    .toBe("  Hello 世界\n");
}
await app.clipboard.clear();
```

Task methods return the normal `AppToolCallResult<T>` envelope and use the task's fixed session. Trigger methods construct a declarative `AppToolAction` to return from the trigger. Host and app SDKs must both contain clipboard support; installing a new host alone does not add tools to an older app.

## Platform behaviour

- Android: native ClipboardManager. The app window must have input focus for all operations. No focus returns `clipboard_not_focused`. True clearing requires Android API 28 or later; older versions return `clipboard_clear_unsupported`. Presence checks inspect advertised text types without fetching content.
- iOS / Mac Catalyst: native UIPasteboard on the main thread. The app must be active. Reading content can trigger the OS paste-permission flow. A pasteboard advertising text but refusing a read returns `clipboard_unavailable`; this does not claim to identify a specific denial reason. Presence checks use `hasStrings`.
- macOS (Swift): native NSPasteboard on the main thread.
- The generic .NET target is available for compilation and testing but returns `clipboard_platform_unsupported` at runtime. Browser-only framework runtimes do not acquire a native clipboard implementation from this package.

The tool schemas distinguish required nullable values from absent values. Empty clipboard responses include the `text` key with null. Clipboard operations are explicit; the SDK does not monitor, synchronize or retain clipboard history. The host redacts read/write text in task call diagnostic payloads. Authors should avoid placing sensitive clipboard contents in task outputs, assertions or source literals if those artifacts are retained.
