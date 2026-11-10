import CryptoKit
import Foundation

#if canImport(CAnsightCrashCapture)
import CAnsightCrashCapture
#endif

#if canImport(MetricKit)
import MetricKit
#endif

public struct AnsightCrashCaptureOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var hostHandoffEnabled: Bool
    public var offlineCaptureAttachmentEnabled: Bool
    public var maximumPendingReports: Int
    public var retentionDays: Int
    public var maximumBreadcrumbs: Int
    public var maximumTraceBytes: Int

    public init(
        enabled: Bool = true,
        hostHandoffEnabled: Bool = true,
        offlineCaptureAttachmentEnabled: Bool = true,
        maximumPendingReports: Int = 8,
        retentionDays: Int = 7,
        maximumBreadcrumbs: Int = 64,
        maximumTraceBytes: Int = 1_048_576
    ) {
        self.enabled = enabled
        self.hostHandoffEnabled = hostHandoffEnabled
        self.offlineCaptureAttachmentEnabled = offlineCaptureAttachmentEnabled
        self.maximumPendingReports = maximumPendingReports
        self.retentionDays = retentionDays
        self.maximumBreadcrumbs = maximumBreadcrumbs
        self.maximumTraceBytes = maximumTraceBytes
    }

    private enum CodingKeys: String, CodingKey {
        case enabled
        case hostHandoffEnabled
        case offlineCaptureAttachmentEnabled
        case maximumPendingReports
        case retentionDays
        case maximumBreadcrumbs
        case maximumTraceBytes
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try container.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        hostHandoffEnabled = try container.decodeIfPresent(Bool.self, forKey: .hostHandoffEnabled)
            ?? true
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

nonisolated(unsafe) private var ansightPreviousUncaughtExceptionHandler: (@convention(c) (NSException) -> Void)?

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

    private let configuredRootDirectory: URL?
    private let currentProcessSessionId: String
    private let installPlatformHandlers: Bool
    private var processInitialized = false
    private var delivering = false
    private var deliveryRequested = false
    private var reportAvailableHandler: (@Sendable () -> Void)?

    func setReportAvailableHandler(_ handler: (@Sendable () -> Void)?) {
        lock.withLock { reportAvailableHandler = handler }
    }

    init(rootDirectory: URL? = nil, processSessionId: String = ProcessSessionIdentity.current,
         installPlatformHandlers: Bool = true) {
        self.configuredRootDirectory = rootDirectory
        self.currentProcessSessionId = processSessionId
        self.installPlatformHandlers = installPlatformHandlers
        super.init()
    }

    func initialize(options: AnsightCrashCaptureOptions) {
        lock.withLock {
            var validated = options
            validated.validate()
            self.options = validated
            self.rootDirectory = configuredRootDirectory ?? (try? resolveRootDirectory())

            guard validated.enabled, let rootDirectory else {
                uninstallHandlersLocked()
                return
            }
            guard (try? FileManager.default.createDirectory(at: rootDirectory, withIntermediateDirectories: true)) != nil else {
                return
            }

            trimEvidenceLocked(rootDirectory: rootDirectory)
            if !processInitialized {
                guard archivePreviousSessionLocked(rootDirectory: rootDirectory) else { return }
                recoverPreviousProcessesLocked(rootDirectory: rootDirectory)
                guard beginCurrentProcessLocked(rootDirectory: rootDirectory) else { return }
                processInitialized = true
            } else {
                recoverPreviousProcessesLocked(rootDirectory: rootDirectory)
            }

            if installPlatformHandlers {
                installExceptionHandlerLocked()
                installSignalHandlersLocked(rootDirectory: rootDirectory)
                installMetricKitLocked()
            }
            trimEvidenceLocked(rootDirectory: rootDirectory)
        }
    }

    var processSessionId: String { currentProcessSessionId }
    var offlineAttachmentEnabled: Bool { options.enabled && options.offlineCaptureAttachmentEnabled }

    @discardableResult
    func recordCandidate(
        runtime: String,
        kind: String,
        message: String?,
        stack: String?,
        fatal: Bool,
        metadata: [String: Any]? = nil
    ) -> String? {
        let captureEnabled = options.enabled && processInitialized
        guard captureEnabled, let rootDirectory else { return nil }
        let candidateId = UUID().uuidString.lowercased()
        var candidate: [String: Any] = [
            "candidateId": candidateId,
            "processSessionId": currentProcessSessionId,
            "occurredAtUtc": AnsightClock.isoNow(),
            "occurredAtEpochMs": Int64(Date().timeIntervalSince1970 * 1_000),
            "runtime": bounded(runtime.isEmpty ? "apple" : runtime, maximum: 128),
            "kind": bounded(kind.isEmpty ? "unhandled_exception" : kind, maximum: 128),
            "fatal": fatal,
        ]
        candidate["message"] = message.map { bounded($0, maximum: 16_384) }
        candidate["stack"] = stack.map { bounded($0, maximum: 128 * 1_024) }
        if let metadata, JSONSerialization.isValidJSONObject(metadata) {
            if let encoded = try? JSONSerialization.data(withJSONObject: metadata), encoded.count <= 16_384 {
                candidate["metadata"] = metadata
            }
        }
        let file = rawDirectory(rootDirectory).appendingPathComponent("\(candidateId).json")
        guard writeJSON(candidate, to: file) else { return nil }
        trimRawCandidates(rootDirectory: rootDirectory, processSessionId: currentProcessSessionId, maximum: 16)
        trimRawCandidates(rootDirectory: rootDirectory, processSessionId: nil, maximum: options.maximumPendingReports * 16)
        return candidateId
    }

    func recordBreadcrumb(kind: String, label: String, details: String? = nil) {
        let currentOptions = options
        let breadcrumbsEnabled = currentOptions.enabled && currentOptions.maximumBreadcrumbs > 0
        guard breadcrumbsEnabled, let rootDirectory else { return }
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

    func associateHostSession(hostId: String?, configId: String?, appId: String?) {
        updateActiveSession { active in
            active["hostSessionId"] = currentProcessSessionId
            active["hostId"] = normalized(hostId)
            active["configId"] = normalized(configId)
            active["appId"] = normalized(appId)
            active["hostOpenedAtUtc"] = AnsightClock.isoNow()
            active.removeValue(forKey: "hostCompletedAtUtc")
        }
    }

    func markHostSessionCompleted() {
        updateActiveSession { $0["hostCompletedAtUtc"] = AnsightClock.isoNow() }
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
        let reports = lock.withLock { pendingReportFiles().compactMap(readJSONObject) }
        return jsonString([
            "processSessionId": currentProcessSessionId,
            "reports": reports,
        ]) ?? "{\"processSessionId\":\"\(currentProcessSessionId)\",\"reports\":[]}"
    }

    func markOfflineReportPersisted(reportId: String) -> Bool {
        updatePendingReport(reportId: reportId) { report in
            report["offlineCapturePersisted"] = true
        }
    }

    func deliverPendingReports(using transport: PairingLiveSessionTransport) async {
        let shouldDeliver = lock.withLock {
            guard options.enabled, options.hostHandoffEnabled else { return false }
            if delivering {
                deliveryRequested = true
                return false
            }
            delivering = true
            deliveryRequested = false
            return true
        }
        guard shouldDeliver else { return }
        defer {
            let retry = lock.withLock {
                delivering = false
                return deliveryRequested
            }
            if retry { Task { await self.deliverPendingReports(using: transport) } }
        }

        for file in pendingReportFiles() {
            guard let report = readJSONObject(file), report["hostAcknowledged"] as? Bool != true else { continue }
            let reportId = report["reportId"] as? String ?? file.deletingPathExtension().lastPathComponent
            guard let payload = try? JSONValue.fromEncodable(CrashHandoffPayload(
                reportId: reportId,
                targetProcessSessionId: report["previousProcessSessionId"] as? String ?? "",
                targetSessionId: report["hostSessionId"] as? String,
                deliveryProcessSessionId: currentProcessSessionId,
                report: try JSONValue.fromAnyCrashObject(report)
            )) else { continue }

            let result = await transport.sendControlRequestWithResponse(action: "crash.handoff", payload: payload).operationResult
            if result.success {
                _ = updatePendingReport(reportId: reportId) {
                    $0["hostAcknowledged"] = true
                    $0["hostAcknowledgedAtUtc"] = AnsightClock.isoNow()
                }
            }
        }
    }

    private func archivePreviousSessionLocked(rootDirectory: URL) -> Bool {
        let activeFile = rootDirectory.appendingPathComponent("active-session.json")
        guard var previous = readJSONObject(activeFile),
              let id = previous["processSessionId"] as? String else { return true }
        if id == currentProcessSessionId { return true }
        guard isSafeIdentifier(id) else { return false }
        let historyFile = historyDirectory(rootDirectory).appendingPathComponent("\(id).json")
        if FileManager.default.fileExists(atPath: historyFile.path) { return true }
        previous["endedBeforeEpochMs"] = Int64(Date().timeIntervalSince1970 * 1_000)
        previous["breadcrumbs"] = readJSONArray(rootDirectory.appendingPathComponent("breadcrumbs.json"))
        return writeJSON(previous, to: historyFile)
    }

    private func recoverPreviousProcessesLocked(rootDirectory: URL) {
        for session in files(in: historyDirectory(rootDirectory), extension: "json").compactMap(readJSONObject) {
            recoverSessionLocked(session, rootDirectory: rootDirectory)
        }
    }

    private func recoverSessionLocked(_ previousSession: [String: Any], rootDirectory: URL) {
        guard let previousProcessSessionId = previousSession["processSessionId"] as? String else { return }
        let previousProcess = isSafeIdentifier(previousProcessSessionId) && previousProcessSessionId != currentProcessSessionId
        guard previousProcess else { return }
        guard previousSession["crashReportId"] == nil else { return }
        let signalPath = signalFile(rootDirectory, processSessionId: previousProcessSessionId)
        let legacySignalPath = rootDirectory.appendingPathComponent("signal.raw")
        let previousProcessId = (previousSession["processId"] as? NSNumber)?.int32Value
        let signalSource = FileManager.default.fileExists(atPath: signalPath.path) ? signalPath : legacySignalPath
        let signalEvidence = readSignalEvidence(file: signalSource).flatMap { evidence in
            previousProcessId == nil || previousProcessId == evidence.processId ? evidence : nil
        }
        let candidateFiles = files(in: rawDirectory(rootDirectory), extension: "json").filter {
            readJSONObject($0)?["processSessionId"] as? String == previousProcessSessionId
        }
        let candidates = candidateFiles.compactMap(readJSONObject).sorted { epochMilliseconds($0) < epochMilliseconds($1) }
        let fatalCandidate = candidates.last { $0["fatal"] as? Bool == true }
        guard signalEvidence != nil || fatalCandidate != nil else { return }
        // Keep all bounded framework evidence. Prefer the managed stack for the
        // compatibility field rather than replacing it with a later native wrapper.
        let candidate = candidates.last { $0["runtime"] as? String == "dotnet" } ?? fatalCandidate ?? candidates.last
        let occurredAtEpochMs = signalEvidence?.occurredAtEpochMs ?? fatalCandidate.map { epochMilliseconds($0) } ?? 0
        let reason = signalEvidence?.kind ?? fatalCandidate?["kind"] as? String ?? "unhandled_exception"
        let reportId = stableReportId(processSessionId: previousProcessSessionId, occurredAtEpochMs: occurredAtEpochMs, reason: reason)
        let reportFile = pendingDirectory(rootDirectory).appendingPathComponent("\(reportId).json")
        if readJSONObject(reportFile) == nil {
            let requiresOfflineCapture = options.offlineCaptureAttachmentEnabled
                && previousSession["offlineSessionId"] != nil
                && previousSession["offlineCompletedAtUtc"] == nil
            var report: [String: Any] = [
                "schema": "ansight.crash.v1", "reportId": reportId,
                "previousProcessSessionId": previousProcessSessionId,
                "occurredAtUtc": AnsightClock.isoString(from: Date(timeIntervalSince1970: Double(occurredAtEpochMs) / 1_000)),
                "detectedAtUtc": AnsightClock.isoNow(), "platform": "apple", "kind": reason,
                "confidence": "confirmed", "candidates": candidates,
                "breadcrumbs": previousSession["breadcrumbs"] as? [[String: Any]] ?? [],
                "hostRequired": options.hostHandoffEnabled,
                "offlineCaptureRequired": requiresOfflineCapture,
                "hostAcknowledged": false, "offlineCapturePersisted": false,
            ]
            report["candidate"] = candidate
            report["termination"] = signalEvidence?.json
            copyAssociationKeys(from: previousSession, to: &report)
            guard writeReport(report, to: reportFile) else { return }
        }
        var recoveredSession = previousSession
        recoveredSession["crashReportId"] = reportId
        guard writeJSON(recoveredSession, to: historyDirectory(rootDirectory).appendingPathComponent("\(previousProcessSessionId).json")) else { return }
        // Commit the report before acknowledging any of its source evidence.
        for file in candidateFiles { try? FileManager.default.removeItem(at: file) }
        if signalEvidence != nil { try? FileManager.default.removeItem(at: signalSource) }
    }

    private func beginCurrentProcessLocked(rootDirectory: URL) -> Bool {
        let bundle = Bundle.main
        let active: [String: Any] = [
            "processSessionId": currentProcessSessionId,
            "processId": ProcessInfo.processInfo.processIdentifier,
            "startedAtUtc": AnsightClock.isoNow(),
            "startedAtEpochMs": Int64(Date().timeIntervalSince1970 * 1_000),
            "appId": bundle.bundleIdentifier ?? "unknown",
            "appVersion": bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            "buildNumber": bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "",
        ]
        guard writeJSON(active, to: rootDirectory.appendingPathComponent("active-session.json")) else { return false }
        writeJSON([], to: rootDirectory.appendingPathComponent("breadcrumbs.json"))
        return true
    }

    private func installExceptionHandlerLocked() {
        guard !exceptionHandlerInstalled else { return }
        ansightPreviousUncaughtExceptionHandler = NSGetUncaughtExceptionHandler()
        NSSetUncaughtExceptionHandler(ansightHandleUncaughtException)
        exceptionHandlerInstalled = true
    }

    private func signalFile(_ rootDirectory: URL, processSessionId: String) -> URL {
        directory(rootDirectory, "signals").appendingPathComponent("\(processSessionId).raw")
    }

    private func readSignalEvidence(file: URL) -> CrashSignalEvidence? {
        var record = AnsightCrashSignalRecord()
        let found = file.path.withCString { ansight_crash_read_signal_record($0, &record) }
        return found == 1 ? CrashSignalEvidence(record: record) : nil
    }

    private func installSignalHandlersLocked(rootDirectory: URL) {
        guard !signalHandlersInstalled else { return }
        let result = signalFile(rootDirectory, processSessionId: currentProcessSessionId).path.withCString {
            ansight_crash_install_signal_handlers($0)
        }
        signalHandlersInstalled = result == 0
    }

    private func uninstallHandlersLocked() {
        guard installPlatformHandlers else { return }
        if signalHandlersInstalled {
            ansight_crash_uninstall_signal_handlers()
            signalHandlersInstalled = false
        }
        let handler: @convention(c) (NSException) -> Void = ansightHandleUncaughtException
        if exceptionHandlerInstalled,
           NSGetUncaughtExceptionHandler().map({ unsafeBitCast($0, to: UInt.self) }) ==
            unsafeBitCast(handler, to: UInt.self) {
            NSSetUncaughtExceptionHandler(ansightPreviousUncaughtExceptionHandler)
            exceptionHandlerInstalled = false
        }
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

    func recordMetricKitPayload(_ payload: Data, periodStart: Date, periodEnd: Date) {
        let persisted = lock.withLock { () -> Bool in
            guard options.enabled, let rootDirectory else { return false }
            let cutoff = Date().addingTimeInterval(-Double(options.retentionDays) * 86_400)
            guard periodEnd >= cutoff, periodStart <= periodEnd else { return false }
            let startMs = Int64(periodStart.timeIntervalSince1970 * 1_000)
            let endMs = Int64(periodEnd.timeIntervalSince1970 * 1_000)
            let histories = files(in: historyDirectory(rootDirectory), extension: "json").compactMap(readJSONObject)
            let matches = histories.filter {
                epochMilliseconds($0, key: "startedAtEpochMs") <= endMs &&
                    epochMilliseconds($0, key: "endedBeforeEpochMs") >= startMs
            }
            let uniqueSession = matches.count == 1 ? matches[0] : nil
            let session = uniqueSession.flatMap { candidate -> [String: Any]? in
                let containsPeriod = epochMilliseconds(candidate, key: "startedAtEpochMs") <= startMs
                    && epochMilliseconds(candidate, key: "endedBeforeEpochMs") >= endMs
                return containsPeriod ? candidate : nil
            }
            let digest = SHA256.hash(data: payload).prefix(16).map { String(format: "%02x", $0) }.joined()
            let reportId = "metrickit-\(digest)"
            let file = pendingDirectory(rootDirectory).appendingPathComponent("\(reportId).json")
            if readJSONObject(file) != nil { return false }
            let requiresOfflineCapture = options.offlineCaptureAttachmentEnabled
                && session?["offlineSessionId"] != nil
                && session?["offlineCompletedAtUtc"] == nil
            var report: [String: Any] = [
                "schema": "ansight.crash.v1", "reportId": reportId,
                "previousProcessSessionId": session?["processSessionId"] as? String ?? reportId,
                "occurredAtUtc": AnsightClock.isoString(from: periodEnd),
                "occurrenceTimePrecision": "reporting_period",
                "reportingPeriodStartUtc": AnsightClock.isoString(from: periodStart),
                "reportingPeriodEndUtc": AnsightClock.isoString(from: periodEnd),
                "sessionCorrelation": session == nil ? "unattributed" : "reporting_period",
                "detectedAtUtc": AnsightClock.isoNow(), "platform": "apple", "kind": "native_crash",
                "confidence": "confirmed", "hostRequired": options.hostHandoffEnabled,
                "offlineCaptureRequired": requiresOfflineCapture,
                "hostAcknowledged": false, "offlineCapturePersisted": false,
            ]
            if payload.count <= options.maximumTraceBytes {
                report["metricKitPayloadBase64"] = payload.base64EncodedString()
            } else {
                report["metricKitPayloadOmitted"] = true
                report["metricKitPayloadBytes"] = payload.count
            }
            if let session { copyAssociationKeys(from: session, to: &report) }
            guard writeReport(report, to: file) else { return false }
            trimEvidenceLocked(rootDirectory: rootDirectory)
            return true
        }
        if persisted {
            let handler = lock.withLock { reportAvailableHandler }
            handler?()
        }
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
            guard writeJSON(report, to: file) else { return false }
            deleteIfFullyDelivered(file: file, report: report)
            return true
        }
    }

    private func deleteIfFullyDelivered(file: URL, report: [String: Any]) {
        let hostDelivered = report["hostRequired"] as? Bool == false || report["hostAcknowledged"] as? Bool == true
        let offlineDelivered = report["offlineCaptureRequired"] as? Bool == false || report["offlineCapturePersisted"] as? Bool == true
        if hostDelivered && offlineDelivered {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func trimEvidenceLocked(rootDirectory: URL) {
        let cutoff = Date().addingTimeInterval(-Double(options.retentionDays) * 86_400)
        trimFiles(pendingReportFiles(), maximum: options.maximumPendingReports, cutoff: cutoff)
        trimFiles(files(in: historyDirectory(rootDirectory), extension: "json"),
                  maximum: options.maximumPendingReports * 4, cutoff: cutoff)
        trimFiles(files(in: directory(rootDirectory, "signals"), extension: "raw").filter {
            $0.deletingPathExtension().lastPathComponent != currentProcessSessionId
        }, maximum: options.maximumPendingReports * 4, cutoff: cutoff)
        trimRawCandidates(rootDirectory: rootDirectory, processSessionId: nil, maximum: options.maximumPendingReports * 16)
        let raw = files(in: rawDirectory(rootDirectory), extension: "json")
        for file in raw where modificationDate(file) < cutoff { try? FileManager.default.removeItem(at: file) }
    }

    private func trimFiles(_ files: [URL], maximum: Int, cutoff: Date) {
        let ordered = files.sorted { modificationDate($0) > modificationDate($1) }
        for (index, file) in ordered.enumerated() where index >= maximum || modificationDate(file) < cutoff {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func trimRawCandidates(rootDirectory: URL, processSessionId: String?, maximum: Int) {
        let candidates = files(in: rawDirectory(rootDirectory), extension: "json").filter {
            processSessionId == nil || readJSONObject($0)?["processSessionId"] as? String == processSessionId
        }.sorted {
            let leftFatal = readJSONObject($0)?["fatal"] as? Bool == true
            let rightFatal = readJSONObject($1)?["fatal"] as? Bool == true
            return leftFatal == rightFatal ? modificationDate($0) > modificationDate($1) : leftFatal
        }
        for file in candidates.dropFirst(maximum) { try? FileManager.default.removeItem(at: file) }
    }

    private func isSafeIdentifier(_ value: String) -> Bool {
        guard !value.isEmpty, value.count <= 128 else { return false }
        return value.utf8.allSatisfy { byte in
            let letter = (65...90).contains(byte) || (97...122).contains(byte)
            let digit = (48...57).contains(byte)
            let separator = byte == 45 || byte == 95
            let allowed = letter
                || digit
                || separator
            return allowed
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

    private func writeReport(_ value: [String: Any], to file: URL) -> Bool {
        var report = value
        while let data = try? JSONSerialization.data(withJSONObject: report), data.count > 7 * 1024 * 1024 {
            if let candidates = report["candidates"] as? [[String: Any]], !candidates.isEmpty {
                report["candidates"] = Array(candidates.suffix(candidates.count / 2))
                report["candidatesTruncated"] = true
            } else if let breadcrumbs = report["breadcrumbs"] as? [[String: Any]], !breadcrumbs.isEmpty {
                report["breadcrumbs"] = Array(breadcrumbs.suffix(breadcrumbs.count / 2))
                report["breadcrumbsTruncated"] = true
            } else if report.removeValue(forKey: "traceBase64") != nil {
                report["traceOmittedForReportLimit"] = true
            } else {
                return false
            }
        }
        return writeJSON(report, to: file)
    }

    @discardableResult
    private func writeJSON(_ value: Any, to file: URL) -> Bool {
        do {
            guard JSONSerialization.isValidJSONObject(value) else { return false }
            let data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: file, options: .atomic)
            let handle = try FileHandle(forWritingTo: file)
            defer { try? handle.close() }
            try handle.synchronize()
            return true
        } catch {
            return false
        }
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
        for key in ["hostSessionId", "hostId", "configId", "appId", "offlineSessionId", "offlineSessionDirectory", "appVersion", "buildNumber"] {
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
            recordMetricKitPayload(payload.jsonRepresentation(), periodStart: payload.timeStampBegin, periodEnd: payload.timeStampEnd)
        }
    }
}
#endif
