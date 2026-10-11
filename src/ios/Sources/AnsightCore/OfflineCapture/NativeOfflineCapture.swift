import Foundation

/// Native runtime ownership of recording and acknowledged reconnect delivery.
final class NativeOfflineCapture: @unchecked Sendable {
    let store: OfflineCaptureStore
    private let exportDirectory: URL
    private let options: OfflineCaptureOptions
    private let transport: PairingLiveSessionTransport
    private let lock = NSLock()
    private var connected = false
    private var active = false
    private var generation = 0
    private var transferring = false
    private var transferError: String?

    init(store: OfflineCaptureStore, options: OfflineCaptureOptions, transport: PairingLiveSessionTransport) {
        self.store = store
        self.exportDirectory = FileManager.default.temporaryDirectory.appendingPathComponent("AnsightOfflineExports").appendingPathComponent(store.exportIdentifier)
        self.options = options
        self.transport = transport
    }

    var lastError: String? { store.lastError ?? lock.withLock { transferError } }

    func update(active: Bool, connected: Bool) {
        let shouldTransfer = lock.withLock { () -> Bool in
            let reconnected = connected && (!self.connected || (active && !self.active))
            if self.connected != connected || self.active != active { generation += 1 }
            self.active = active
            self.connected = connected
            store.setRecording(active && !connected)
            return active && reconnected && options.transferMode == .automatic
        }
        if shouldTransfer { _ = transfer() }
    }

    @discardableResult
    func append(_ record: JSONValue) -> Bool {
        guard store.isRecording, let json = try? record.jsonData() else { return false }
        return store.append(json: json)
    }

    /// Runs off the control-message receive pump; a manual tool never blocks its acknowledgement.
    func transfer() -> OperationResult {
        let token = lock.withLock { () -> Int? in
            guard active, connected, !transferring else { return nil }
            transferring = true
            transferError = nil
            return generation
        }
        guard let token else { return .failure("Offline capture transfer requires an active host connection and no transfer in progress.") }
        Task.detached(priority: .utility) { [self] in
            defer {
                let retry = lock.withLock { () -> Bool in
                    transferring = false
                    return generation != token && active && connected && options.transferMode == .automatic
                }
                if retry { _ = transfer() }
            }
            do {
                try FileManager.default.createDirectory(at: exportDirectory, withIntermediateDirectories: true)
                for file in try FileManager.default.contentsOfDirectory(at: exportDirectory, includingPropertiesForKeys: nil)
                    where file.pathExtension == "zip" || file.pathExtension == "part" {
                    try FileManager.default.removeItem(at: file)
                }
                try await store.flush()
                for session in try await store.sessions() where session.stoppedAtUtc != nil {
                    try requireConnection(token)
                    try await transfer(session.sessionId, generation: token)
                }
            } catch {
                lock.withLock { transferError = error.localizedDescription }
            }
        }
        return .success("Offline capture transfer started.")
    }

    private func requireConnection(_ token: Int) throws {
        guard lock.withLock({ active && connected && generation == token }), transport.isOpen else {
            throw OfflineCaptureError.persistenceFailed("Host connection changed during offline capture transfer.")
        }
    }

    private func transfer(_ sessionId: String, generation: Int) async throws {
        let lease = try await store.acquire(sessionId)
        let path = exportDirectory.appendingPathComponent("ansight-offline-\(UUID().uuidString).zip")
        do {
            let archive = try OfflineCaptureArchive.write(lease, to: path)
            let transferId = UUID()
            let descriptor = JSONValue.object([
                "schema": .string("ansight.offline-transfer.v1"),
                "sessionId": .string(sessionId),
                "transferId": .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()),
                "byteCount": .integer(archive.byteCount), "sha256": .string(archive.sha256),
            ])
            try requireConnection(generation)
            let begin = await transport.sendControlRequest(action: "offline_capture.begin", payload: descriptor)
            guard begin.success else { throw OfflineCaptureError.persistenceFailed(begin.message) }
            let input = try FileHandle(forReadingFrom: path)
            defer { try? input.close() }
            var sequence: Int32 = 0
            var offset: Int64 = 0
            while let chunk = try OfflineCaptureFileIO.read(from: input) {
                try requireConnection(generation)
                let result = await transport.sendData(PairingFileTransferWireProtocol.createFrame(
                    transferId: transferId, frameType: .chunk, sequence: sequence, offsetBytes: offset, payload: chunk))
                guard result.success else { throw OfflineCaptureError.persistenceFailed(result.message) }
                sequence += 1
                offset += Int64(chunk.count)
            }
            try requireConnection(generation)
            let complete = await transport.sendData(PairingFileTransferWireProtocol.createFrame(
                transferId: transferId, frameType: .complete, sequence: sequence, offsetBytes: offset, payload: Data()))
            guard complete.success else { throw OfflineCaptureError.persistenceFailed(complete.message) }
            let commit = await transport.sendControlRequestWithResponse(action: "offline_capture.commit", payload: descriptor,
                acknowledgementTimeoutSeconds: 120)
            guard commit.operationResult.success,
                  case .object(let receipt) = commit.response?.payload,
                  receipt["sessionId"] == .string(sessionId), receipt["sha256"] == .string(archive.sha256),
                  receipt["durable"] == .bool(true) else { throw OfflineCaptureError.invalidReceipt }
            try await store.acknowledge(lease, archiveSha256: archive.sha256, acknowledgedSha256: archive.sha256)
        } catch {
            try? await store.release(lease)
            try? FileManager.default.removeItem(at: path)
            throw error
        }
        try? FileManager.default.removeItem(at: path)
    }
}
