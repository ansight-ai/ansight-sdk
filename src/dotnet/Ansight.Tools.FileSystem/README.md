# Ansight.Tools.FileSystem

Grouped sandboxed file access tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

Registered tools:

- `files.list_directory`
- `files.read_file`
- `files.get_file_checksum`
- `files.download_file`
- `files.begin_binary_download`
- `files.push_file`
- `files.copy_file`
- `files.move_file`
- `files.delete_file`

## Usage

```csharp
using Ansight;
using Ansight.Tools.FileSystem;

var options = Options.CreateBuilder()
    .WithFileSystemTools()
    .WithReadOnlyToolAccess()
    .Build();
```

## File checksums

`files.get_file_checksum` computes one or more hexadecimal checksums for a sandboxed file without returning file contents.

Checksum request arguments:

- `root`: optional sandbox root alias
- `path`: file path relative to the root
- `algorithms`: optional comma-separated list of `md5`, `sha1`, `sha256`, `sha384`, `sha512`, `crc32`, or `all`; defaults to `sha256`

Checksum response highlights:

- `fileName`, `fileExtension`, `mimeType`
- `sizeBytes`, `lastModifiedUtc`, `version`
- `checksums`: array of `{ algorithm, checksum, encoding }`

## MCP-facing file transfer

`files.begin_binary_download` is the bridge-oriented path when an MCP caller wants the SDK to stream raw bytes over the pairing WebSocket and materialize the file in a caller-chosen local temp directory. The app SDK does not choose or know that temp path; it only returns metadata and then emits binary frames keyed by a `transferId`.

Binary download request arguments:

- `root`: optional sandbox root alias
- `path`: file path relative to the root
- `chunkBytes`: maximum bytes to include in each binary websocket frame
- `downloadId`: optional caller-supplied correlation id for mapping the transfer to a local temp file

Binary download response highlights:

- `downloadId`, `transferId`
- `fileName`, `fileExtension`, `mimeType`
- `sizeBytes`, `lastModifiedUtc`, `version`
- `deliveryMode = websocket_binary`
- `wireProtocol = ansight.file-transfer.v1`

The consuming MCP bridge is expected to:

- choose the temp directory and local file path
- call `files.begin_binary_download`
- map `transferId` to that local temp file
- write incoming `ASFT` binary frames into the chosen file until the `complete` frame arrives

`files.download_file` remains available as a JSON fallback when the caller cannot consume binary websocket frames. It stays inside the configured sandbox roots, returns best-effort file metadata for tool selection, and pages large files through ordinary `tool.result` payloads.

JSON fallback request arguments:

- `root`: optional sandbox root alias
- `path`: file path relative to the root
- `offsetBytes`: starting byte offset for the chunk
- `maxBytes`: maximum bytes to return for the chunk
- `encoding`: `auto`, `utf8`, or `base64`
- `expectedVersion`: optional version token from a prior chunk

JSON fallback response highlights:

- `fileName`, `fileExtension`, `mimeType`
- `sizeBytes`, `lastModifiedUtc`, `version`
- `offsetBytes`, `bytesRead`, `hasMore`, `nextOffsetBytes`
- `contentType`, `encoding`, and either `text` or `base64`
- `nextRequest`, which contains the next `tool.call` payload to continue the download safely

## File writes and file management

`files.push_file` writes caller-provided content into a folder under an approved sandbox root. MCP bridges should pass arbitrary files as `contentBase64`; `text` is available for UTF-8 text payloads.

Push request arguments:

- `root`: optional sandbox root alias
- `directoryPath`: destination folder path relative to the root
- `fileName`: destination file name, not a path
- `contentBase64` or `text`: provide exactly one
- `overwrite`: replace an existing file
- `createDirectory`: create the destination folder when missing

`files.copy_file` and `files.move_file` accept:

- `root`: optional source sandbox root alias
- `sourcePath`: source file path relative to the source root
- `destinationRoot`: optional destination sandbox root alias
- `destinationPath`: destination file path relative to the destination root
- `overwrite`: replace an existing destination file
- `createDirectory`: create the destination folder when missing

`files.delete_file` accepts `root` and `path` and is delete-scoped. Use `WithAllToolAccess()` or a custom `ToolGuard` if you want delete operations to execute. `files.push_file`, `files.copy_file`, and `files.move_file` are write-scoped and require `WithReadWriteToolAccess()`, `WithAllToolAccess()`, or a custom write-enabled guard.

Configure additional tagged roots:

```csharp
using Ansight;
using Ansight.Tools.FileSystem;

var options = Options.CreateBuilder()
    .WithFileSystemTools(fileSystem =>
    {
        fileSystem.AddRoot("logs", "/absolute/path/to/logs");
    })
    .WithReadOnlyToolAccess()
    .Build();
```

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
