import AnsightCore
import Foundation

public final class DeleteFileTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.deleteFile,
            name: "Delete File",
            description: "Deletes a sandboxed file from an approved root.",
            category: "files",
            policy: .critical,
            keywords: "filesystem file delete remove sandbox",
            argumentsSchema: AnsightFileSystemToolSchemas.deleteFileArguments,
            resultSchema: AnsightFileSystemToolSchemas.deleteFileResult
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
            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: resolvedFile,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build delete payload.", errorCode: "filesystem_delete_failed")
            }

            try FileManager.default.removeItem(atPath: resolvedFile.fullPath)
            payload["deleted"] = .bool(true)
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_delete_failed")
        }
    }
}
