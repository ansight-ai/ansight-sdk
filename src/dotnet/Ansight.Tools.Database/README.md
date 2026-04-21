# Ansight.Tools.Database

Grouped database inspection tool registrations for the Ansight .NET SDK.

## Usage

```csharp
using Ansight;
using Ansight.Tools.Database;

var options = Options.CreateBuilder()
    .WithDatabaseTools()
    .WithReadOnlyToolAccess()
    .Build();
```

## Query result shape

`data.query` keeps the simple `columns` and `rows` payloads, and also returns richer metadata for host renderers:

- `columnMetadata`: result columns in order, including each column's stable row key, declared SQLite type when available, and source table/column when SQLite exposes it.
- `rows`: row objects keyed by `columnMetadata.key`, so duplicate result column names are preserved with stable suffixes.
- `rowValues`: ordered cell arrays with each cell's runtime SQLite storage type.

SQLite `BLOB` values are encoded as descriptor objects with `type = "blob"`, `base64`, and `byteLength` fields. The package inspects ordinary SQLite databases that are readable through the platform SQLite library; encrypted database support is intentionally out of scope.
