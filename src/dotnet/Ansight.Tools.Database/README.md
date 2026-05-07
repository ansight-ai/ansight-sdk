# Ansight.Tools.Database

Grouped database inspection tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
