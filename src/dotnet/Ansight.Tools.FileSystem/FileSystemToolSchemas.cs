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
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time"),
            ["isHidden"] = ToolSchema.Boolean("Whether the entry is hidden.")
        },
        required: new[] { "name", "path", "relativePath", "rootAlias", "kind", "lastModifiedUtc", "isHidden" });

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
            ["encoding"] = ToolSchema.String("Requested response encoding.", enumValues: new[] { "utf8", "base64" }, nullable: true)
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
            ["sizeBytes"] = ToolSchema.Integer("File size in bytes."),
            ["bytesRead"] = ToolSchema.Integer("Number of bytes returned."),
            ["truncated"] = ToolSchema.Boolean("Whether additional bytes were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["contentType"] = ToolSchema.String("Payload content type.", enumValues: new[] { "text", "binary" }),
            ["encoding"] = ToolSchema.String("Encoding used in the payload."),
            ["text"] = ToolSchema.String("UTF-8 text content when available.", nullable: true),
            ["base64"] = ToolSchema.String("Base64 content for binary payloads.", nullable: true)
        },
        required: new[] { "rootAlias", "rootPath", "filePath", "relativePath", "availableRoots", "sizeBytes", "bytesRead", "truncated", "capturedAtUtc", "contentType", "encoding" });
}
