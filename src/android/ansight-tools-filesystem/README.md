# ansight-tools-filesystem-android

Sandboxed Android file inspection and mutation tools.

## Tools

- `files.list_directory`
- `files.read_file`
- `files.get_file_checksum`
- `files.download_file`
- `files.begin_binary_download`
- `files.push_file`
- `files.copy_file`
- `files.move_file`
- `files.delete_file`

Read tools are available with `AnsightToolGuard.ReadOnly`. Push, copy, and move
require `ReadWrite`. Delete requires `FullAccess`.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.filesystem.AndroidFileSystemTools

val options = AnsightOptions(
    initialTools = AndroidFileSystemTools.create(),
    toolGuard = AnsightToolGuard.ReadWrite,
)
```

`files.begin_binary_download` returns transfer metadata and then streams the
file over the active pairing WebSocket using the shared binary transfer
protocol. `files.download_file` returns smaller files inline as base64.
