# ansight-tools-filedescriptordiagnostics-android

Process-level file descriptor diagnostic remote tools for Android apps.

## Tools

| Tool id | Result |
| --- | --- |
| `file_descriptors.list_open` | Filterable descriptor records, including kind, flags, position, inode, and optional target. |
| `file_descriptors.count_open` | Only `{ "count": number }`; it does not resolve targets or collect descriptor details. |
| `file_descriptors.inspect` | One descriptor record by descriptor number. |
| `file_descriptors.get_usage` | Open count, process limits, remaining capacity, and utilization. |

All tools use `ToolPolicy.Read`. Listing and inspection can disclose file paths and
socket metadata. Set `includeTargets` to `false` in
`AndroidFileDescriptorDiagnosticsOptions` to suppress descriptor targets.

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.tools.filedescriptordiagnostics.withFileDescriptorDiagnosticsTools

val options = AnsightOptions.createBuilder()
    .withFileDescriptorDiagnosticsTools {
        includeTargets(false)
    }
    .build()
```
