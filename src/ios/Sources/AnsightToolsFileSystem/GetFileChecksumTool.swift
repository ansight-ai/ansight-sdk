import AnsightCore
import Foundation

public final class GetFileChecksumTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions

    public init(options: AnsightFileSystemToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.getFileChecksum,
            name: "Get File Checksum",
            description: "Computes content fingerprints for a sandboxed file.",
            category: "files",
            scope: AnsightToolScope.read.rawValue,
            keywords: "filesystem file checksum hash digest sandbox",
            security: AnsightFileSystemToolSecurityProfiles.getFileChecksum,
            argumentsSchema: AnsightFileSystemToolSchemas.getFileChecksumArguments,
            resultSchema: AnsightFileSystemToolSchemas.getFileChecksumResult
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
            let algorithms = try AnsightFileSystemChecksumSupport.algorithms(from: arguments)
            let data = try AnsightFileSystemIO.readAll(path: resolvedFile.fullPath)
            let attributes = try AnsightFileSystemIO.attributes(path: resolvedFile.fullPath)
            let checksums = try AnsightFileSystemChecksumSupport.checksums(data: data, algorithms: algorithms)

            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: resolvedFile,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build checksum payload.", errorCode: "filesystem_checksum_failed")
            }

            payload["checksums"] = .array(checksums.map { checksum in
                .object([
                    "algorithm": .string(checksum.algorithm),
                    "checksum": .string(checksum.checksum),
                    "encoding": .string(checksum.encoding),
                ])
            })
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())
            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_checksum_failed")
        }
    }
}
