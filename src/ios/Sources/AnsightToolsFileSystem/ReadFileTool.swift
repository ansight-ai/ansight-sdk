import AnsightCore
import Foundation

public final class ReadFileTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.readFile,
            name: "Read File",
            description: "Reads a file from the app sandbox using a constrained path.",
            category: "files",
            policy: .read,
            keywords: "filesystem file read sandbox",
            argumentsSchema: AnsightFileSystemToolSchemas.readFileArguments,
            resultSchema: AnsightFileSystemToolSchemas.readFileResult
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
            let maxBytes = try AnsightFileSystemSandbox.integer(
                arguments,
                key: "maxBytes",
                defaultValue: 64 * 1_024,
                minimum: 1,
                maximum: 1_024 * 1_024
            )
            let bytesToRead = Int(min(sizeBytes, Int64(maxBytes)))
            let data = try AnsightFileSystemIO.read(path: resolvedFile.fullPath, maxBytes: bytesToRead)
            let mimeType = AnsightFileSystemContentDescriptor.mimeType(path: resolvedFile.fullPath)
            let encoded = try AnsightFileSystemContentDescriptor.encodeContent(
                data: data,
                mimeType: mimeType,
                requestedEncoding: AnsightFileSystemSandbox.string(arguments, key: "encoding")
            )

            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: resolvedFile,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build file payload.", errorCode: "filesystem_read_failed")
            }

            payload["bytesRead"] = .integer(Int64(data.count))
            payload["truncated"] = .bool(sizeBytes > Int64(data.count))
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
            payload["contentType"] = .string(encoded.contentType)
            payload["encoding"] = .string(encoded.encoding)
            payload["text"] = encoded.text.map(JSONValue.string) ?? .null
            payload["base64"] = encoded.base64.map(JSONValue.string) ?? .null
            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_read_failed")
        }
    }
}
