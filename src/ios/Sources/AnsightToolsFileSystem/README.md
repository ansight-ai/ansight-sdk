# AnsightToolsFileSystem

Sandboxed iOS file inspection and mutation tools.

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

Read tools are available with `.readOnly`. Push, copy, and move require
`.readWrite`. Delete requires `.fullAccess`.

## Usage

```swift
import AnsightCore
import AnsightToolsFileSystem

let options = AnsightFileSystemToolsOptions.createBuilder()
    .addRoot(alias: "exports", path: exportsDirectory.path)
    .build()

try AnsightRuntime.shared.registerFileSystemTools(options: options)
```

`files.begin_binary_download` returns transfer metadata and then streams the
file over the active pairing WebSocket. `files.download_file` keeps smaller
transfers inside JSON.
