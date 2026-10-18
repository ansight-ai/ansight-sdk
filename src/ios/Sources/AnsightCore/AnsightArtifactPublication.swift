import Foundation

internal enum AnsightArtifactPublication {
    static let maximumSizeBytes = 512 * 1024 * 1024

    static func send(metadata: JSONValue, data: Data,
        control: @Sendable (String, JSONValue) async -> OperationResult,
        binary: @Sendable (Data) async -> OperationResult) async -> OperationResult {
        guard case .object(var values) = metadata else { return .failure("Artifact metadata must be an object.") }
        for key in ["artifactId", "providerId", "name", "kind", "mimeType", "fileName"] {
            guard case .string(let value) = values[key], !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return .failure("Artifact metadata requires '\(key)'.")
            }
        }
        guard data.count <= maximumSizeBytes else { return .failure("Published artifacts must be at most 512 MiB.") }
        if let size = values["sizeBytes"], size != .null, size != .integer(Int64(data.count)) {
            return .failure("Artifact metadata size does not match its payload.")
        }
        values["sizeBytes"] = .integer(Int64(data.count))
        if values["createdAtUtc"] == nil { values["createdAtUtc"] = .string(AnsightClock.isoNow()) }
        if Task.isCancelled { return .failure("Artifact publication cancelled.") }
        let transferId = UUID()
        let identity: JSONValue = .object(["transferId": .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())])
        guard case .object(var begin) = identity else { return .failure("Invalid transfer identity.") }
        begin["schema"] = .string("ansight.artifact.publish.v1")
        begin["wireProtocol"] = .string(PairingFileTransferWireProtocol.protocolName)
        begin["artifact"] = .object(values)
        let ready = await control("artifact.publish.begin", .object(begin))
        guard ready.success else { return ready }
        var result = OperationResult.success("Artifact bytes sent.")
        var offset = 0
        var sequence: Int32 = 0
        while offset < data.count && result.success {
            if Task.isCancelled { result = .failure("Artifact publication cancelled."); break }
            let end = min(offset + 64 * 1024, data.count)
            result = await binary(PairingFileTransferWireProtocol.createFrame(transferId: transferId,
                frameType: .chunk, sequence: sequence, offsetBytes: Int64(offset), payload: data.subdata(in: offset..<end)))
            sequence += 1
            offset = end
        }
        if result.success && Task.isCancelled { result = .failure("Artifact publication cancelled.") }
        if result.success {
            result = await binary(PairingFileTransferWireProtocol.createFrame(transferId: transferId,
                frameType: .complete, sequence: sequence, offsetBytes: Int64(offset), payload: Data()))
        }
        if result.success && Task.isCancelled { result = .failure("Artifact publication cancelled.") }
        if result.success { result = await control("artifact.publish.commit", identity) }
        if !result.success { _ = await control("artifact.publish.abort", identity) }
        return result
    }
}
