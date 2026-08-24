# AnsightToolsDatabase

SQLite discovery, schema inspection, and read-only query tools for iOS app
sandbox roots.

## Tools

- `data.list_databases`
- `data.describe_schema`
- `data.query`

All database tools use `.read` and are available with `.readOnly`.

## Usage

```swift
import AnsightCore
import AnsightToolsDatabase

let options = AnsightDatabaseToolsOptions.createBuilder()
    .addRoot(alias: "seeded", path: databaseDirectory.path)
    .includePlatformRoots(true)
    .build()

try AnsightRuntime.shared.registerDatabaseTools(options: options)
```

`data.query` accepts one read-only SQLite statement, clamps row limits, returns
column metadata, and rejects write statements.
