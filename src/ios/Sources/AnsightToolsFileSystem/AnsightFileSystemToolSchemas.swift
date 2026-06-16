import AnsightCore
import Foundation

internal enum AnsightFileSystemToolSchemas {
    static let listDirectoryArguments = object(
        description: "Arguments for listing a sandboxed directory.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("Optional directory path relative to the root.", nullable: true),
            "includeHidden": boolean("Include hidden files and directories."),
            "recursive": boolean("Recurse into child directories."),
            "maxDepth": integer("Maximum directory depth to traverse."),
            "maxEntries": integer("Maximum number of entries to return."),
        ]
    )

    static let listDirectoryResult = object(
        description: "Directory listing payload.",
        properties: [
            "rootAlias": string("Sandbox root alias."),
            "rootPath": string("Resolved sandbox root path."),
            "directoryPath": string("Resolved directory path."),
            "relativePath": string("Path relative to the sandbox root."),
            "availableRoots": array(sandboxRoot, "Approved sandbox roots visible to the tool."),
            "entries": array(fileEntry, "Directory entries."),
            "truncated": boolean("Whether additional entries were omitted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["rootAlias", "rootPath", "directoryPath", "relativePath", "availableRoots", "entries", "truncated", "capturedAtUtc"]
    )

    static let readFileArguments = object(
        description: "Arguments for reading a sandboxed file.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("File path relative to the root."),
            "maxBytes": integer("Maximum number of bytes to read."),
            "encoding": string("Requested response encoding.", enumValues: encodings, nullable: true),
        ],
        required: ["path"]
    )

    static let readFileResult = object(
        description: "File read payload.",
        properties: resolvedFileProperties().merging([
            "bytesRead": integer("Number of bytes returned."),
            "truncated": boolean("Whether additional bytes were omitted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "contentType": string("Payload content type.", enumValues: ["text", "binary"]),
            "encoding": string("Encoding used in the payload."),
            "text": string("UTF-8 text content when available.", nullable: true),
            "base64": string("Base64 content for binary payloads.", nullable: true),
        ]) { _, new in new },
        required: resolvedFileRequired + ["bytesRead", "truncated", "capturedAtUtc", "contentType", "encoding", "text", "base64"]
    )

    static let getFileChecksumArguments = object(
        description: "Arguments for computing checksums for a sandboxed file.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("File path relative to the root."),
            "algorithms": string("Optional comma-separated checksum algorithms: md5, sha1, sha256, sha384, sha512, crc32, or all. Defaults to sha256.", nullable: true),
        ],
        required: ["path"]
    )

    static let getFileChecksumResult = object(
        description: "File checksum payload.",
        properties: resolvedFileProperties().merging([
            "checksums": array(checksumEntry, "Computed checksum values."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ]) { _, new in new },
        required: resolvedFileRequired + ["checksums", "capturedAtUtc"]
    )

    static let downloadFileArguments = object(
        description: "Arguments for downloading a sandboxed file in resumable chunks.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("File path relative to the root."),
            "offsetBytes": integer("Starting byte offset for this chunk."),
            "maxBytes": integer("Maximum number of bytes to return."),
            "encoding": string("Requested response encoding.", enumValues: encodings, nullable: true),
            "expectedVersion": string("Optional file version token returned by a previous chunk.", nullable: true),
        ],
        required: ["path"]
    )

    static let downloadFileResult = object(
        description: "Chunked file download payload.",
        properties: resolvedFileProperties().merging([
            "offsetBytes": integer("Byte offset used for this chunk."),
            "requestedMaxBytes": integer("Requested maximum chunk size in bytes."),
            "bytesRead": integer("Number of bytes returned in this chunk."),
            "hasMore": boolean("Whether more chunks remain."),
            "nextOffsetBytes": integer("Byte offset for the next chunk when available.", nullable: true),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "contentType": string("Payload content type.", enumValues: ["text", "binary"]),
            "encoding": string("Encoding used in the payload."),
            "text": string("UTF-8 text content when available.", nullable: true),
            "base64": string("Base64 content for binary payloads.", nullable: true),
            "nextRequest": downloadFileNextRequest,
        ]) { _, new in new },
        required: resolvedFileRequired + ["offsetBytes", "requestedMaxBytes", "bytesRead", "hasMore", "nextOffsetBytes", "capturedAtUtc", "contentType", "encoding", "text", "base64", "nextRequest"]
    )

    static let beginBinaryDownloadArguments = object(
        description: "Arguments for starting a binary WebSocket download of a sandboxed file.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("File path relative to the root."),
            "chunkBytes": integer("Maximum bytes to include in each binary WebSocket frame."),
            "downloadId": string("Optional caller-supplied correlation id for mapping the transfer to a host temp file.", nullable: true),
        ],
        required: ["path"]
    )

    static let beginBinaryDownloadResult = object(
        description: "Binary download initiation payload.",
        properties: resolvedFileProperties().merging([
            "downloadId": string("Caller correlation id for the host-side temp file."),
            "transferId": string("Transfer id carried in the binary frame headers."),
            "deliveryMode": string("How file bytes are delivered.", enumValues: ["websocket_binary"]),
            "wireProtocol": string("Binary wire protocol identifier."),
            "status": string("Initial transfer state.", enumValues: ["queued"]),
            "chunkBytes": integer("Maximum bytes per binary frame."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ]) { _, new in new },
        required: resolvedFileRequired + ["downloadId", "transferId", "deliveryMode", "wireProtocol", "status", "chunkBytes", "capturedAtUtc"]
    )

    static let pushFileArguments = object(
        description: "Arguments for writing caller-provided content into a sandboxed folder.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "directoryPath": string("Destination folder path relative to the root."),
            "fileName": string("Destination file name. This must be a file name, not a path."),
            "contentBase64": string("Base64-encoded file content. Provide exactly one of contentBase64 or text.", nullable: true),
            "text": string("UTF-8 text content. Provide exactly one of contentBase64 or text.", nullable: true),
            "overwrite": boolean("Replace an existing file with the same name."),
            "createDirectory": boolean("Create the destination folder if it does not exist."),
        ],
        required: ["directoryPath", "fileName"]
    )

    static let pushFileResult = object(
        description: "Sandboxed file write payload.",
        properties: resolvedFileProperties().merging([
            "operation": string("Write outcome.", enumValues: ["created", "overwritten"]),
            "overwritten": boolean("Whether an existing file was replaced."),
            "createdDirectory": boolean("Whether the destination folder was created."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ]) { _, new in new },
        required: resolvedFileRequired + ["operation", "overwritten", "createdDirectory", "capturedAtUtc"]
    )

    static let copyFileArguments = transferArguments(description: "Arguments for copying a sandboxed file.")

    static let copyFileResult = transferResult(description: "Sandboxed file copy payload.", operation: "copied")

    static let moveFileArguments = transferArguments(description: "Arguments for moving or renaming a sandboxed file.")

    static let moveFileResult = transferResult(description: "Sandboxed file move payload.", operation: "moved")

    static let deleteFileArguments = object(
        description: "Arguments for deleting a sandboxed file.",
        properties: [
            "root": string("Optional sandbox root alias.", nullable: true),
            "path": string("File path relative to the root."),
        ],
        required: ["path"]
    )

    static let deleteFileResult = object(
        description: "Sandboxed file delete payload.",
        properties: resolvedFileProperties().merging([
            "deleted": boolean("Whether the file was deleted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ]) { _, new in new },
        required: resolvedFileRequired + ["deleted", "capturedAtUtc"]
    )

    private static let encodings = ["auto", "utf8", "base64"]
    private static let resolvedFileRequired = [
        "rootAlias",
        "rootPath",
        "filePath",
        "relativePath",
        "availableRoots",
        "fileName",
        "fileExtension",
        "mimeType",
        "sizeBytes",
        "lastModifiedUtc",
        "version",
    ]

    private static let sandboxRoot = objectJSON(
        description: "Approved sandbox root.",
        properties: [
            "alias": string("Sandbox root alias."),
            "path": string("Absolute root path."),
        ],
        required: ["alias", "path"]
    )

    private static let fileEntry = objectJSON(
        description: "File or directory entry inside an approved sandbox root.",
        properties: [
            "name": string("Entry name."),
            "path": string("Absolute path."),
            "relativePath": string("Path relative to the sandbox root."),
            "rootAlias": string("Sandbox root alias."),
            "kind": string("Entry type.", enumValues: ["directory", "file"]),
            "sizeBytes": integer("File size in bytes.", nullable: true),
            "fileExtension": string("File extension when the entry is a file.", nullable: true),
            "mimeType": string("Best-effort MIME type when the entry is a file.", nullable: true),
            "lastModifiedUtc": string("Last modification time.", format: "date-time"),
            "isHidden": boolean("Whether the entry is hidden."),
        ],
        required: ["name", "path", "relativePath", "rootAlias", "kind", "sizeBytes", "fileExtension", "mimeType", "lastModifiedUtc", "isHidden"]
    )

    private static let checksumEntry = objectJSON(
        description: "Computed checksum for a file.",
        properties: [
            "algorithm": string("Checksum algorithm used.", enumValues: ["md5", "sha1", "sha256", "sha384", "sha512", "crc32"]),
            "checksum": string("Lowercase hexadecimal checksum value."),
            "encoding": string("Checksum value encoding.", enumValues: ["hex"]),
        ],
        required: ["algorithm", "checksum", "encoding"]
    )

    private static let downloadFileNextRequest = objectJSON(
        description: "Ready-to-use follow-up request for the next chunk, or null when the download is complete.",
        properties: [
            "toolId": string("Tool id to invoke for the next chunk."),
            "arguments": objectJSON(
                description: "Arguments for the next chunk request.",
                properties: [
                    "root": string("Sandbox root alias."),
                    "path": string("File path relative to the root."),
                    "offsetBytes": string("Starting byte offset for the next chunk."),
                    "maxBytes": string("Maximum bytes to request for the next chunk."),
                    "encoding": string("Requested response encoding for the next chunk.", enumValues: encodings),
                    "expectedVersion": string("Version token that must still match before continuing."),
                ],
                required: ["root", "path", "offsetBytes", "maxBytes", "encoding", "expectedVersion"]
            ),
        ],
        required: ["toolId", "arguments"],
        nullable: true
    )

    private static func resolvedFileProperties() -> [String: JSONValue] {
        [
            "rootAlias": string("Sandbox root alias."),
            "rootPath": string("Resolved sandbox root path."),
            "filePath": string("Resolved file path."),
            "relativePath": string("Path relative to the sandbox root."),
            "availableRoots": array(sandboxRoot, "Approved sandbox roots visible to the tool."),
            "fileName": string("File name."),
            "fileExtension": string("File extension.", nullable: true),
            "mimeType": string("Best-effort MIME type."),
            "sizeBytes": integer("File size in bytes."),
            "lastModifiedUtc": string("Last modification time.", format: "date-time"),
            "version": string("Stable version token derived from file size and last modification time."),
        ]
    }

    private static func transferArguments(description: String) -> AnsightToolSchema {
        object(
            description: description,
            properties: [
                "root": string("Optional source sandbox root alias.", nullable: true),
                "sourcePath": string("Source file path relative to the source root."),
                "destinationRoot": string("Optional destination sandbox root alias. Defaults to the resolved source root for relative destination paths.", nullable: true),
                "destinationPath": string("Destination file path relative to the destination root."),
                "overwrite": boolean("Replace an existing destination file."),
                "createDirectory": boolean("Create the destination folder if it does not exist."),
            ],
            required: ["sourcePath", "destinationPath"]
        )
    }

    private static func transferResult(description: String, operation: String) -> AnsightToolSchema {
        object(
            description: description,
            properties: resolvedFileProperties().merging([
                "operation": string("Operation outcome.", enumValues: [operation]),
                "sourceRootAlias": string("Source sandbox root alias."),
                "sourceRootPath": string("Resolved source sandbox root path."),
                "sourceFilePath": string("Resolved source file path."),
                "sourceRelativePath": string("Source path relative to the source sandbox root."),
                "destinationRootAlias": string("Destination sandbox root alias."),
                "destinationRootPath": string("Resolved destination sandbox root path."),
                "destinationFilePath": string("Resolved destination file path."),
                "destinationRelativePath": string("Destination path relative to the destination sandbox root."),
                "overwritten": boolean("Whether an existing destination file was replaced."),
                "createdDirectory": boolean("Whether the destination folder was created."),
                "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            ]) { _, new in new },
            required: resolvedFileRequired + [
                "operation",
                "sourceRootAlias",
                "sourceRootPath",
                "sourceFilePath",
                "sourceRelativePath",
                "destinationRootAlias",
                "destinationRootPath",
                "destinationFilePath",
                "destinationRelativePath",
                "overwritten",
                "createdDirectory",
                "capturedAtUtc",
            ]
        )
    }

    private static func object(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> AnsightToolSchema {
        AnsightToolSchema(json: objectJSON(description: description, properties: properties, required: required))
    }

    private static func objectJSON(
        description: String,
        properties: [String: JSONValue],
        required: [String] = [],
        nullable: Bool = false
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string("object"), .string("null")]) : .string("object"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "properties": .object(properties),
        ]

        if !required.isEmpty {
            result["required"] = .array(required.map(JSONValue.string))
        }

        return .object(result)
    }

    private static func array(_ items: JSONValue, _ description: String) -> JSONValue {
        .object([
            "type": .string("array"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "items": items,
        ])
    }

    private static func string(
        _ description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        primitive(type: "string", description: description, enumValues: enumValues, nullable: nullable, format: format)
    }

    private static func integer(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "integer", description: description, nullable: nullable)
    }

    private static func boolean(_ description: String) -> JSONValue {
        primitive(type: "boolean", description: description)
    }

    private static func primitive(
        type: String,
        description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string(type), .string("null")]) : .string(type),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if !enumValues.isEmpty {
            result["enum"] = .array(enumValues.map(JSONValue.string))
        }

        if let format {
            result["format"] = .string(format)
        }

        return .object(result)
    }
}
