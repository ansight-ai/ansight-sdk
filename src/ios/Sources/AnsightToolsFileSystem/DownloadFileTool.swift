import AnsightCore
import Foundation

public final class DownloadFileTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.downloadFile,
            name: "Download File",
            description: "Downloads a sandboxed file in resumable chunks with text or base64 payloads.",
            category: "files",
            policy: .read,
            keywords: "filesystem file download stream sandbox base64 text binary",
            argumentsSchema: AnsightFileSystemToolSchemas.downloadFileArguments,
            resultSchema: AnsightFileSystemToolSchemas.downloadFileResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightFileSystemSandbox.roots(options: options)
            let resolvedFile = try AnsightFileSystemSandbox.resolvePath(
                arguments: arguments,
                roots: roots,
                requireExisting: true,
                expectDirectory: false
            )
            let attributes = try AnsightFileSystemIO.attributes(path: resolvedFile.fullPath)
            let sizeBytes = Int64((attributes[.size] as? NSNumber)?.int64Value ?? 0)
            let lastModified = attributes[.modificationDate] as? Date ?? Date(timeIntervalSince1970: 0)
            let version = AnsightFileSystemContentDescriptor.version(sizeBytes: sizeBytes, lastModified: lastModified)

            if let expectedVersion = AnsightFileSystemSandbox.string(arguments, key: "expectedVersion"),
               expectedVersion != version {
                return .failure(
                    "The file version changed from '\(expectedVersion)' to '\(version)'. Restart the download from offset 0.",
                    errorCode: "filesystem_download_version_mismatch"
                )
            }

            let offsetBytes = try AnsightFileSystemSandbox.int64(
                arguments,
                key: "offsetBytes",
                defaultValue: 0,
                minimum: 0,
                maximum: Int64.max
            )
            if offsetBytes > sizeBytes {
                return .failure(
                    "The offset '\(offsetBytes)' is beyond the end of the file (\(sizeBytes) bytes).",
                    errorCode: "filesystem_download_offset_invalid"
                )
            }

            let maxBytes = try AnsightFileSystemSandbox.integer(
                arguments,
                key: "maxBytes",
                defaultValue: 256 * 1_024,
                minimum: 1,
                maximum: 1_024 * 1_024
            )
            let bytesToRead = Int(min(sizeBytes - offsetBytes, Int64(maxBytes)))
            let data = try AnsightFileSystemIO.read(path: resolvedFile.fullPath, offsetBytes: offsetBytes, maxBytes: bytesToRead)
            let requestedEncoding = AnsightFileSystemSandbox.string(arguments, key: "encoding")
            let encoded = try AnsightFileSystemContentDescriptor.encodeContent(
                data: data,
                mimeType: AnsightFileSystemContentDescriptor.mimeType(path: resolvedFile.fullPath),
                requestedEncoding: requestedEncoding
            )
            let nextOffsetBytes = offsetBytes + Int64(data.count)
            let hasMore = nextOffsetBytes < sizeBytes

            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: resolvedFile,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build download payload.", errorCode: "filesystem_download_failed")
            }

            payload["offsetBytes"] = .integer(offsetBytes)
            payload["requestedMaxBytes"] = .integer(Int64(maxBytes))
            payload["bytesRead"] = .integer(Int64(data.count))
            payload["hasMore"] = .bool(hasMore)
            payload["nextOffsetBytes"] = hasMore ? .integer(nextOffsetBytes) : .null
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
            payload["contentType"] = .string(encoded.contentType)
            payload["encoding"] = .string(encoded.encoding)
            payload["text"] = encoded.text.map(JSONValue.string) ?? .null
            payload["base64"] = encoded.base64.map(JSONValue.string) ?? .null
            payload["nextRequest"] = hasMore
                ? nextRequest(resolvedFile: resolvedFile, nextOffsetBytes: nextOffsetBytes, maxBytes: maxBytes, requestedEncoding: encoded.requestedEncoding, version: version)
                : .null
            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_download_failed")
        }
    }

    private func nextRequest(
        resolvedFile: AnsightFileSystemResolvedPath,
        nextOffsetBytes: Int64,
        maxBytes: Int,
        requestedEncoding: String,
        version: String
    ) -> JSONValue {
        .object([
            "toolId": .string(AnsightFileSystemToolIds.downloadFile),
            "arguments": .object([
                "root": .string(resolvedFile.rootAlias),
                "path": .string(AnsightFileSystemSandbox.relativePath(rootPath: resolvedFile.rootPath, fullPath: resolvedFile.fullPath)),
                "offsetBytes": .string(String(nextOffsetBytes)),
                "maxBytes": .string(String(maxBytes)),
                "encoding": .string(requestedEncoding),
                "expectedVersion": .string(version),
            ]),
        ])
    }
}
