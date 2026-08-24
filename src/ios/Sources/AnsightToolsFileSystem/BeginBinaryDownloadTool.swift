import AnsightCore
import Foundation

public final class BeginBinaryDownloadTool: AnsightTool {
    private let options: AnsightFileSystemToolsOptions
    private let runtime: AnsightRuntime

    public init(options: AnsightFileSystemToolsOptions = .default, runtime: AnsightRuntime = .shared) {
        self.options = options
        self.runtime = runtime
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileSystemToolIds.beginBinaryDownload,
            name: "Begin Binary Download",
            description: "Starts a binary WebSocket download of a sandboxed file.",
            category: "files",
            policy: .read,
            keywords: "filesystem file download websocket binary sandbox",
            argumentsSchema: AnsightFileSystemToolSchemas.beginBinaryDownloadArguments,
            resultSchema: AnsightFileSystemToolSchemas.beginBinaryDownloadResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightFileSystemSandbox.roots(options: options)
            guard let requestId = AnsightFileSystemSandbox.string(arguments, key: AnsightToolExecutionArgumentNames.requestId) else {
                return .failure(
                    "Binary downloads require a live tool protocol request context.",
                    errorCode: "filesystem_binary_download_unavailable"
                )
            }

            let resolvedFile = try AnsightFileSystemSandbox.resolvePath(
                arguments: arguments,
                roots: roots,
                requireExisting: true,
                expectDirectory: false
            )
            let chunkBytes = try AnsightFileSystemSandbox.integer(
                arguments,
                key: "chunkBytes",
                defaultValue: 64 * 1_024,
                minimum: 1_024,
                maximum: 512 * 1_024
            )
            let attributes = try AnsightFileSystemIO.attributes(path: resolvedFile.fullPath)
            let data = try Data(contentsOf: URL(fileURLWithPath: resolvedFile.fullPath), options: [.mappedIfSafe])
            let transferId = UUID()
            let downloadId = AnsightFileSystemSandbox.string(arguments, key: "downloadId") ?? requestId
            let queueResult = runtime.queueBinaryTransfer(
                requestId: requestId,
                transferId: transferId,
                data: data,
                chunkBytes: chunkBytes,
                description: "\(AnsightFileSystemToolIds.beginBinaryDownload):\(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
            )
            guard queueResult.success else {
                return .failure(
                    queueResult.message,
                    errorCode: "filesystem_binary_download_unavailable"
                )
            }

            guard case .object(var payload) = AnsightFileSystemContentDescriptor.resolvedFilePayload(
                resolvedFile: resolvedFile,
                roots: roots,
                attributes: attributes
            ) else {
                return .failure("Failed to build binary download payload.", errorCode: "filesystem_binary_download_failed")
            }

            payload["downloadId"] = .string(downloadId)
            payload["transferId"] = .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())
            payload["deliveryMode"] = .string("websocket_binary")
            payload["wireProtocol"] = .string(PairingFileTransferWireProtocol.protocolName)
            payload["status"] = .string("queued")
            payload["chunkBytes"] = .integer(Int64(chunkBytes))
            payload["capturedAtUtc"] = .string(AnsightClock.isoNow())

            return .success(.object(payload))
        } catch {
            return .failure(error.localizedDescription, errorCode: "filesystem_binary_download_failed")
        }
    }
}
