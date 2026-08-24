import AnsightCore
import Foundation

public final class CopyFileTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.copyFile,
            name: "Copy File",
            description: "Copies a sandboxed file to another approved sandbox path.",
            category: "files",
            policy: .write,
            keywords: "filesystem file copy sandbox",
            argumentsSchema: AnsightFileSystemToolSchemas.copyFileArguments,
            resultSchema: AnsightFileSystemToolSchemas.copyFileResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightFileSystemSandbox.roots(options: options)
            let source = try AnsightFileSystemSandbox.resolvePath(
                arguments: arguments,
                roots: roots,
                pathKey: "sourcePath",
                requireExisting: true,
                expectDirectory: false
            )
            let destination = try AnsightFileSystemSandbox.resolveDestinationPath(
                arguments: arguments,
                roots: roots,
                sourceRoot: AnsightFileSystemSandbox.root(for: source)
            )
            let overwrite = try AnsightFileSystemSandbox.boolean(arguments, key: "overwrite", defaultValue: false)
            let createDirectory = try AnsightFileSystemSandbox.boolean(arguments, key: "createDirectory", defaultValue: false)
            let overwritten = FileManager.default.fileExists(atPath: destination.fullPath)
            let createdDirectory = try AnsightFileSystemSandbox.ensureWritableDestination(
                destination,
                overwrite: overwrite,
                createDirectory: createDirectory
            )

            if overwritten {
                try FileManager.default.removeItem(atPath: destination.fullPath)
            }

            try FileManager.default.copyItem(atPath: source.fullPath, toPath: destination.fullPath)
            return try transferResult(
                operation: "copied",
                source: source,
                destination: destination,
                roots: roots,
                overwritten: overwritten,
                createdDirectory: createdDirectory
            )
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_copy_failed")
        }
    }

    private func transferResult(
        operation: String,
        source: AnsightFileSystemResolvedPath,
        destination: AnsightFileSystemResolvedPath,
        roots: [AnsightFileSystemRoot],
        overwritten: Bool,
        createdDirectory: Bool
    ) throws -> AnsightToolExecutionResult {
        let attributes = try AnsightFileSystemIO.attributes(path: destination.fullPath)
        guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
            resolvedFile: destination,
            roots: roots,
            attributes: attributes
        ) else {
            return .failure("Failed to build copy payload.", errorCode: "filesystem_copy_failed")
        }

        payload["operation"] = .string(operation)
        payload["sourceRootAlias"] = .string(source.rootAlias)
        payload["sourceRootPath"] = .string(source.rootPath)
        payload["sourceFilePath"] = .string(source.fullPath)
        payload["sourceRelativePath"] = .string(AnsightFileSystemSandbox.relativePath(rootPath: source.rootPath, fullPath: source.fullPath))
        payload["destinationRootAlias"] = .string(destination.rootAlias)
        payload["destinationRootPath"] = .string(destination.rootPath)
        payload["destinationFilePath"] = .string(destination.fullPath)
        payload["destinationRelativePath"] = .string(AnsightFileSystemSandbox.relativePath(rootPath: destination.rootPath, fullPath: destination.fullPath))
        payload["overwritten"] = .bool(overwritten)
        payload["createdDirectory"] = .bool(createdDirectory)
        payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
        return .success(.object(payload))
    }
}
