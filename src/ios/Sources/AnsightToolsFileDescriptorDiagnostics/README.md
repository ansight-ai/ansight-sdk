# AnsightToolsFileDescriptorDiagnostics

Process-level file descriptor diagnostics for native iOS apps.

## Tools

| Tool id | Result |
| --- | --- |
| `file_descriptors.list_open` | Filterable descriptor records, including kind, flags, position, inode, and optional target. |
| `file_descriptors.count_open` | Only `{ "count": number }`; it does not resolve targets or collect descriptor details. |
| `file_descriptors.inspect` | One descriptor record by descriptor number. |
| `file_descriptors.get_usage` | Open count, process limits, remaining capacity, and utilization. |

All tools use `.read`. Listing and inspection can disclose file paths and
socket metadata. Set `includeTargets` to `false` when constructing
`AnsightFileDescriptorDiagnosticsOptions` to suppress descriptor targets.
The count tool fails instead of returning an undercount if a custom
`maximumScannedDescriptors` value is below the process descriptor range.

```swift
import AnsightCore
import AnsightToolsFileDescriptorDiagnostics

let options = AnsightFileDescriptorDiagnosticsOptions(includeTargets: true)
try AnsightRuntime.shared.registerFileDescriptorDiagnosticsTools(options: options)
```
