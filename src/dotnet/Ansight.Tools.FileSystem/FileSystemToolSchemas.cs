namespace Ansight.Tools.FileSystem;

using Ansight.Tools;

internal static class FileSystemToolSchemas
{
    private static readonly ToolSchema SandboxRootSchema = ToolSchema.Object(
        description: "Approved sandbox root.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["alias"] = ToolSchema.String("Sandbox root alias."),
            ["path"] = ToolSchema.String("Absolute root path.")
        },
        required: new[] { "alias", "path" });

    private static readonly ToolSchema FileEntrySchema = ToolSchema.Object(
        description: "File or directory entry inside an approved sandbox root.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Entry name."),
            ["path"] = ToolSchema.String("Absolute path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["kind"] = ToolSchema.String("Entry type.", enumValues: new[] { "directory", "file" }),
            ["sizeBytes"] = ToolSchema.Integer("File size in bytes.", nullable: true),
            ["fileExtension"] = ToolSchema.String("File extension when the entry is a file.", nullable: true),
            ["mimeType"] = ToolSchema.String("Best-effort MIME type when the entry is a file.", nullable: true),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["isHidden"] = ToolSchema.Boolean("Whether the entry is hidden.")
        },
        required: new[] { "name", "path", "relativePath", "rootAlias", "kind", "sizeBytes", "fileExtension", "mimeType", "lastModifiedUtc", "isHidden" });

    private static readonly ToolSchema checksumEntrySchema = ToolSchema.Object(
        description: "Computed checksum for a file.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["algorithm"] = ToolSchema.String(
                "Checksum algorithm used.",
                enumValues: new[] { "md5", "sha1", "sha256", "sha384", "sha512", "crc32" }),
            ["checksum"] = ToolSchema.String("Lowercase hexadecimal checksum value."),
            ["encoding"] = ToolSchema.String("Checksum value encoding.", enumValues: new[] { "hex" })
        },
        required: new[] { "algorithm", "checksum", "encoding" });

    private static readonly ToolSchema DownloadFileNextArgumentsSchema = ToolSchema.Object(
        description: "Arguments for the next chunk request.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Sandbox root alias."),
            ["path"] = ToolSchema.String("File path relative to the root."),
            ["offsetBytes"] = ToolSchema.String("Starting byte offset for the next chunk."),
            ["maxBytes"] = ToolSchema.String("Maximum bytes to request for the next chunk."),
            ["encoding"] = ToolSchema.String("Requested response encoding for the next chunk.", enumValues: new[] { "auto", "utf8", "base64" }),
            ["expectedVersion"] = ToolSchema.String("Version token that must still match before continuing.")
        },
        required: new[] { "root", "path", "offsetBytes", "maxBytes", "encoding", "expectedVersion" });

    private static readonly ToolSchema DownloadFileNextRequestSchema = ToolSchema.Object(
        description: "Ready-to-use follow-up request for the next chunk, or null when the download is complete.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["toolId"] = ToolSchema.String("Tool id to invoke for the next chunk."),
            ["arguments"] = DownloadFileNextArgumentsSchema
        },
        required: new[] { "toolId", "arguments" },
        nullable: true);

    internal static ToolSchema ListDirectoryArguments { get; } = ToolSchema.Object(
        description: "Arguments for listing a sandboxed directory.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Optional sandbox root alias.", nullable: true),
            ["path"] = ToolSchema.String("Optional directory path relative to the root.", nullable: true),
            ["includeHidden"] = ToolSchema.Boolean("Include hidden files and directories."),
            ["recursive"] = ToolSchema.Boolean("Recurse into child directories."),
            ["maxDepth"] = ToolSchema.Integer("Maximum directory depth to traverse."),
            ["maxEntries"] = ToolSchema.Integer("Maximum number of entries to return.")
        });

    internal static ToolSchema ListDirectoryResult { get; } = ToolSchema.Object(
        description: "Directory listing payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["rootPath"] = ToolSchema.String("Resolved sandbox root path."),
            ["directoryPath"] = ToolSchema.String("Resolved directory path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["availableRoots"] = ToolSchema.Array(SandboxRootSchema, "Approved sandbox roots visible to the tool."),
            ["entries"] = ToolSchema.Array(FileEntrySchema, "Directory entries."),
            ["truncated"] = ToolSchema.Boolean("Whether additional entries were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "rootAlias", "rootPath", "directoryPath", "relativePath", "availableRoots", "entries", "truncated", "capturedAtUtc" });

    internal static ToolSchema ReadFileArguments { get; } = ToolSchema.Object(
        description: "Arguments for reading a sandboxed file.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Optional sandbox root alias.", nullable: true),
            ["path"] = ToolSchema.String("File path relative to the root."),
            ["maxBytes"] = ToolSchema.Integer("Maximum number of bytes to read."),
            ["encoding"] = ToolSchema.String("Requested response encoding.", enumValues: new[] { "auto", "utf8", "base64" }, nullable: true)
        },
        required: new[] { "path" });

    internal static ToolSchema ReadFileResult { get; } = ToolSchema.Object(
        description: "File read payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["rootPath"] = ToolSchema.String("Resolved sandbox root path."),
            ["filePath"] = ToolSchema.String("Resolved file path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["availableRoots"] = ToolSchema.Array(SandboxRootSchema, "Approved sandbox roots visible to the tool."),
            ["fileName"] = ToolSchema.String("File name."),
            ["fileExtension"] = ToolSchema.String("File extension.", nullable: true),
            ["mimeType"] = ToolSchema.String("Best-effort MIME type."),
            ["sizeBytes"] = ToolSchema.Integer("File size in bytes."),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["version"] = ToolSchema.String("Stable version token derived from file size and last modification time."),
            ["bytesRead"] = ToolSchema.Integer("Number of bytes returned."),
            ["truncated"] = ToolSchema.Boolean("Whether additional bytes were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["contentType"] = ToolSchema.String("Payload content type.", enumValues: new[] { "text", "binary" }),
            ["encoding"] = ToolSchema.String("Encoding used in the payload."),
            ["text"] = ToolSchema.String("UTF-8 text content when available.", nullable: true),
            ["base64"] = ToolSchema.String("Base64 content for binary payloads.", nullable: true)
        },
        required: new[] { "rootAlias", "rootPath", "filePath", "relativePath", "availableRoots", "fileName", "fileExtension", "mimeType", "sizeBytes", "lastModifiedUtc", "version", "bytesRead", "truncated", "capturedAtUtc", "contentType", "encoding", "text", "base64" });

    internal static ToolSchema GetFileChecksumArguments { get; } = ToolSchema.Object(
        description: "Arguments for computing checksums for a sandboxed file.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Optional sandbox root alias.", nullable: true),
            ["path"] = ToolSchema.String("File path relative to the root."),
            ["algorithms"] = ToolSchema.String("Optional comma-separated checksum algorithms: md5, sha1, sha256, sha384, sha512, crc32, or all. Defaults to sha256.", nullable: true)
        },
        required: new[] { "path" });

    internal static ToolSchema GetFileChecksumResult { get; } = ToolSchema.Object(
        description: "File checksum payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["rootPath"] = ToolSchema.String("Resolved sandbox root path."),
            ["filePath"] = ToolSchema.String("Resolved file path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["availableRoots"] = ToolSchema.Array(SandboxRootSchema, "Approved sandbox roots visible to the tool."),
            ["fileName"] = ToolSchema.String("File name."),
            ["fileExtension"] = ToolSchema.String("File extension.", nullable: true),
            ["mimeType"] = ToolSchema.String("Best-effort MIME type."),
            ["sizeBytes"] = ToolSchema.Integer("File size in bytes."),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["version"] = ToolSchema.String("Stable version token derived from file size and last modification time."),
            ["checksums"] = ToolSchema.Array(checksumEntrySchema, "Computed checksum values."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "rootAlias", "rootPath", "filePath", "relativePath", "availableRoots", "fileName", "fileExtension", "mimeType", "sizeBytes", "lastModifiedUtc", "version", "checksums", "capturedAtUtc" });

    internal static ToolSchema DownloadFileArguments { get; } = ToolSchema.Object(
        description: "Arguments for downloading a sandboxed file in resumable chunks.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Optional sandbox root alias.", nullable: true),
            ["path"] = ToolSchema.String("File path relative to the root."),
            ["offsetBytes"] = ToolSchema.Integer("Starting byte offset for this chunk."),
            ["maxBytes"] = ToolSchema.Integer("Maximum number of bytes to return."),
            ["encoding"] = ToolSchema.String("Requested response encoding.", enumValues: new[] { "auto", "utf8", "base64" }, nullable: true),
            ["expectedVersion"] = ToolSchema.String("Optional file version token returned by a previous chunk.", nullable: true)
        },
        required: new[] { "path" });

    internal static ToolSchema DownloadFileResult { get; } = ToolSchema.Object(
        description: "Chunked file download payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["rootPath"] = ToolSchema.String("Resolved sandbox root path."),
            ["filePath"] = ToolSchema.String("Resolved file path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["availableRoots"] = ToolSchema.Array(SandboxRootSchema, "Approved sandbox roots visible to the tool."),
            ["fileName"] = ToolSchema.String("File name."),
            ["fileExtension"] = ToolSchema.String("File extension.", nullable: true),
            ["mimeType"] = ToolSchema.String("Best-effort MIME type."),
            ["sizeBytes"] = ToolSchema.Integer("Total file size in bytes."),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["version"] = ToolSchema.String("Stable version token derived from file size and last modification time."),
            ["offsetBytes"] = ToolSchema.Integer("Byte offset used for this chunk."),
            ["requestedMaxBytes"] = ToolSchema.Integer("Requested maximum chunk size in bytes."),
            ["bytesRead"] = ToolSchema.Integer("Number of bytes returned in this chunk."),
            ["hasMore"] = ToolSchema.Boolean("Whether more chunks remain."),
            ["nextOffsetBytes"] = ToolSchema.Integer("Byte offset for the next chunk when available.", nullable: true),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["contentType"] = ToolSchema.String("Payload content type.", enumValues: new[] { "text", "binary" }),
            ["encoding"] = ToolSchema.String("Encoding used in the payload."),
            ["text"] = ToolSchema.String("UTF-8 text content when available.", nullable: true),
            ["base64"] = ToolSchema.String("Base64 content for binary payloads.", nullable: true),
            ["nextRequest"] = DownloadFileNextRequestSchema
        },
        required: new[] { "rootAlias", "rootPath", "filePath", "relativePath", "availableRoots", "fileName", "fileExtension", "mimeType", "sizeBytes", "lastModifiedUtc", "version", "offsetBytes", "requestedMaxBytes", "bytesRead", "hasMore", "nextOffsetBytes", "capturedAtUtc", "contentType", "encoding", "text", "base64", "nextRequest" });

    internal static ToolSchema BeginBinaryDownloadArguments { get; } = ToolSchema.Object(
        description: "Arguments for starting a binary WebSocket download of a sandboxed file.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Optional sandbox root alias.", nullable: true),
            ["path"] = ToolSchema.String("File path relative to the root."),
            ["chunkBytes"] = ToolSchema.Integer("Maximum bytes to include in each binary WebSocket frame."),
            ["downloadId"] = ToolSchema.String("Optional caller-supplied correlation id for mapping the transfer to a host temp file.", nullable: true)
        },
        required: new[] { "path" });

    internal static ToolSchema BeginBinaryDownloadResult { get; } = ToolSchema.Object(
        description: "Binary download initiation payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["rootPath"] = ToolSchema.String("Resolved sandbox root path."),
            ["filePath"] = ToolSchema.String("Resolved file path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["availableRoots"] = ToolSchema.Array(SandboxRootSchema, "Approved sandbox roots visible to the tool."),
            ["fileName"] = ToolSchema.String("File name."),
            ["fileExtension"] = ToolSchema.String("File extension.", nullable: true),
            ["mimeType"] = ToolSchema.String("Best-effort MIME type."),
            ["sizeBytes"] = ToolSchema.Integer("Total file size in bytes."),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["version"] = ToolSchema.String("Stable version token derived from file size and last modification time."),
            ["downloadId"] = ToolSchema.String("Caller correlation id for the host-side temp file."),
            ["transferId"] = ToolSchema.String("Transfer id carried in the binary frame headers."),
            ["deliveryMode"] = ToolSchema.String("How file bytes are delivered.", enumValues: new[] { "websocket_binary" }),
            ["wireProtocol"] = ToolSchema.String("Binary wire protocol identifier."),
            ["status"] = ToolSchema.String("Initial transfer state.", enumValues: new[] { "queued" }),
            ["chunkBytes"] = ToolSchema.Integer("Maximum bytes per binary frame."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "rootAlias", "rootPath", "filePath", "relativePath", "availableRoots", "fileName", "fileExtension", "mimeType", "sizeBytes", "lastModifiedUtc", "version", "downloadId", "transferId", "deliveryMode", "wireProtocol", "status", "chunkBytes", "capturedAtUtc" });
}
