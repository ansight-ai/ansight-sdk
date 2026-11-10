import XCTest
import CAnsightCrashCapture
@testable import AnsightCore

final class AnsightCrashCaptureTests: XCTestCase {
    private func root() throws -> URL {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        return root
    }

    private func capture(_ root: URL, _ id: String, options: AnsightCrashCaptureOptions = .init()) -> AnsightCrashCapture {
        let capture = AnsightCrashCapture(rootDirectory: root, processSessionId: id, installPlatformHandlers: false)
        capture.initialize(options: options)
        return capture
    }

    private func record(_ capture: AnsightCrashCapture, fatal: Bool, runtime: String = "dotnet") {
        XCTAssertNotNil(capture.recordCandidate(runtime: runtime, kind: "unhandled_exception", message: "failure", stack: "managed stack", fatal: fatal))
    }

    private func reports(_ capture: AnsightCrashCapture) throws -> [[String: Any]] {
        let payload = try JSONSerialization.jsonObject(with: Data(capture.pendingReportsJSON().utf8)) as! [String: Any]
        return payload["reports"] as! [[String: Any]]
    }

    func testLaterNonfatalCandidateDoesNotHideFatalException() throws {
        let root = try root()
        let first = capture(root, "first")
        record(first, fatal: true)
        record(first, fatal: false, runtime: "other")
        let recovered = capture(root, "second")
        let report = try XCTUnwrap(reports(recovered).first)
        XCTAssertEqual(try reports(recovered).count, 1)
        XCTAssertEqual((report["candidates"] as? [[String: Any]])?.count, 2)
        XCTAssertEqual((report["candidate"] as? [String: Any])?["runtime"] as? String, "dotnet")
    }

    func testWriteFailureRetainsEvidenceAndRetriesAcrossAnotherLaunch() throws {
        let root = try root()
        let first = capture(root, "first")
        first.recordBreadcrumb(kind: "event", label: "before crash")
        record(first, fatal: true)
        let pending = root.appendingPathComponent("pending")
        try FileManager.default.removeItem(at: pending)
        try Data("blocked".utf8).write(to: pending)
        let second = capture(root, "second")
        XCTAssertTrue(try reports(second).isEmpty)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: root.appendingPathComponent("raw").path).count, 1)
        second.recordBreadcrumb(kind: "event", label: "different process")
        try FileManager.default.removeItem(at: pending)
        let third = capture(root, "third")
        let report = try XCTUnwrap(reports(third).first)
        XCTAssertEqual(report["previousProcessSessionId"] as? String, "first")
        XCTAssertEqual((report["breadcrumbs"] as? [[String: Any]])?.first?["label"] as? String, "before crash")
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: root.appendingPathComponent("raw").path).isEmpty)
    }

    func testSignalRecoveryKeepsNonfatalManagedStackAndDoesNotConsumeOnWriteFailure() throws {
        let root = try root()
        let first = capture(root, "first")
        record(first, fatal: false)
        let signals = root.appendingPathComponent("signals")
        try FileManager.default.createDirectory(at: signals, withIntermediateDirectories: true)
        var signal = AnsightCrashSignalRecord()
        signal.version = 1
        signal.signalNumber = SIGABRT
        signal.processId = ProcessInfo.processInfo.processIdentifier
        signal.occurredAtEpochSeconds = Int64(Date().timeIntervalSince1970)
        let signalPath = signals.appendingPathComponent("first.raw")
        try Data(bytes: &signal, count: MemoryLayout<AnsightCrashSignalRecord>.size).write(to: signalPath)
        let pending = root.appendingPathComponent("pending")
        try FileManager.default.removeItem(at: pending)
        try Data().write(to: pending)
        _ = capture(root, "second")
        XCTAssertTrue(FileManager.default.fileExists(atPath: signalPath.path))
        try FileManager.default.removeItem(at: pending)
        let third = capture(root, "third")
        let report = try XCTUnwrap(reports(third).first)
        XCTAssertEqual((report["candidate"] as? [String: Any])?["stack"] as? String, "managed stack")
        XCTAssertFalse(FileManager.default.fileExists(atPath: signalPath.path))
    }

    func testReinitializePreservesAssociationsAndBreadcrumbs() throws {
        let root = try root()
        let first = capture(root, "first")
        first.associateHostSession(hostId: "host", configId: "config", appId: "app")
        first.recordBreadcrumb(kind: "screen", label: "checkout")
        first.initialize(options: .init())
        record(first, fatal: true)
        let next = capture(root, "next")
        let report = try XCTUnwrap(reports(next).first)
        XCTAssertEqual(report["hostId"] as? String, "host")
        XCTAssertEqual((report["breadcrumbs"] as? [[String: Any]])?.first?["label"] as? String, "checkout")
    }

    func testNonfatalCandidatesAndSessionHistoryAreBounded() throws {
        let root = try root()
        let options = AnsightCrashCaptureOptions(maximumPendingReports: 1)
        for index in 0..<10 {
            let current = capture(root, "process-\(index)", options: options)
            for _ in 0..<20 { record(current, fatal: false) }
        }
        let latest = capture(root, "last", options: options)
        XCTAssertTrue(try reports(latest).isEmpty)
        XCTAssertLessThanOrEqual(try FileManager.default.contentsOfDirectory(atPath: root.appendingPathComponent("raw").path).count, 16)
        XCTAssertLessThanOrEqual(try FileManager.default.contentsOfDirectory(atPath: root.appendingPathComponent("sessions").path).count, 4)
    }

    func testMetricKitUsesReportingPeriodAndLeavesAmbiguousSessionUnattributed() throws {
        let root = try root()
        let current = capture(root, "current")
        let base = Date().addingTimeInterval(-100)
        let baseMs = Int64(base.timeIntervalSince1970 * 1_000)
        let sessions: [[String: Any]] = [
            ["processSessionId": "one", "startedAtEpochMs": baseMs + 1000, "endedBeforeEpochMs": baseMs + 3000, "hostSessionId": "host-one"],
            ["processSessionId": "two", "startedAtEpochMs": baseMs + 3000, "endedBeforeEpochMs": baseMs + 6000, "hostSessionId": "host-two"],
        ]
        for session in sessions {
            let file = root.appendingPathComponent("sessions/\(session["processSessionId"]!).json")
            try JSONSerialization.data(withJSONObject: session).write(to: file)
        }
        let payload = Data("{\"crash\":true}".utf8)
        current.recordMetricKitPayload(payload, periodStart: base.addingTimeInterval(2), periodEnd: base.addingTimeInterval(4))
        current.recordMetricKitPayload(payload, periodStart: base.addingTimeInterval(2), periodEnd: base.addingTimeInterval(4))
        let report = try XCTUnwrap(reports(current).first)
        XCTAssertEqual(try reports(current).count, 1)
        XCTAssertEqual(report["sessionCorrelation"] as? String, "unattributed")
        XCTAssertNil(report["hostSessionId"])
        XCTAssertEqual(report["occurredAtUtc"] as? String, AnsightClock.isoString(from: base.addingTimeInterval(4)))
    }

    func testOversizedCandidatesProduceADeliverableReport() throws {
        let root = try root()
        let first = capture(root, "first")
        let stack = String(repeating: "💥", count: 128 * 1024)
        for _ in 0..<16 {
            XCTAssertNotNil(first.recordCandidate(runtime: "dotnet", kind: "unhandled_exception", message: "failure", stack: stack, fatal: true))
        }
        let next = capture(root, "next")
        let report = try XCTUnwrap(reports(next).first)
        XCTAssertEqual(report["candidatesTruncated"] as? Bool, true)
        XCTAssertNotNil(report["candidate"])
        XCTAssertLessThan(try JSONSerialization.data(withJSONObject: report).count, 8 * 1024 * 1024)
    }

    func testExpiredCandidatesAreNotRevivedAsNewReports() throws {
        let root = try root()
        let options = AnsightCrashCaptureOptions(retentionDays: 1)
        let first = capture(root, "first", options: options)
        record(first, fatal: true)
        let files = try FileManager.default.contentsOfDirectory(at: root.appendingPathComponent("raw"), includingPropertiesForKeys: nil)
        for file in files {
            try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-172_800)], ofItemAtPath: file.path)
        }
        XCTAssertTrue(try reports(capture(root, "next", options: options)).isEmpty)
    }

    func testNativeOfflineAttachmentIsDurableAndAcknowledgedOnlyAfterSuccess() async throws {
        let crashRoot = try root()
        let offlineRoot = try root()
        let first = capture(crashRoot, "first", options: .init(hostHandoffEnabled: false))
        let oldStore = try OfflineCaptureStore(root: offlineRoot, options: .init(enabled: true), appId: "app", processSessionId: "first")
        oldStore.crashCapture = first
        oldStore.setRecording(true)
        XCTAssertTrue(oldStore.append(json: Data("{}".utf8)))
        oldStore.setRecording(false)
        let sessions = try await oldStore.sessions()
        let session = try XCTUnwrap(sessions.first)
        first.associateOfflineSession(sessionId: session.sessionId, directory: nil)
        record(first, fatal: true)
        let next = capture(crashRoot, "next", options: .init(hostHandoffEnabled: false))
        let store = try OfflineCaptureStore(root: offlineRoot, options: .init(enabled: true), appId: "app", processSessionId: "next")
        store.crashCapture = next
        let folder = offlineRoot.appendingPathComponent("sessions/\(session.sessionId)")
        let blocked = folder.appendingPathComponent("diagnostics")
        try Data("blocked".utf8).write(to: blocked)
        do {
            _ = try await store.sessions()
            XCTFail("Attachment should fail while its directory is blocked")
        } catch {}
        XCTAssertEqual(try reports(next).count, 1)
        try FileManager.default.removeItem(at: blocked)
        let recovered = try await store.sessions()
        XCTAssertEqual(recovered.first?.terminationKind, "unhandled_exception")
        XCTAssertTrue(try reports(next).isEmpty)
        let manifest = try JSONSerialization.jsonObject(with: Data(contentsOf: folder.appendingPathComponent("manifest.json"))) as! [String: Any]
        let ids = try XCTUnwrap(manifest["crashReportIds"] as? [String])
        XCTAssertEqual(ids.count, 1)
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("diagnostics/crashes/\(ids[0]).json").path))
    }
}
