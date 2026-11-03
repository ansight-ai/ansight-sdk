import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Upload-only credentials belong to the caller's secure configuration. They are
/// never serialized into capture manifests or exported evidence.
public struct OfflineCaptureUploadConfiguration: Sendable {
    public let endpoint: URL
    public let apiKey: String

    public init(endpoint: URL, apiKey: String) throws {
        let local = endpoint.host == "localhost" || endpoint.host == "127.0.0.1"
        let secureTransport = endpoint.scheme == "https" || (endpoint.scheme == "http" && local)
        let validConfiguration = secureTransport
            && endpoint.host != nil
            && endpoint.user == nil
            && endpoint.password == nil
            && (apiKey.hasPrefix("an_cap_") || apiKey.hasPrefix("an_run_"))
        guard validConfiguration else {
            throw OfflineCaptureError.invalidOptions
        }
        self.endpoint = endpoint
        self.apiKey = apiKey
    }
}

public struct OfflineCaptureUploadResult: Sendable {
    public let sessionIds: [String]
    public let acknowledgedChunks: Int
    public let pendingChunks: Int
}

private final class OfflineUploadSessionDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

/// A serialized, resumable upload pass. Call again after connectivity returns or
/// while server processing is pending. Only hash-confirmed ready chunks are removed.
public actor OfflineCaptureCloudUploader {
    private let store: OfflineCaptureStore
    private let configuration: OfflineCaptureUploadConfiguration
    private let session: URLSession
    private var uploading = false

    public init(store: OfflineCaptureStore, configuration: OfflineCaptureUploadConfiguration) {
        self.store = store
        self.configuration = configuration
        let settings = URLSessionConfiguration.ephemeral
        settings.timeoutIntervalForRequest = 60
        settings.timeoutIntervalForResource = 120
        self.session = URLSession(configuration: settings, delegate: OfflineUploadSessionDelegate(), delegateQueue: nil)
    }

    public func uploadPending() async throws -> OfflineCaptureUploadResult {
        guard !uploading else { throw OfflineCaptureError.sessionBusy }
        uploading = true
        defer { uploading = false }
        // sessions() is a queue barrier. A retention error must not prevent
        // uploading the sealed chunks that will free the retained-byte budget.
        let captures = try await store.sessions().filter { $0.captureId != nil && $0.stoppedAtUtc != nil }
        var sessionIds = Set<String>()
        var acknowledged = 0
        var pending = 0
        var processingFailure: OfflineCaptureError?
        for capture in captures {
            try Task.checkCancellation()
            guard let captureId = capture.captureId, let sequence = capture.sequence,
                  let stoppedAt = capture.stoppedAtUtc else { throw OfflineCaptureError.invalidReceipt }
            let lease = try await store.acquire(capture.sessionId)
            let destination = FileManager.default.temporaryDirectory.appendingPathComponent("ansight-cloud-\(UUID().uuidString).zip")
            var released = false
            do {
                let archive = try OfflineCaptureArchive.write(lease, to: destination)
                let begun = try await request([
                    "action": "begin", "sessionId": captureId, "appId": capture.appId,
                    "startedAtUtc": capture.captureStartedAtUtc ?? capture.startedAtUtc,
                ])
                guard let sessionId = begun["sessionId"] as? String else { throw OfflineCaptureError.invalidReceipt }
                sessionIds.insert(sessionId)
                let reserved = try await request([
                    "action": "chunk", "sessionId": sessionId, "sequence": sequence,
                    "sha256": archive.sha256, "byteSize": archive.byteCount,
                    "startedAtUtc": capture.startedAtUtc, "endedAtUtc": stoppedAt,
                ])
                if let signedUrl = reserved["uploadUrl"] as? String {
                    guard let url = URL(string: signedUrl, relativeTo: configuration.endpoint)?.absoluteURL else {
                        throw OfflineCaptureError.invalidReceipt
                    }
                    let trustedOrigin = url.host == configuration.endpoint.host
                        && url.port == configuration.endpoint.port
                        && url.scheme == configuration.endpoint.scheme
                    guard trustedOrigin else { throw OfflineCaptureError.invalidReceipt }
                    var upload = URLRequest(url: url)
                    upload.httpMethod = "PUT"
                    upload.setValue("application/zip", forHTTPHeaderField: "Content-Type")
                    let (_, response) = try await session.upload(for: upload, fromFile: destination)
                    guard let response = response as? HTTPURLResponse else { throw OfflineCaptureError.invalidReceipt }
                    // A previous attempt may have uploaded before losing its response.
                    let uploadAccepted = (200..<300).contains(response.statusCode)
                        || response.statusCode == 409
                        || response.statusCode == 400
                    guard uploadAccepted else {
                        throw OfflineCaptureError.persistenceFailed("Chunk upload failed with HTTP \(response.statusCode).")
                    }
                }
                _ = try await request(["action": "commit", "sessionId": sessionId, "sequence": sequence])
                let isFinal = capture.terminationKind != "sizeLimit" && capture.terminationKind != "chunkLimit"
                if isFinal {
                    _ = try await request(["action": "finish", "sessionId": sessionId, "finalSequence": sequence])
                }
                let status = try await request(["action": "status", "sessionId": sessionId])
                let chunks = status["chunks"] as? [[String: Any]] ?? []
                let receipt = chunks.first { ($0["sequence"] as? Int) == sequence }
                if receipt?["status"] as? String == "failed" {
                    processingFailure = .persistenceFailed(receipt?["failureMessage"] as? String ?? "Chunk processing failed.")
                }
                if receipt?["status"] as? String == "ready" && receipt?["sha256"] as? String == archive.sha256 {
                    try await store.acknowledge(lease, archiveSha256: archive.sha256, acknowledgedSha256: archive.sha256)
                    acknowledged += 1
                } else {
                    try await store.release(lease)
                    pending += 1
                }
                released = true
            } catch {
                if !released { try? await store.release(lease) }
                try? FileManager.default.removeItem(at: destination)
                throw error
            }
            try? FileManager.default.removeItem(at: destination)
        }
        if let processingFailure { throw processingFailure }
        return OfflineCaptureUploadResult(sessionIds: sessionIds.sorted(), acknowledgedChunks: acknowledged, pendingChunks: pending)
    }

    private func request(_ body: [String: Any]) async throws -> [String: Any] {
        var request = URLRequest(url: configuration.endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(configuration.apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data, response) = try await session.data(for: request)
        guard data.count <= 2 * 1024 * 1024 else { throw OfflineCaptureError.invalidReceipt }
        guard let response = response as? HTTPURLResponse else { throw OfflineCaptureError.invalidReceipt }
        guard (200..<300).contains(response.statusCode) else {
            throw OfflineCaptureError.persistenceFailed("Capture API request failed with HTTP \(response.statusCode).")
        }
        guard let result = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw OfflineCaptureError.invalidReceipt
        }
        return result
    }
}
