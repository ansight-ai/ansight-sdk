import AnsightCore
import Foundation

public final class PushFileTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.pushFile,
            name: "Push File",
            description: "Writes caller-provided content into a sandboxed folder.",
            category: "files",
            policy: .write,
            keywords: "filesystem file write upload sandbox",
            argumentsSchema: AnsightFileSystemToolSchemas.pushFileArguments,
            resultSchema: AnsightFileSystemToolSchemas.pushFileResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightFileSystemSandbox.roots(options: options)
            let directory = try AnsightFileSystemSandbox.resolvePath(
                arguments: arguments,
                roots: roots,
                pathKey: "directoryPath",
                requireExisting: false,
                expectDirectory: true
            )
            let fileName = try validFileName(AnsightFileSystemSandbox.requiredString(arguments, key: "fileName"))
            let destinationPath = AnsightFileSystemSandbox.canonicalPath((directory.fullPath as NSString).appendingPathComponent(fileName))
            let destination = AnsightFileSystemResolvedPath(
                rootAlias: directory.rootAlias,
                rootPath: directory.rootPath,
                fullPath: destinationPath
            )
            let overwrite = try AnsightFileSystemSandbox.boolean(arguments, key: "overwrite", defaultValue: false)
            let createDirectory = try AnsightFileSystemSandbox.boolean(arguments, key: "createDirectory", defaultValue: false)
            let overwritten = FileManager.default.fileExists(atPath: destination.fullPath)
            let createdDirectory = try AnsightFileSystemSandbox.ensureWritableDestination(
                destination,
                overwrite: overwrite,
                createDirectory: createDirectory
            )
            let data = try contentData(arguments: arguments)
            try data.write(to: URL(fileURLWithPath: destination.fullPath), options: [.atomic])
            let attributes = try AnsightFileSystemIO.attributes(path: destination.fullPath)

            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: destination,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build push-file payload.", errorCode: "filesystem_push_failed")
            }

            payload["operation"] = .string(overwritten ? "overwritten" : "created")
            payload["overwritten"] = .bool(overwritten)
            payload["createdDirectory"] = .bool(createdDirectory)
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_push_failed")
        }
    }

    private func validFileName(_ fileName: String) throws -> String {
        let trimmed = fileName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty,
              trimmed == (trimmed as NSString).lastPathComponent,
              !trimmed.contains("/"),
              !trimmed.contains("\\") else {
            throw AnsightFileSystemToolError.invalidArgument("The argument 'fileName' must be a file name, not a path.")
        }

        return trimmed
    }

    private func contentData(arguments: [String: String]) throws -> Data {
        let contentBase64 = AnsightFileSystemSandbox.string(arguments, key: "contentBase64")
        let text = AnsightFileSystemSandbox.string(arguments, key: "text")

        switch (contentBase64, text) {
        case (.some(let value), nil):
            guard let data = Data(base64Encoded: value) else {
                throw AnsightFileSystemToolError.invalidArgument("The argument 'contentBase64' must be valid base64.")
            }

            return data
        case (nil, .some(let value)):
            return Data(value.utf8)
        default:
            throw AnsightFileSystemToolError.invalidArgument("Provide exactly one of 'contentBase64' or 'text'.")
        }
    }
}
