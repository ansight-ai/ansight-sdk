import CryptoKit
import Foundation

import CAnsightCrashCapture

#if canImport(MetricKit)
import MetricKit
#endif

public struct AnsightCrashCaptureOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var studioHandoffEnabled: Bool
    public var offlineCaptureAttachmentEnabled: Bool
    public var maximumPendingReports: Int
    public var retentionDays: Int
    public var maximumBreadcrumbs: Int
    public var maximumTraceBytes: Int

    public init(
        enabled: Bool = true,
        studioHandoffEnabled: Bool = true,
        offlineCaptureAttachmentEnabled: Bool = true,
        maximumPendingReports: Int = 8,
        retentionDays: Int = 7,
        maximumBreadcrumbs: Int = 64,
        maximumTraceBytes: Int = 1_048_576
    ) {
        self.enabled = enabled
        self.studioHandoffEnabled = studioHandoffEnabled
        self.offlineCaptureAttachmentEnabled = offlineCaptureAttachmentEnabled
        self.maximumPendingReports = maximumPendingReports
        self.retentionDays = retentionDays
        self.maximumBreadcrumbs = maximumBreadcrumbs
        self.maximumTraceBytes = maximumTraceBytes
    }

    private enum CodingKeys: String, CodingKey {
        case enabled
        case studioHandoffEnabled
        case offlineCaptureAttachmentEnabled
        case maximumPendingReports
        case retentionDays
        case maximumBreadcrumbs
        case maximumTraceBytes
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try container.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        studioHandoffEnabled = try container.decodeIfPresent(Bool.self, forKey: .studioHandoffEnabled) ?? true
        offlineCaptureAttachmentEnabled = try container.decodeIfPresent(
            Bool.self,
            forKey: .offlineCaptureAttachmentEnabled
        ) ?? true
        maximumPendingReports = try container.decodeIfPresent(Int.self, forKey: .maximumPendingReports) ?? 8
        retentionDays = try container.decodeIfPresent(Int.self, forKey: .retentionDays) ?? 7
        maximumBreadcrumbs = try container.decodeIfPresent(Int.self, forKey: .maximumBreadcrumbs) ?? 64
        maximumTraceBytes = try container.decodeIfPresent(Int.self, forKey: .maximumTraceBytes) ?? 1_048_576
    }

    public mutating func validate() {
        maximumPendingReports = min(max(maximumPendingReports, 1), 32)
        retentionDays = min(max(retentionDays, 1), 30)
        maximumBreadcrumbs = min(max(maximumBreadcrumbs, 0), 256)
        maximumTraceBytes = min(max(maximumTraceBytes, 16 * 1_024), 4 * 1_024 * 1_024)
    }
}

nonisolated(unsafe) private var ansightPreviousUncaughtExceptionHandler: NSUncaughtExceptionHandler?

private func ansightHandleUncaughtException(_ exception: NSException) {
    AnsightCrashCapture.shared.recordCandidate(
        runtime: "apple-objective-c",
        kind: "uncaught_objective_c_exception",
        message: "\(exception.name.rawValue): \(exception.reason ?? "Uncaught Objective-C exception")",
        stack: exception.callStackSymbols.joined(separator: "\n"),
        fatal: true,
        metadata: ["exceptionName": exception.name.rawValue]
    )
    ansightPreviousUncaughtExceptionHandler?(exception)
}

/**
 * Owns crash-safe persistence and post-launch delivery for every Apple-backed SDK. Framework
 * integrations add candidates here; the native runtime remains the only outbox owner.
 */
final class AnsightCrashCapture: NSObject, @unchecked Sendable {
    static let shared = AnsightCrashCapture()

    private let lock = NSLock()
    private var options = AnsightCrashCaptureOptions(enabled: false)
    private var rootDirectory: URL?
    private var exceptionHandlerInstalled = false
    private var signalHandlersInstalled = false
    private var metricKitInstalled = false

    private override init() {}

    func initialize(options: AnsightCrashCaptureOptions) {
        lock.withLock {
            var validated = options
            validated.validate()
            self.options = validated
            self.rootDirectory = try? resolveRootDirectory()

            guard validated.enabled, let rootDirectory else {
                return
            }

            try? FileManager.default.createDirectory(at: rootDirectory, withIntermediateDirectories: true)
            let signalEvidence = consumeSignalEvidence(rootDirectory: rootDirectory)
            recoverPreviousProcessLocked(
                rootDirectory: rootDirectory,
                signalEvidence: signalEvidence
            )
            beginCurrentProcessLocked(rootDirectory: rootDirectory)
            installExceptionHandlerLocked()
            installSignalHandlersLocked(rootDirectory: rootDirectory)
            installMetricKitLocked()
            trimPendingReportsLocked()
        }
    }

    var processSessionId: String { ProcessSessionIdentity.current }

    @discardableResult
    func recordCandidate(
        runtime: String,
        kind: String,
        message: String?,
        stack: String?,
        fatal: Bool,
        metadata: [String: Any]? = nil
    ) -> String? {
        guard options.enabled, let rootDirectory else { return nil }
        let candidateId = UUID().uuidString.lowercased()
        var candidate: [String: Any] = [
            "candidateId": candidateId,
            "processSessionId": ProcessSessionIdentity.current,
            "occurredAtUtc": AnsightClock.isoNow(),
            "occurredAtEpochMs": Int64(Date().timeIntervalSince1970 * 1_000),
            "runtime": bounded(runtime.isEmpty ? "apple" : runtime, maximum: 128),
            "kind": bounded(kind.isEmpty ? "unhandled_exception" : kind, maximum: 128),
            "fatal": fatal,
        ]
        candidate["message"] = message.map { bounded($0, maximum: 16_384) }
        candidate["stack"] = stack.map { bounded($0, maximum: 128 * 1_024) }
        candidate["metadata"] = metadata
        let file = rawDirectory(rootDirectory).appendingPathComponent("\(candidateId).json")
        writeJSON(candidate, to: file)
        return candidateId
    }

    func recordBreadcrumb(kind: String, label: String, details: String? = nil) {
        let currentOptions = options
        guard currentOptions.enabled, currentOptions.maximumBreadcrumbs > 0, let rootDirectory else { return }
        lock.withLock {
            let file = rootDirectory.appendingPathComponent("breadcrumbs.json")
            var breadcrumbs = readJSONArray(file)
            var breadcrumb: [String: Any] = [
                "capturedAtUtc": AnsightClock.isoNow(),
                "kind": bounded(kind, maximum: 64),
                "label": bounded(label, maximum: 512),
            ]
            if let details {
                breadcrumb["details"] = bounded(details, maximum: 4_096)
            }
            breadcrumbs.append(breadcrumb)
            breadcrumbs = Array(breadcrumbs.suffix(currentOptions.maximumBreadcrumbs))
            writeJSON(breadcrumbs, to: file)
        }
    }

    func associateStudioSession(hostId: String?, configId: String?, appId: String?) {
        updateActiveSession { active in
            active["studioSessionId"] = ProcessSessionIdentity.current
            active["hostId"] = normalized(hostId)
            active["configId"] = normalized(configId)
            active["appId"] = normalized(appId)
            active["studioOpenedAtUtc"] = AnsightClock.isoNow()
            active.removeValue(forKey: "studioCompletedAtUtc")
        }
    }

    func markStudioSessionCompleted() {
        updateActiveSession { $0["studioCompletedAtUtc"] = AnsightClock.isoNow() }
    }

    func associateOfflineSession(sessionId: String, directory: String?) {
        guard let sessionId = normalized(sessionId) else { return }
        updateActiveSession { active in
            active["offlineSessionId"] = sessionId
            active["offlineSessionDirectory"] = normalized(directory)
            active["offlineStartedAtUtc"] = AnsightClock.isoNow()
            active.removeValue(forKey: "offlineCompletedAtUtc")
        }
    }

    func markOfflineSessionCompleted(sessionId: String) {
        updateActiveSession { active in
            guard active["offlineSessionId"] as? String == sessionId else { return }
            active["offlineCompletedAtUtc"] = AnsightClock.isoNow()
        }
    }

    func pendingReportsJSON() -> String {
        let reports = pendingReportFiles().compactMap(readJSONObject)
        return jsonString([
            "processSessionId": ProcessSessionIdentity.current,
            "reports": reports,
        ]) ?? "{\"processSessionId\":\"\(ProcessSessionIdentity.current)\",\"reports\":[]}"
    }

    func markOfflineReportPersisted(reportId: String) -> Bool {
        updatePendingReport(reportId: reportId) { report in
            report["offlineCapturePersisted"] = true
        }
    }

    func deliverPendingReports(using transport: PairingLiveSessionTransport) async {
        guard options.enabled, options.studioHandoffEnabled else { return }
        for file in pendingReportFiles() {
            guard var report = readJSONObject(file) else { continue }
            if report["studioAcknowledged"] as? Bool == true {
                deleteIfFullyDelivered(file: file, report: report)
                continue
            }

            let payload: JSONValue
            do {
                payload = try .fromEncodable(CrashHandoffPayload(
                    reportId: report["reportId"] as? String ?? file.deletingPathExtension().lastPathComponent,
                    targetProcessSessionId: report["previousProcessSessionId"] as? String ?? "",
                    targetSessionId: report["studioSessionId"] as? String,
                    deliveryProcessSessionId: ProcessSessionIdentity.current,
                    report: try JSONValue.fromAnyCrashObject(report)
                ))
            } catch {
                continue
            }

            let result = await transport.sendControlRequestWithResponse(
                action: "crash.handoff",
                payload: payload
            ).operationResult
            if result.success {
                report["studioAcknowledged"] = true
                report["studioAcknowledgedAtUtc"] = AnsightClock.isoNow()
                writeJSON(report, to: file)
                deleteIfFullyDelivered(file: file, report: report)
            }
        }
    }

    private func recoverPreviousProcessLocked(
        rootDirectory: URL,
        signalEvidence: CrashSignalEvidence?
    ) {
        let activeFile = rootDirectory.appendingPathComponent("active-session.json")
        guard let previousSession = readJSONObject(activeFile),
              let previousProcessSessionId = previousSession["processSessionId"] as? String,
              previousProcessSessionId != ProcessSessionIdentity.current else {
            return
        }
        let previousProcessId = (previousSession["processId"] as? NSNumber)?.int32Value
        let matchingSignalEvidence = signalEvidence.flatMap { evidence in
            previousProcessId == nil || previousProcessId == evidence.processId ? evidence : nil
        }

        let historyFile = historyDirectory(rootDirectory)
            .appendingPathComponent("\(previousProcessSessionId).json")
        writeJSON(previousSession, to: historyFile)

        let candidates = files(in: rawDirectory(rootDirectory), extension: "json")
            .compactMap(readJSONObject)
            .filter { $0["processSessionId"] as? String == previousProcessSessionId }
            .sorted { epochMilliseconds($0) < epochMilliseconds($1) }
        let candidate = candidates.last
        guard matchingSignalEvidence != nil || candidate?["fatal"] as? Bool == true else { return }

        let occurredAtEpochMs = matchingSignalEvidence?.occurredAtEpochMs
            ?? candidate.map { epochMilliseconds($0) }
            ?? Int64(Date().timeIntervalSince1970 * 1_000)
        let reason = matchingSignalEvidence?.kind
            ?? candidate?["kind"] as? String
            ?? "abnormal_exit"
        let reportId = stableReportId(
            processSessionId: previousProcessSessionId,
            occurredAtEpochMs: occurredAtEpochMs,
            reason: reason
        )
        let reportFile = pendingDirectory(rootDirectory).appendingPathComponent("\(reportId).json")
        if !FileManager.default.fileExists(atPath: reportFile.path) {
            var report: [String: Any] = [
                "schema": "ansight.crash.v1",
                "reportId": reportId,
                "previousProcessSessionId": previousProcessSessionId,
                "occurredAtUtc": AnsightClock.isoString(from: Date(timeIntervalSince1970: Double(occurredAtEpochMs) / 1_000)),
                "detectedAtUtc": AnsightClock.isoNow(),
                "platform": "apple",
                "kind": reason,
                "confidence": "confirmed",
                "breadcrumbs": readJSONArray(rootDirectory.appendingPathComponent("breadcrumbs.json")),
                "studioRequired": options.studioHandoffEnabled,
                "offlineCaptureRequired": options.offlineCaptureAttachmentEnabled &&
                    previousSession["offlineSessionId"] != nil &&
                    previousSession["offlineCompletedAtUtc"] == nil,
                "studioAcknowledged": false,
                "offlineCapturePersisted": false,
            ]
            if let candidate {
                report["candidate"] = candidate
            }
            if let signalEvidence = matchingSignalEvidence {
                report["termination"] = signalEvidence.json
            }
            copyAssociationKeys(from: previousSession, to: &report)
            writeJSON(report, to: reportFile)
        }

        for file in files(in: rawDirectory(rootDirectory), extension: "json") {
            if readJSONObject(file)?["processSessionId"] as? String == previousProcessSessionId {
                try? FileManager.default.removeItem(at: file)
            }
        }
    }

    private func beginCurrentProcessLocked(rootDirectory: URL) {
        let bundle = Bundle.main
        let active: [String: Any] = [
            "processSessionId": ProcessSessionIdentity.current,
            "processId": ProcessInfo.processInfo.processIdentifier,
            "startedAtUtc": AnsightClock.isoNow(),
            "startedAtEpochMs": Int64(Date().timeIntervalSince1970 * 1_000),
            "appId": bundle.bundleIdentifier ?? "unknown",
            "appVersion": bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            "buildNumber": bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "",
        ]
        writeJSON(active, to: rootDirectory.appendingPathComponent("active-session.json"))
        writeJSON([], to: rootDirectory.appendingPathComponent("breadcrumbs.json"))
    }

    private func installExceptionHandlerLocked() {
        guard !exceptionHandlerInstalled else { return }
        ansightPreviousUncaughtExceptionHandler = NSGetUncaughtExceptionHandler()
        NSSetUncaughtExceptionHandler(ansightHandleUncaughtException)
        exceptionHandlerInstalled = true
    }

    private func signalFile(_ rootDirectory: URL) -> URL {
        rootDirectory.appendingPathComponent("signal.raw")
    }

    private func consumeSignalEvidence(rootDirectory: URL) -> CrashSignalEvidence? {
        var record = AnsightCrashSignalRecord()
        let found = signalFile(rootDirectory).path.withCString { path in
            ansight_crash_consume_signal_record(path, &record)
        }
        guard found == 1 else { return nil }
        return CrashSignalEvidence(record: record)
    }

    private func installSignalHandlersLocked(rootDirectory: URL) {
        guard !signalHandlersInstalled else { return }
        let result = signalFile(rootDirectory).path.withCString { path in
            ansight_crash_install_signal_handlers(path)
        }
        signalHandlersInstalled = result == 0
    }

    private func installMetricKitLocked() {
        #if canImport(MetricKit)
        guard !metricKitInstalled else { return }
        if #available(iOS 13.0, macOS 12.0, *) {
            MXMetricManager.shared.add(self)
            metricKitInstalled = true
        }
        #endif
    }

    private func recordMetricKitPayload(_ payload: Data) {
        guard options.enabled, let rootDirectory else { return }
        let histories = files(in: historyDirectory(rootDirectory), extension: "json")
            .compactMap(readJSONObject)
            .sorted { epochMilliseconds($0, key: "startedAtEpochMs") < epochMilliseconds($1, key: "startedAtEpochMs") }
        guard let previousSession = histories.last,
              let processSessionId = previousSession["processSessionId"] as? String else { return }
        let timestamp = Int64(Date().timeIntervalSince1970 * 1_000)
        let reportId = stableReportId(processSessionId: processSessionId, occurredAtEpochMs: timestamp, reason: "metrickit_crash")
        let file = pendingDirectory(rootDirectory).appendingPathComponent("\(reportId).json")
        var report: [String: Any] = [
            "schema": "ansight.crash.v1",
            "reportId": reportId,
            "previousProcessSessionId": processSessionId,
            "occurredAtUtc": AnsightClock.isoNow(),
            "detectedAtUtc": AnsightClock.isoNow(),
            "platform": "apple",
            "kind": "native_crash",
            "confidence": "confirmed",
            "metricKitPayloadBase64": payload.prefix(options.maximumTraceBytes).base64EncodedString(),
            "studioRequired": options.studioHandoffEnabled,
            "offlineCaptureRequired": options.offlineCaptureAttachmentEnabled &&
                previousSession["offlineSessionId"] != nil &&
                previousSession["offlineCompletedAtUtc"] == nil,
            "studioAcknowledged": false,
            "offlineCapturePersisted": false,
        ]
        copyAssociationKeys(from: previousSession, to: &report)
        writeJSON(report, to: file)
    }

    private func updateActiveSession(_ update: (inout [String: Any]) -> Void) {
        guard options.enabled, let rootDirectory else { return }
        lock.withLock {
            let file = rootDirectory.appendingPathComponent("active-session.json")
            var active = readJSONObject(file) ?? [:]
            update(&active)
            writeJSON(active, to: file)
        }
    }

    private func updatePendingReport(reportId: String, update: (inout [String: Any]) -> Void) -> Bool {
        guard let reportId = normalized(reportId) else { return false }
        return lock.withLock {
            guard let file = pendingReportFiles().first(where: { $0.deletingPathExtension().lastPathComponent == reportId }),
                  var report = readJSONObject(file) else { return false }
            update(&report)
            writeJSON(report, to: file)
            deleteIfFullyDelivered(file: file, report: report)
            return true
        }
    }

    private func deleteIfFullyDelivered(file: URL, report: [String: Any]) {
        let studioDelivered = report["studioRequired"] as? Bool == false || report["studioAcknowledged"] as? Bool == true
        let offlineDelivered = report["offlineCaptureRequired"] as? Bool == false || report["offlineCapturePersisted"] as? Bool == true
        if studioDelivered && offlineDelivered {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func trimPendingReportsLocked() {
        let cutoff = Date().addingTimeInterval(-Double(options.retentionDays) * 86_400)
        var files = pendingReportFiles()
        for file in files where modificationDate(file) < cutoff {
            try? FileManager.default.removeItem(at: file)
        }
        files = pendingReportFiles()
        for file in files.dropLast(options.maximumPendingReports) {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func pendingReportFiles() -> [URL] {
        guard let rootDirectory else { return [] }
        return files(in: pendingDirectory(rootDirectory), extension: "json")
            .sorted { modificationDate($0) < modificationDate($1) }
    }

    private func resolveRootDirectory() throws -> URL {
        let applicationSupport = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let root = applicationSupport.appendingPathComponent("Ansight/Crashes", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        var mutableRoot = root
        try? mutableRoot.setResourceValues(values)
        return root
    }

    private func rawDirectory(_ root: URL) -> URL { directory(root, "raw") }
    private func pendingDirectory(_ root: URL) -> URL { directory(root, "pending") }
    private func historyDirectory(_ root: URL) -> URL { directory(root, "sessions") }

    private func directory(_ root: URL, _ name: String) -> URL {
        let result = root.appendingPathComponent(name, isDirectory: true)
        try? FileManager.default.createDirectory(at: result, withIntermediateDirectories: true)
        return result
    }

    private func files(in directory: URL, extension fileExtension: String) -> [URL] {
        (try? FileManager.default.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ))?.filter { $0.pathExtension == fileExtension } ?? []
    }

    private func readJSONObject(_ file: URL) -> [String: Any]? {
        guard let data = try? Data(contentsOf: file),
              let value = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return value
    }

    private func readJSONArray(_ file: URL) -> [[String: Any]] {
        guard let data = try? Data(contentsOf: file),
              let value = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { return [] }
        return value
    }

    private func writeJSON(_ value: Any, to file: URL) {
        guard JSONSerialization.isValidJSONObject(value),
              let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]) else { return }
        try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: file, options: .atomic)
    }

    private func jsonString(_ value: Any) -> String? {
        guard JSONSerialization.isValidJSONObject(value),
              let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private func stableReportId(processSessionId: String, occurredAtEpochMs: Int64, reason: String) -> String {
        let digest = SHA256.hash(data: Data("\(processSessionId):\(occurredAtEpochMs):\(reason)".utf8))
        return digest.prefix(16).map { String(format: "%02x", $0) }.joined()
    }

    private func epochMilliseconds(_ value: [String: Any], key: String = "occurredAtEpochMs") -> Int64 {
        (value[key] as? NSNumber)?.int64Value ?? 0
    }

    private func modificationDate(_ file: URL) -> Date {
        (try? file.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
    }

    private func copyAssociationKeys(from source: [String: Any], to target: inout [String: Any]) {
        for key in ["studioSessionId", "hostId", "configId", "appId", "offlineSessionId", "offlineSessionDirectory"] {
            target[key] = source[key]
        }
    }

    private func bounded(_ value: String, maximum: Int) -> String { String(value.prefix(maximum)) }

    private func normalized(_ value: String?) -> String? {
        value?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false
            ? value?.trimmingCharacters(in: .whitespacesAndNewlines)
            : nil
    }
}

private struct CrashSignalEvidence {
    let signalNumber: Int32
    let signalCode: Int32
    let faultAddress: UInt64
    let occurredAtEpochMs: Int64
    let processId: Int32

    init(record: AnsightCrashSignalRecord) {
        signalNumber = record.signalNumber
        signalCode = record.signalCode
        faultAddress = record.faultAddress
        occurredAtEpochMs = record.occurredAtEpochSeconds * 1_000
        processId = record.processId
    }

    var kind: String {
        switch signalNumber {
        case SIGABRT: "signal_sigabrt"
        case SIGBUS: "signal_sigbus"
        case SIGFPE: "signal_sigfpe"
        case SIGILL: "signal_sigill"
        case SIGSEGV: "signal_sigsegv"
        case SIGTRAP: "signal_sigtrap"
        default: "signal"
        }
    }

    var json: [String: Any] {
        [
            "reason": kind,
            "signalNumber": signalNumber,
            "signalCode": signalCode,
            "faultAddress": String(format: "0x%llx", faultAddress),
            "processId": processId,
        ]
    }
}

private struct CrashHandoffPayload: Encodable {
    let reportId: String
    let targetProcessSessionId: String
    let targetSessionId: String?
    let deliveryProcessSessionId: String
    let report: JSONValue
}

private extension JSONValue {
    static func fromAnyCrashObject(_ value: Any) throws -> JSONValue {
        let data = try JSONSerialization.data(withJSONObject: value)
        return try JSONDecoder().decode(JSONValue.self, from: data)
    }
}

#if canImport(MetricKit)
@available(iOS 13.0, macOS 12.0, *)
extension AnsightCrashCapture: MXMetricManagerSubscriber {
    func didReceive(_ payloads: [MXDiagnosticPayload]) {
        for payload in payloads where payload.crashDiagnostics?.isEmpty == false {
            recordMetricKitPayload(payload.jsonRepresentation())
        }
    }
}
#endif
