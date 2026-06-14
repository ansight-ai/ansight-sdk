import AnsightKit
import Foundation

internal enum AnsightFileSystemContentDescriptor {
    private static let mimeTypesByExtension: [String: String] = [
        ".aac": "audio/aac",
        ".bmp": "image/bmp",
        ".csv": "text/csv",
        ".dae": "model/vnd.collada+xml",
        ".db": "application/x-sqlite3",
        ".gif": "image/gif",
        ".glb": "model/gltf-binary",
        ".gltf": "model/gltf+json",
        ".gz": "application/gzip",
        ".heic": "image/heic",
        ".heif": "image/heif",
        ".htm": "text/html",
        ".html": "text/html",
        ".ini": "text/plain",
        ".jpeg": "image/jpeg",
        ".jpg": "image/jpeg",
        ".json": "application/json",
        ".jsonl": "application/x-ndjson",
        ".log": "text/plain",
        ".m4a": "audio/mp4",
        ".md": "text/markdown",
        ".mov": "video/quicktime",
        ".mp3": "audio/mpeg",
        ".mp4": "video/mp4",
        ".obj": "model/obj",
        ".pdf": "application/pdf",
        ".plist": "application/xml",
        ".png": "image/png",
        ".sql": "application/sql",
        ".sqlite": "application/x-sqlite3",
        ".sqlite3": "application/x-sqlite3",
        ".stl": "model/stl",
        ".svg": "image/svg+xml",
        ".tar": "application/x-tar",
        ".toml": "application/toml",
        ".tsv": "text/tab-separated-values",
        ".txt": "text/plain",
        ".usdz": "model/vnd.usdz+zip",
        ".wav": "audio/wav",
        ".webp": "image/webp",
        ".xml": "application/xml",
        ".yaml": "application/yaml",
        ".yml": "application/yaml",
        ".zip": "application/zip",
    ]

    static func resolvedFilePayload(
        resolvedFile: AnsightFileSystemResolvedPath,
        roots: [AnsightFileSystemRoot],
        attributes: [FileAttributeKey: Any]
    ) -> JSONValue {
        let fileName = (resolvedFile.fullPath as NSString).lastPathComponent
        let extensionValue = fileExtension(path: resolvedFile.fullPath)
        let modified = attributes[.modificationDate] as? Date ?? Date(timeIntervalSince1970: 0)
        let sizeBytes = Int64((attributes[.size] as? NSNumber)?.int64Value ?? 0)

        return .object([
            "rootAlias": .string(resolvedFile.rootAlias),
            "rootPath": .string(resolvedFile.rootPath),
            "filePath": .string(resolvedFile.fullPath),
            "relativePath": .string(AnsightFileSystemSandbox.relativePath(rootPath: resolvedFile.rootPath, fullPath: resolvedFile.fullPath)),
            "availableRoots": AnsightFileSystemSandbox.availableRootsJSON(roots),
            "fileName": .string(fileName),
            "fileExtension": AnsightFileSystemSandbox.optionalString(extensionValue),
            "mimeType": .string(mimeType(path: resolvedFile.fullPath)),
            "sizeBytes": .integer(sizeBytes),
            "lastModifiedUtc": .string(AnsightClock.isoString(from: modified)),
            "version": .string(version(sizeBytes: sizeBytes, lastModified: modified)),
        ])
    }

    static func encodeContent(data: Data, mimeType: String, requestedEncoding: String?) throws -> AnsightFileSystemEncodedContent {
        let normalized = try normalizeRequestedEncoding(requestedEncoding)
        switch normalized {
        case "utf8":
            return try encodeUTF8(data: data, requestedEncoding: normalized)
        case "base64":
            return encodeBase64(data: data, requestedEncoding: normalized)
        default:
            return looksLikeText(data: data, mimeType: mimeType)
                ? try encodeUTF8(data: data, requestedEncoding: normalized)
                : encodeBase64(data: data, requestedEncoding: normalized)
        }
    }

    static func normalizeRequestedEncoding(_ requestedEncoding: String?) throws -> String {
        let normalized = requestedEncoding?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        switch normalized {
        case nil, "", "auto":
            return "auto"
        case "utf8", "utf-8":
            return "utf8"
        case "base64":
            return "base64"
        default:
            throw AnsightFileSystemToolError.invalidArgument("The argument 'encoding' must be one of: auto, utf8, base64.")
        }
    }

    static func fileExtension(path: String) -> String? {
        let extensionValue = (path as NSString).pathExtension
        return extensionValue.isEmpty ? nil : ".\(extensionValue)"
    }

    static func mimeType(path: String) -> String {
        guard let extensionValue = fileExtension(path: path)?.lowercased(),
              let mimeType = mimeTypesByExtension[extensionValue] else {
            return "application/octet-stream"
        }

        return mimeType
    }

    static func version(sizeBytes: Int64, lastModified: Date) -> String {
        let ticks = Int64(lastModified.timeIntervalSince1970 * 1_000_000_000)
        return "\(sizeBytes):\(ticks)"
    }

    static func looksLikeText(data: Data, mimeType: String) -> Bool {
        guard String(data: data, encoding: .utf8) != nil else {
            return false
        }

        if isTextLikeMimeType(mimeType) {
            return true
        }

        return data.allSatisfy { byte in
            if byte == 0 {
                return false
            }

            if byte < 0x20 && byte != 0x09 && byte != 0x0a && byte != 0x0d {
                return false
            }

            return true
        }
    }

    private static func encodeUTF8(data: Data, requestedEncoding: String) throws -> AnsightFileSystemEncodedContent {
        guard let text = String(data: data, encoding: .utf8) else {
            throw AnsightFileSystemToolError.invalidArgument(
                "The requested chunk is not valid UTF-8. Use 'base64' for binary-safe transfer."
            )
        }

        return AnsightFileSystemEncodedContent(
            requestedEncoding: requestedEncoding,
            contentType: "text",
            encoding: "utf-8",
            text: text,
            base64: nil
        )
    }

    private static func encodeBase64(data: Data, requestedEncoding: String) -> AnsightFileSystemEncodedContent {
        AnsightFileSystemEncodedContent(
            requestedEncoding: requestedEncoding,
            contentType: "binary",
            encoding: "base64",
            text: nil,
            base64: data.base64EncodedString()
        )
    }

    private static func isTextLikeMimeType(_ mimeType: String) -> Bool {
        let lowercased = mimeType.lowercased()
        return lowercased.hasPrefix("text/") ||
            lowercased == "application/json" ||
            lowercased == "application/xml" ||
            lowercased == "application/yaml" ||
            lowercased == "application/x-ndjson" ||
            lowercased == "application/javascript" ||
            lowercased == "application/sql" ||
            lowercased.hasSuffix("+json") ||
            lowercased.hasSuffix("+xml")
    }
}
