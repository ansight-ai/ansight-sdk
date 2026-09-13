# Clipboard tools

`AnsightClipboardTools.tools()` registers `clipboard.get_text`, `clipboard.has_text`, `clipboard.set_text`, and `clipboard.clear`. Included by the all-in-one Ansight remote tools. Reads use read policy; writes and clearing use write policy. iOS and Mac Catalyst require the app to be active; iOS may ask for paste permission. macOS uses NSPasteboard. Text is preserved exactly and limited to 65536 UTF-8 bytes. No automatic clipboard polling occurs.
