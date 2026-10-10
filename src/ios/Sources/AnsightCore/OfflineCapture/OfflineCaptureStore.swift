import Foundation
import CryptoKit

/// A native, serialized session store. Producers only enqueue bounded work; disk IO
/// and lifecycle barriers share a serial queue so a flush can never be dropped.
public final class OfflineCaptureStore: @unchecked Sendable {
    private let queue = DispatchQueue(label: "ai.ansight.offline-capture", qos: .utility)
    private let ingressLock = NSLock()
    private let root: URL
    private let options: OfflineCaptureOptions
    private let appId: String
    private let processSessionId: String
    private let sessionHeader: Data?
    // Worker-owned metadata; admissionMetadataBytes is guarded by ingressLock.
    private var customProperties: Data?
    private var admissionMetadataBytes: Int64
    private let onSessionStarted: (@Sendable (String, String) -> Void)?
    private let onSessionCompleted: (@Sendable (String) -> Void)?
    var exportIdentifier: String { SHA256.hash(data: Data(root.path.utf8)).map { String(format: "%02x", $0) }.joined() }
    private let fileManager = FileManager.default
    private var accepting = false
    private var queuedBytes = 0
    private var droppedRecords: Int64 = 0
    private var failure: String?
    private var blockedByLease = false
    // Accessed only on queue.
    private var current: OfflineCaptureSession?
    private var writer: FileHandle?
    private var leases: [String: String] = [:]
    private var lastOrdinal: Int64 = 0
    private var recovered = false
    private var cachedSessions: [String: OfflineCaptureSession]?

    public init(root: URL, options: OfflineCaptureOptions, appId: String, processSessionId: String, sessionHeader: Data? = nil, customProperties: Data? = nil,
                onSessionStarted: (@Sendable (String, String) -> Void)? = nil,
                onSessionCompleted: (@Sendable (String) -> Void)? = nil) throws {
        self.root = root.standardizedFileURL.resolvingSymlinksInPath()
        self.options = try options.validated()
        self.appId = appId
        self.processSessionId = processSessionId
        self.sessionHeader = sessionHeader
        self.customProperties = customProperties
        self.admissionMetadataBytes = Int64((sessionHeader?.count ?? -1) + 1 + (customProperties?.count ?? 0))
        self.onSessionStarted = onSessionStarted
        self.onSessionCompleted = onSessionCompleted
        guard Int64((sessionHeader?.count ?? -1) + 1 + (customProperties?.count ?? 0)) < options.maximumSessionBytes else {
            throw OfflineCaptureError.invalidOptions
        }
    }

    public func updateCustomProperties(json: Data) {
        ingressLock.withLock {
            let metadataBytes = Int64(json.count + (sessionHeader?.count ?? -1) + 1)
            guard json.count <= options.maximumQueuedBytes - queuedBytes, metadataBytes < options.maximumSessionBytes else { droppedRecords += 1; return }
            admissionMetadataBytes = metadataBytes
            queuedBytes += json.count
            queue.async {
                defer { self.ingressLock.withLock { self.queuedBytes -= json.count } }
                self.run {
                    guard Int64(json.count + (self.sessionHeader?.count ?? -1) + 1) < self.options.maximumSessionBytes else {
                        throw OfflineCaptureError.invalidOptions
                    }
                    let delta = Int64(json.count - (self.customProperties?.count ?? 0))
                    self.customProperties = json
                    guard let session = self.current else { return }
                    if session.bytes + delta > self.options.maximumSessionBytes {
                        try self.seal(reason: "sizeLimit")
                        try self.start()
                        return
                    }
                    try self.enforceRetention(reservingSessions: 0, reservingBytes: max(0, delta))
                    let path = self.directory(session.sessionId).appendingPathComponent("metadata/custom-properties.json")
                    try self.fileManager.createDirectory(at: path.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try json.write(to: path, options: .atomic)
                    self.current!.bytes += delta
                    try self.persistCurrent()
                }
            }
        }
    }

    public var isRecording: Bool { ingressLock.withLock { accepting } }
    public var lastError: String? { ingressLock.withLock { failure } }

    /// Orders the transition with producer admissions. Call on runtime activation,
    /// trusted host connect/disconnect, and access-policy changes.
    public func setRecording(_ enabled: Bool) {
        ingressLock.withLock {
            let requested = enabled && options.enabled
            guard accepting != requested else { return }
            accepting = requested
            queue.async {
                self.run {
                    if requested { try self.start() }
                    else { try self.seal(reason: "normal") }
                }
            }
        }
    }

    /// JSON must be a complete host-compatible JSONL record, without a trailing newline.
    @discardableResult
    public func append(json: Data) -> Bool {
        enqueue(json: json, attachment: nil)
    }

    /// The attachment and its index are admitted together and written on the same
    /// queue. The index references the generated, relative attachment path.
    @discardableResult
    public func appendAttachment(data: Data, extension fileExtension: String,
                                 index: @Sendable (String) throws -> Data) rethrows -> Bool {
        guard !fileExtension.isEmpty,
              fileExtension.utf8.allSatisfy({ ($0 >= 97 && $0 <= 122) || ($0 >= 48 && $0 <= 57) }) else {
            return false
        }
        let path = "attachments/\(UUID().uuidString.lowercased()).\(fileExtension)"
        return enqueue(json: try index(path), attachment: Attachment(path: path, data: data))
    }

    @discardableResult
    public func appendScreenshot(jpeg: Data, capturedAtUtc: String, width: Int, height: Int, quality: Int) -> Bool {
        let path = "screenshots/\(UUID().uuidString.lowercased()).jpg"
        guard let index = try? JSONSerialization.data(withJSONObject: ["path": path, "capturedAtUtc": capturedAtUtc,
            "width": width, "height": height, "quality": quality]) else { return false }
        return enqueue(json: index, attachment: Attachment(path: path, data: jpeg), indexPath: "screenshots/index/frames.jsonl")
    }

    /// A barrier behind accepted writes and lifecycle transitions; never competes
    /// with producer records for admission capacity.
    public func flush() async throws {
        try await perform {
            if let error = self.lastError { throw OfflineCaptureError.persistenceFailed(error) }
            try self.writer?.synchronize()
            try self.persistCurrent()
        }
    }

    public func sessions() async throws -> [OfflineCaptureSession] {
        try await perform {
            try self.recover()
            try self.persistCurrent()
            return try self.readSessions()
        }
    }

    public func acquire(_ sessionId: String) async throws -> OfflineCaptureLease {
        try await perform {
            try self.recover()
            guard self.current?.sessionId != sessionId, self.leases[sessionId] == nil else {
                throw OfflineCaptureError.sessionBusy
            }
            guard try self.readSessions().contains(where: { $0.sessionId == sessionId && $0.stoppedAtUtc != nil }) else {
                throw OfflineCaptureError.invalidSession
            }
            let token = UUID().uuidString.lowercased()
            self.leases[sessionId] = token
            return OfflineCaptureLease(sessionId: sessionId, token: token, directory: self.directory(sessionId))
        }
    }

    public func release(_ lease: OfflineCaptureLease) async throws {
        try await perform {
            try self.validate(lease)
            self.leases.removeValue(forKey: lease.sessionId)
            try self.enforceRetention(reservingSessions: 0, reservingBytes: 0)
            try self.resumeAfterLeaseChange()
        }
    }

    /// Invoke only after validating the authenticated host's durable import receipt
    /// against the session ID and SHA-256 of the exported archive.
    public func acknowledge(_ lease: OfflineCaptureLease, archiveSha256: String,
                            acknowledgedSha256: String) async throws {
        try await perform {
            try self.validate(lease)
            guard archiveSha256.count == 64,
                  archiveSha256.utf8.allSatisfy({ ($0 >= 48 && $0 <= 57) || ($0 >= 97 && $0 <= 102) }),
                  archiveSha256 == acknowledgedSha256 else {
                throw OfflineCaptureError.invalidReceipt
            }
            try self.fileManager.removeItem(at: self.directory(lease.sessionId))
            self.leases.removeValue(forKey: lease.sessionId)
            self.cachedSessions?.removeValue(forKey: lease.sessionId)
            try self.resumeAfterLeaseChange()
        }
    }

    private func resumeAfterLeaseChange() throws {
        let resume = ingressLock.withLock { () -> Bool in
            guard blockedByLease else { return false }
            failure = nil
            blockedByLease = false
            return accepting
        }
        if resume { try start() }
    }

    private struct Attachment: Sendable { let path: String; let data: Data }

    private func enqueue(json: Data, attachment: Attachment?, indexPath: String? = nil) -> Bool {
        let byteCount = json.count + 1 + (attachment?.data.count ?? 0)
        return ingressLock.withLock {
            guard accepting else { return false }
            guard byteCount <= options.maximumQueuedBytes - queuedBytes,
                  Int64(byteCount) + admissionMetadataBytes <= options.maximumSessionBytes else {
                droppedRecords += 1
                return false
            }
            queuedBytes += byteCount
            queue.async {
                defer { self.ingressLock.withLock { self.queuedBytes -= byteCount } }
                self.run {
                    if self.current == nil { try self.start() }
                    if self.current!.bytes + Int64(byteCount) > self.options.maximumSessionBytes {
                        try self.seal(reason: "sizeLimit")
                        try self.start()
                    }
                    try self.enforceRetention(reservingSessions: 0, reservingBytes: Int64(byteCount))
                    if let attachment {
                        let url = self.directory(self.current!.sessionId).appendingPathComponent(attachment.path)
                        try self.fileManager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                        try attachment.data.write(to: url, options: .atomic)
                    }
                    var line = json
                    line.append(10)
                    guard let writer = self.writer else { throw CocoaError(.fileWriteUnknown) }
                    if let indexPath {
                        let file = self.directory(self.current!.sessionId).appendingPathComponent(indexPath)
                        try self.fileManager.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                        if !self.fileManager.fileExists(atPath: file.path) { self.fileManager.createFile(atPath: file.path, contents: nil) }
                        let indexWriter = try FileHandle(forWritingTo: file)
                        defer { try? indexWriter.close() }
                        indexWriter.seekToEndOfFile()
                        try OfflineCaptureFileIO.write(line, to: indexWriter)
                    } else { try OfflineCaptureFileIO.write(line, to: writer) }
                    self.current!.bytes += Int64(byteCount)
                    self.current!.records += 1
                }
            }
            return true
        }
    }

    private func start() throws {
        guard current == nil else { return }
        try recover()
        try enforceRetention(reservingSessions: 1, reservingBytes: 0)
        lastOrdinal = max(lastOrdinal + 1, Int64(Date().timeIntervalSince1970 * 1_000_000))
        let session = OfflineCaptureSession(
            sessionId: UUID().uuidString.lowercased(), startedAtUtc: Self.now(),
            ordinal: lastOrdinal,
            appId: appId, processSessionId: processSessionId, bytes: 0, records: 0, droppedRecords: 0)
        let directory = directory(session.sessionId)
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent("records.jsonl")
        guard fileManager.createFile(atPath: file.path, contents: nil) else {
            throw CocoaError(.fileWriteUnknown)
        }
        writer = try FileHandle(forWritingTo: file)
        current = session
        try persistCurrent()
        if var header = sessionHeader {
            header.append(10)
            guard Int64(header.count) <= options.maximumSessionBytes else { throw OfflineCaptureError.invalidOptions }
            try enforceRetention(reservingSessions: 0, reservingBytes: Int64(header.count))
            try OfflineCaptureFileIO.write(header, to: writer!)
            current!.bytes += Int64(header.count)
        }
        if let properties = customProperties {
            let path = directory.appendingPathComponent("metadata/custom-properties.json")
            try fileManager.createDirectory(at: path.deletingLastPathComponent(), withIntermediateDirectories: true)
            try enforceRetention(reservingSessions: 0, reservingBytes: Int64(properties.count))
            try properties.write(to: path, options: .atomic)
            current!.bytes += Int64(properties.count)
        }
        try persistCurrent()
        onSessionStarted?(session.sessionId, directory.path)
    }

    private func seal(reason: String) throws {
        guard current != nil else { return }
        try writer?.synchronize()
        try writer?.close()
        writer = nil
        current!.stoppedAtUtc = Self.now()
        current!.terminationKind = reason
        try persistCurrent()
        onSessionCompleted?(current!.sessionId)
        current = nil
    }

    private func persistCurrent() throws {
        guard var session = current else { return }
        session.droppedRecords = ingressLock.withLock { droppedRecords }
        current = session
        try writeManifest(session)
    }

    private func recover() throws {
        guard !recovered else { return }
        try fileManager.createDirectory(at: root.appendingPathComponent("sessions"), withIntermediateDirectories: true)
        for var session in try readSessions() where session.stoppedAtUtc == nil && session.sessionId != current?.sessionId {
            session.stoppedAtUtc = Self.now()
            session.terminationKind = "interrupted"
            try writeManifest(session)
        }
        lastOrdinal = max(lastOrdinal, try readSessions().map(\.ordinal).max() ?? 0)
        cachedSessions = Dictionary(uniqueKeysWithValues: try readSessions().map { ($0.sessionId, $0) })
        recovered = true
        try enforceRetention(reservingSessions: 0, reservingBytes: 0)
    }

    private func enforceRetention(reservingSessions: Int, reservingBytes: Int64) throws {
        let sessions = try readSessions()
        var count = sessions.count + reservingSessions
        var bytes = sessions.reduce(Int64(0)) { $0 + $1.bytes } + reservingBytes
        for session in sessions {
            if count <= options.maximumSessions && bytes <= options.maximumRetainedBytes { return }
            guard session.sessionId != current?.sessionId, leases[session.sessionId] == nil else { continue }
            try fileManager.removeItem(at: directory(session.sessionId))
            cachedSessions?.removeValue(forKey: session.sessionId)
            count -= 1
            bytes -= session.bytes
        }
        if count > options.maximumSessions || bytes > options.maximumRetainedBytes {
            throw OfflineCaptureError.retentionBlocked
        }
    }

    private static func isSessionId(_ value: String) -> Bool {
        UUID(uuidString: value) != nil || (value.count == 32 && value.utf8.allSatisfy { (48...57).contains($0) || (97...102).contains($0) })
    }

    private static func fractionalDate(_ value: String) -> Date? {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.date(from: value)
    }

    private func readSessions() throws -> [OfflineCaptureSession] {
        if var sessions = cachedSessions {
            if let current { sessions[current.sessionId] = current }
            return sessions.values.sorted { $0.ordinal == $1.ordinal ? $0.sessionId < $1.sessionId : $0.ordinal < $1.ordinal }
        }
        let sessionsRoot = root.appendingPathComponent("sessions")
        guard fileManager.fileExists(atPath: sessionsRoot.path) else { return [] }
        return try fileManager.contentsOfDirectory(at: sessionsRoot, includingPropertiesForKeys: [.isSymbolicLinkKey])
            .filter { try $0.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true }
            .filter { Self.isSessionId($0.lastPathComponent) }
            .map { url in
                var values = try JSONSerialization.jsonObject(with: Data(contentsOf: url.appendingPathComponent("manifest.json"))) as? [String: Any] ?? [:]
                // Adopt the previous managed archive layout in place. Its append-only
                // evidence remains untouched and its source session identity is retained.
                if values["ordinal"] == nil {
                    guard let started = values["startedAtUtc"] as? String,
                          let date = ISO8601DateFormatter().date(from: started) ?? Self.fractionalDate(started) else {
                        throw OfflineCaptureError.invalidSession
                    }
                    values["ordinal"] = Int64(date.timeIntervalSince1970 * 1_000_000)
                    values["bytes"] = 0
                    values["records"] = 0
                    values["droppedRecords"] = values["droppedRecordCount"] ?? 0
                    values["processSessionId"] = values["processSessionId"] as? String ?? "legacy"
                }
                var session = try JSONDecoder().decode(OfflineCaptureSession.self, from: JSONSerialization.data(withJSONObject: values))
                guard session.sessionId == url.lastPathComponent else { throw OfflineCaptureError.invalidSession }
                if let current, current.sessionId == session.sessionId { session = current }
                else { session.bytes = try self.payloadBytes(url) }
                return session
            }.sorted { $0.ordinal < $1.ordinal }
    }

    private func validate(_ lease: OfflineCaptureLease) throws {
        guard leases[lease.sessionId] == lease.token,
              lease.directory == directory(lease.sessionId), current?.sessionId != lease.sessionId else {
            throw OfflineCaptureError.invalidReceipt
        }
    }

    private func payloadBytes(_ directory: URL) throws -> Int64 {
        let keys: [URLResourceKey] = [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]
        guard let enumerator = fileManager.enumerator(at: directory, includingPropertiesForKeys: keys) else {
            throw CocoaError(.fileReadUnknown)
        }
        var bytes: Int64 = 0
        for case let file as URL in enumerator {
            let values = try file.resourceValues(forKeys: Set(keys))
            if values.isSymbolicLink == true { enumerator.skipDescendants(); continue }
            if values.isRegularFile == true && file.lastPathComponent != "manifest.json" {
                bytes += Int64(values.fileSize ?? 0)
            }
        }
        return bytes
    }

    private func directory(_ id: String) -> URL { root.appendingPathComponent("sessions").appendingPathComponent(id) }
    private func writeManifest(_ session: OfflineCaptureSession) throws {
        let path = directory(session.sessionId).appendingPathComponent("manifest.json")
        var manifest = fileManager.fileExists(atPath: path.path)
            ? (try JSONSerialization.jsonObject(with: Data(contentsOf: path)) as? [String: Any] ?? [:]) : [:]
        let values = try JSONSerialization.jsonObject(with: JSONEncoder().encode(session)) as! [String: Any]
        manifest.merge(values) { _, new in new }
        try JSONSerialization.data(withJSONObject: manifest, options: .sortedKeys).write(to: path, options: .atomic)
        cachedSessions?[session.sessionId] = session
    }
    private static func now() -> String { ISO8601DateFormatter().string(from: Date()) }
    private func run(_ work: () throws -> Void) {
        do { try work() }
        catch { ingressLock.withLock {
            failure = String(describing: error)
            if case OfflineCaptureError.retentionBlocked = error { blockedByLease = true } else { blockedByLease = false }
            droppedRecords += 1
        } }
    }
    private func perform<T: Sendable>(_ work: @escaping @Sendable () throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { continuation in
            queue.async { continuation.resume(with: Result { try work() }) }
        }
    }
}
