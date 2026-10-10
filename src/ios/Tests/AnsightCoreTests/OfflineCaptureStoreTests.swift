import XCTest
@testable import AnsightCore

final class OfflineCaptureStoreTests: XCTestCase {
    private func root() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        return url
    }

    private func recordSession(_ store: OfflineCaptureStore) async throws {
        store.setRecording(true)
        XCTAssertTrue(store.append(json: Data("{\"metrics\":[{\"value\":1,\"channel\":1}]}".utf8)))
        store.setRecording(false)
        try await store.flush()
        XCTAssertNil(store.lastError)
    }

    func testDefaultsAreOptInWithAutomaticTransferAndTenSessions() {
        let options = OfflineCaptureOptions()
        XCTAssertFalse(options.enabled)
        XCTAssertEqual(options.transferMode, .automatic)
        XCTAssertEqual(options.maximumSessions, 10)
    }

    func testKeepsNewestTenWholeSessions() async throws {
        let store = try OfflineCaptureStore(root: root(), options: .init(enabled: true), appId: "test", processSessionId: "process")
        try await recordSession(store)
        let firstSessions = try await store.sessions()
        let first = try XCTUnwrap(firstSessions.first)
        for _ in 0..<11 { try await recordSession(store) }
        let retained = try await store.sessions()
        XCTAssertEqual(retained.count, 10)
        XCTAssertFalse(retained.contains { $0.sessionId == first.sessionId })
        XCTAssertTrue(retained.allSatisfy { $0.records == 1 && $0.stoppedAtUtc != nil && $0.bytes > 0 })
    }

    func testLeaseProtectsSessionAndWrongReceiptDoesNotPurge() async throws {
        let store = try OfflineCaptureStore(root: root(), options: .init(enabled: true, maximumSessions: 2), appId: "test", processSessionId: "process")
        try await recordSession(store)
        let sessions = try await store.sessions()
        let first = try XCTUnwrap(sessions.first)
        let lease = try await store.acquire(first.sessionId)
        for _ in 0..<3 { try await recordSession(store) }
        let retained = try await store.sessions()
        XCTAssertTrue(retained.contains { $0.sessionId == first.sessionId })
        let hash = String(repeating: "a", count: 64)
        do {
            try await store.acknowledge(lease, archiveSha256: hash, acknowledgedSha256: String(repeating: "b", count: 64))
            XCTFail("Mismatched receipt accepted")
        } catch OfflineCaptureError.invalidReceipt {}
        XCTAssertTrue(FileManager.default.fileExists(atPath: lease.directory.path))
        try await store.acknowledge(lease, archiveSha256: hash, acknowledgedSha256: hash)
        XCTAssertFalse(FileManager.default.fileExists(atPath: lease.directory.path))
    }

    func testDisabledAndOversizedAdmissionsDoNotWriteAndFlushStillCompletes() async throws {
        let store = try OfflineCaptureStore(root: root(), options: .init(enabled: true, maximumQueuedBytes: 8), appId: "test", processSessionId: "process")
        XCTAssertFalse(store.append(json: Data("{}".utf8)))
        store.setRecording(true)
        XCTAssertFalse(store.append(json: Data(repeating: 65, count: 20)))
        XCTAssertTrue(store.append(json: Data("{}".utf8)))
        store.setRecording(false)
        try await store.flush()
        let sessions = try await store.sessions()
        XCTAssertEqual(sessions.first?.records, 1)
        XCTAssertEqual(sessions.first?.droppedRecords, 1)
    }

    func testRecoverySealsInterruptedSessionAndRecountsPayload() async throws {
        let directory = try root()
        let old = try OfflineCaptureStore(root: directory, options: .init(enabled: true), appId: "test", processSessionId: "old")
        try await recordSession(old)
        let sessions = try await old.sessions()
        let first = try XCTUnwrap(sessions.first)
        let manifest = directory.appendingPathComponent("sessions/\(first.sessionId)/manifest.json")
        var json = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: manifest)) as? [String: Any])
        json.removeValue(forKey: "stoppedAtUtc")
        json.removeValue(forKey: "terminationKind")
        json["bytes"] = 0
        try JSONSerialization.data(withJSONObject: json).write(to: manifest)
        let resumed = try OfflineCaptureStore(root: directory, options: .init(enabled: true), appId: "test", processSessionId: "new")
        resumed.setRecording(true)
        try await resumed.flush()
        let recovered = try await resumed.sessions().first { $0.sessionId == first.sessionId }
        XCTAssertEqual(recovered?.terminationKind, "interrupted")
        XCTAssertEqual(recovered?.bytes, first.bytes)
        resumed.setRecording(false)
        try await resumed.flush()
    }

    func testByteBudgetEvictsWholeSessions() async throws {
        let store = try OfflineCaptureStore(root: root(), options: .init(enabled: true, maximumSessionBytes: 10, maximumRetainedBytes: 15), appId: "test", processSessionId: "process")
        for _ in 0..<3 {
            store.setRecording(true)
            XCTAssertTrue(store.append(json: Data("{\"a\":1}".utf8)))
            store.setRecording(false)
            try await store.flush()
        }
        let sessions = try await store.sessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions.first?.bytes, 8)
    }

    func testArchiveIsReadableAndStableAcrossRetries() async throws {
        let root = try root()
        let store = try OfflineCaptureStore(root: root, options: .init(enabled: true), appId: "test", processSessionId: "process")
        try await recordSession(store)
        let sessions = try await store.sessions()
        let lease = try await store.acquire(XCTUnwrap(sessions.first).sessionId)
        let first = try OfflineCaptureArchive.write(lease, to: root.appendingPathComponent("first.zip"))
        let second = try OfflineCaptureArchive.write(lease, to: root.appendingPathComponent("second.zip"))
        XCTAssertEqual(first.sha256, second.sha256)
        XCTAssertEqual(first.byteCount, second.byteCount)
        XCTAssertEqual(first.sha256.count, 64)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = ["-p", first.path.path, "records.jsonl"]
        let output = Pipe()
        process.standardOutput = output
        process.standardError = Pipe()
        try process.run()
        let records = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        XCTAssertEqual(process.terminationStatus, 0)
        XCTAssertEqual(records, Data("{\"metrics\":[{\"value\":1,\"channel\":1}]}\n".utf8))
        try await store.release(lease)
    }

    func testRetentionNeverDeletesAnActiveLeaseWhenThereIsNoRoom() async throws {
        let store = try OfflineCaptureStore(root: root(), options: .init(enabled: true, maximumSessions: 1), appId: "test", processSessionId: "process")
        try await recordSession(store)
        let sessions = try await store.sessions()
        let lease = try await store.acquire(XCTUnwrap(sessions.first).sessionId)
        store.setRecording(true)
        do { try await store.flush(); XCTFail("Blocked retention was reported as success") }
        catch OfflineCaptureError.persistenceFailed {}
        XCTAssertTrue(FileManager.default.fileExists(atPath: lease.directory.path))
        store.setRecording(false)
        try await store.release(lease)
    }

    func testCorruptManifestFailsVisiblyAndPreservesEvidence() async throws {
        let root = try root()
        let session = root.appendingPathComponent("sessions/\(UUID().uuidString.lowercased())")
        try FileManager.default.createDirectory(at: session, withIntermediateDirectories: true)
        try Data("corrupt".utf8).write(to: session.appendingPathComponent("manifest.json"))
        let store = try OfflineCaptureStore(root: root, options: .init(enabled: true), appId: "test", processSessionId: "process")
        store.setRecording(true)
        do { try await store.flush(); XCTFail("Corrupt storage was reported as success") }
        catch OfflineCaptureError.persistenceFailed {}
        XCTAssertTrue(FileManager.default.fileExists(atPath: session.path))
        store.setRecording(false)
    }

    func testAttachmentAndIndexAreAdmittedTogether() async throws {
        let root = try root()
        let store = try OfflineCaptureStore(root: root, options: .init(enabled: true, maximumQueuedBytes: 1024), appId: "test", processSessionId: "process")
        store.setRecording(true)
        XCTAssertFalse(store.appendAttachment(data: Data(repeating: 1, count: 2048), extension: "jpg") { _ in Data("{}".utf8) })
        XCTAssertTrue(try store.appendAttachment(data: Data([1, 2, 3]), extension: "jpg") { path in
            try JSONSerialization.data(withJSONObject: ["path": path])
        })
        store.setRecording(false)
        try await store.flush()
        let sessions = try await store.sessions()
        let session = try XCTUnwrap(sessions.first)
        XCTAssertEqual(session.records, 1)
        let directory = root.appendingPathComponent("sessions/\(session.sessionId)")
        let records = try Data(contentsOf: directory.appendingPathComponent("records.jsonl"))
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: records) as? [String: String])
        let path = try XCTUnwrap(json["path"])
        XCTAssertEqual(try Data(contentsOf: directory.appendingPathComponent(path)), Data([1, 2, 3]))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: directory.appendingPathComponent("attachments").path).count, 1)
    }
}
