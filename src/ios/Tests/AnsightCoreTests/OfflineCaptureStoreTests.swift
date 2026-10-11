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

    func testNativeRuntimeRecordsWithoutHostAndHonorsAccessPolicy() async throws {
        let directory = try root()
        let runtime = AnsightRuntime.shared
        var options = AnsightOptions()
        options.additionalChannels = [AnsightChannel(id: 200, name: "Test", color: nil)]
        options.crashCapture.enabled = false
        options.hostAutoProbe.enabled = false
        options.offlineCapture = OfflineCaptureOptions(enabled: true, recordingEnabled: false,
            rootDirectory: directory.path, maximumSessions: 2)
        try runtime.initializeAndActivate(options: options)
        defer { runtime.deactivate() }
        XCTAssertTrue(runtime.isToolRegistered("offline_capture.transfer"))
        XCTAssertEqual(OfflineCaptureTransferToolSupport.descriptor.policy, .write)
        try runtime.metric(1, channel: 200)
        let before = try await runtime.offlineCaptureSessions()
        XCTAssertTrue(before.isEmpty)
        _ = await runtime.sendControlRequest(action: "offline_capture.set_enabled", payload: .object(["enabled": .bool(true)]))
        try runtime.metric(1234, channel: 200)
        let logResult = await runtime.sendClientLog("offline-runtime-log")
        XCTAssertTrue(logResult.success)
        var performance = AnsightNetworkPerformance()
        performance.timeToFirstByteMilliseconds = 125
        performance.downloadDurationMilliseconds = 250
        performance.downloadedBytes = 8_192
        let request = AnsightNetworkRequest(
            id: "offline-performance", source: "apple.nsurlprotocol",
            startedAtUtc: "2026-10-11T01:00:00Z", completedAtUtc: "2026-10-11T01:00:01Z",
            durationMilliseconds: 1_000, performance: performance,
            method: "GET", url: "https://example.test/download", statusCode: 200
        )
        let requestJson = try XCTUnwrap(String(data: JSONEncoder().encode(request), encoding: .utf8))
        let networkResult = await runtime.recordNetworkRequest(json: requestJson)
        XCTAssertTrue(networkResult.success)
        let sessions = try await runtime.offlineCaptureSessions()
        let session = try XCTUnwrap(sessions.last)
        let records = try String(contentsOf: directory.appendingPathComponent("sessions/\(session.sessionId)/records.jsonl"), encoding: .utf8)
        XCTAssertTrue(records.contains("1234"))
        XCTAssertTrue(records.contains("offline-runtime-log"))
        let envelopes = try records.split(separator: "\n").map {
            try JSONSerialization.jsonObject(with: Data($0.utf8)) as? [String: Any]
        }
        let networkEnvelope = try XCTUnwrap(envelopes.compactMap { $0 }.first {
            $0["type"] as? String == "CLIENT_NETWORK_REQUEST"
        })
        let retainedRequest = try XCTUnwrap(networkEnvelope["request"] as? [String: Any])
        let retainedPerformance = try XCTUnwrap(retainedRequest["performance"] as? [String: Any])
        XCTAssertEqual(retainedPerformance["timeToFirstByteMilliseconds"] as? Double, 125)
        XCTAssertEqual(retainedPerformance["downloadDurationMilliseconds"] as? Double, 250)
        XCTAssertEqual(retainedPerformance["downloadedBytes"] as? Int, 8_192)
        XCTAssertNil(retainedRequest["requestBody"])
        XCTAssertNil(retainedRequest["responseBody"])
        XCTAssertFalse(runtime.hostConnectionStatus().isConnected)
        _ = await runtime.sendControlRequest(action: "offline_capture.set_enabled", payload: .object(["enabled": .bool(false)]))
        try runtime.metric(9876, channel: 200)
        _ = try await runtime.offlineCaptureSessions()
        let after = try String(contentsOf: directory.appendingPathComponent("sessions/\(session.sessionId)/records.jsonl"), encoding: .utf8)
        XCTAssertFalse(after.contains("9876"))
    }

    func testManagedArchiveIsAdoptedWithoutLosingManifestMetadata() async throws {
        let directory = try root()
        let id = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        let session = directory.appendingPathComponent("sessions/\(id)")
        try FileManager.default.createDirectory(at: session, withIntermediateDirectories: true)
        try JSONSerialization.data(withJSONObject: ["sessionId": id, "appId": "test", "startedAtUtc": "2026-10-11T01:00:00.1234567+00:00", "crashReportIds": ["existing-report"]])
            .write(to: session.appendingPathComponent("manifest.json"))
        try Data("{\"metrics\":[{\"value\":1,\"channel\":1}]}\n".utf8).write(to: session.appendingPathComponent("old.jsonl"))
        let store = try OfflineCaptureStore(root: directory, options: .init(enabled: true), appId: "test", processSessionId: "process")
        let sessions = try await store.sessions()
        XCTAssertEqual(sessions.first?.sessionId, id)
        XCTAssertEqual(sessions.first?.terminationKind, "interrupted")
        let metadata = try JSONSerialization.jsonObject(with: Data(contentsOf: session.appendingPathComponent("manifest.json"))) as! [String: Any]
        XCTAssertEqual(metadata["crashReportIds"] as? [String], ["existing-report"])
        let lease = try await store.acquire(id)
        try await store.release(lease)
    }

    func testScreenshotIndexAndCustomPropertiesAreExported() async throws {
        let directory = try root()
        let store = try OfflineCaptureStore(root: directory, options: .init(enabled: true), appId: "test", processSessionId: "process",
            sessionHeader: Data("{\"channels\":[]}".utf8), customProperties: Data("{\"user\":{\"role\":\"tester\"}}".utf8))
        store.setRecording(true)
        XCTAssertTrue(store.appendScreenshot(jpeg: Data([255, 216, 255, 217]), capturedAtUtc: "2026-10-11T01:00:00Z", width: 1, height: 1, quality: 70))
        store.setRecording(false)
        try await store.flush()
        let sessions = try await store.sessions()
        let folder = directory.appendingPathComponent("sessions/\(try XCTUnwrap(sessions.last).sessionId)")
        let index = try JSONSerialization.jsonObject(with: Data(contentsOf: folder.appendingPathComponent("screenshots/index/frames.jsonl"))) as! [String: Any]
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent(index["path"] as! String).path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("metadata/custom-properties.json").path))
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
        try await store.release(lease)
        XCTAssertTrue(store.append(json: Data("{}".utf8)))
        store.setRecording(false)
        try await store.flush()
        XCTAssertNil(store.lastError)
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
    func testUpdatedMetadataIsAccountedForAcrossRotation() async throws {
        let directory = try root()
        let store = try OfflineCaptureStore(root: directory, options: .init(enabled: true, maximumSessionBytes: 512),
            appId: "test", processSessionId: "process", sessionHeader: Data("{\"channels\":[]}".utf8), customProperties: Data("{}".utf8))
        store.setRecording(true)
        XCTAssertTrue(store.appendScreenshot(jpeg: Data([255, 216, 255, 217]), capturedAtUtc: "2026-10-11T01:00:00Z", width: 1, height: 1, quality: 70))
        let updated = Data("{\"user\":{\"role\":\"updated\"}}".utf8)
        store.updateCustomProperties(json: updated)
        try await store.flush()
        let initialSessions = try await store.sessions()
        let folder = directory.appendingPathComponent("sessions/\(try XCTUnwrap(initialSessions.first).sessionId)")
        XCTAssertEqual(try Data(contentsOf: folder.appendingPathComponent("metadata/custom-properties.json")), updated)
        XCTAssertTrue(store.append(json: Data(repeating: 32, count: 400)))
        store.setRecording(false)
        try await store.flush()
        let sessions = try await store.sessions()
        XCTAssertEqual(sessions.count, 2)
        XCTAssertTrue(sessions.allSatisfy { $0.bytes <= 512 })
        let newest = directory.appendingPathComponent("sessions/\(try XCTUnwrap(sessions.last).sessionId)/metadata/custom-properties.json")
        XCTAssertEqual(try Data(contentsOf: newest), updated)
    }

}
