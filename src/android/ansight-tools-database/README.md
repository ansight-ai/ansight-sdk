# ansight-tools-database-android

SQLite discovery, schema inspection, and read-only query tools for Android app
sandbox roots.

## Tools

- `data.list_databases`
- `data.describe_schema`
- `data.query`

All database tools are read-scoped and available with
`AnsightToolGuard.ReadOnly`.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.database.AndroidDatabaseTools

val options = AnsightOptions(
    initialTools = AndroidDatabaseTools.create(),
    toolGuard = AnsightToolGuard.ReadOnly,
)
```

`data.query` accepts one read-only SQLite statement and clamps result limits.
Write statements and multiple statements are rejected.
