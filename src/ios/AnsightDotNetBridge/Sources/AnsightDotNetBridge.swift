import Foundation

@objc(ANSDotNetRuntime)
public final class ANSDotNetRuntime: NSObject {
    private static let customPropertiesLock = NSLock()
    private nonisolated(unsafe) static var customProperties: [String: [String: String]] = [:]

    @objc public static var bridgeVersion: String { "1" }

    @objc public static var isInitialized: Bool {
        AnsightRuntime.shared.snapshot().initialized
    }

    @objc public static var isActive: Bool {
        AnsightRuntime.shared.snapshot().active
    }

    @objc(initializeWithOptionsJson:)
    public static func initialize(optionsJson: String?) -> String? {
        perform {
            let options = try makeOptions(optionsJson)
            try AnsightRuntime.shared.initialize(options: options)
        }
    }

    @objc public static func activate() -> String? {
        perform {
            try AnsightRuntime.shared.activate()
        }
    }

    @objc public static func deactivate() {
        AnsightRuntime.shared.deactivate()
    }

    @objc public static func clear() {
        AnsightRuntime.shared.clear()
    }

    @objc(recordMetric:channel:)
    public static func recordMetric(_ value: Int64, channel: Int) -> String? {
        perform {
            try AnsightRuntime.shared.metric(value, channel: channel)
        }
    }

    @objc(recordEventWithLabel:type:details:channel:)
    public static func recordEvent(label: String, type: String?, details: String?, channel: Int) -> String? {
        perform {
            try AnsightRuntime.shared.event(
                label,
                type: eventType(type),
                details: normalized(details),
                channel: channel
            )
        }
    }

    @objc(screenViewedWithName:details:channel:)
    public static func screenViewed(name: String, details: String?, channel: Int) -> String? {
        perform {
            let normalizedDetails = normalized(details)
            try AnsightRuntime.shared.screenViewed(
                name,
                details: normalizedDetails.map { ["details": $0] } ?? [:],
                channel: channel
            )
        }
    }

    @objc(setAppLifecycleState:changedAtUtc:)
    public static func setAppLifecycleState(_ state: String, changedAtUtc: String) {
        AnsightRuntime.shared.setAppLifecycleState(
            lifecycleState(state),
            changedAtUtc: changedAtUtc
        )
    }

    @objc public static func enableFramesPerSecond() {
        AnsightRuntime.shared.enableFramesPerSecond()
    }

    @objc public static func disableFramesPerSecond() {
        AnsightRuntime.shared.disableFramesPerSecond()
    }

    @objc public static func enableTouchCapture() {
        AnsightRuntime.shared.enableTouchCapture()
    }

    @objc public static func disableTouchCapture() {
        AnsightRuntime.shared.disableTouchCapture()
    }

    @objc(setTouchCaptureGuard:)
    public static func setTouchCaptureGuard(_ guardCallback: (() -> Bool)?) {
        guard let guardCallback else {
            AnsightRuntime.shared.setTouchCaptureGuard(nil)
            return
        }

        let guardBox = BridgeTouchCaptureGuard(guardCallback)
        AnsightRuntime.shared.setTouchCaptureGuard {
            guardBox.canCapture()
        }
    }

    @objc(registerCustomPropertyWithGroup:key:value:)
    public static func registerCustomProperty(group: String, key: String, value: String) {
        customPropertiesLock.withLock {
            var properties = customProperties[group] ?? [:]
            properties[key] = value
            customProperties[group] = properties
        }
        publishCustomProperties()
    }

    @objc(removeCustomPropertyWithGroup:key:)
    public static func removeCustomProperty(group: String, key: String) {
        customPropertiesLock.withLock {
            customProperties[group]?.removeValue(forKey: key)
            if customProperties[group]?.isEmpty == true {
                customProperties.removeValue(forKey: group)
            }
        }
        publishCustomProperties()
    }

    @objc public static func clearCustomProperties() {
        customPropertiesLock.withLock {
            customProperties.removeAll()
        }
        publishCustomProperties()
    }

    @objc public static func hostConnectionStatusJson() -> String {
        encode(AnsightRuntime.shared.hostConnectionStatus())
    }

    @objc public static func hostConnectionCapabilitiesJson() -> String {
        encode(AnsightRuntime.shared.hostConnectionCapabilities())
    }

    @objc(telemetrySnapshotJsonAfterMetricSequence:afterEventSequence:)
    public static func telemetrySnapshotJson(
        afterMetricSequence: Int64,
        afterEventSequence: Int64
    ) -> String {
        encode(
            BridgeTelemetrySnapshot(
                metrics: AnsightRuntime.shared.recordedMetrics().filter {
                    $0.sequence > afterMetricSequence
                },
                events: AnsightRuntime.shared.recordedEvents().filter {
                    $0.sequence > afterEventSequence
                }
            )
        )
    }

    @objc(connectWithRequestJson:completion:)
    public static func connect(requestJson: String, completion: @escaping (String) -> Void) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            do {
                let request = try makeHostConnectionRequest(requestJson)
                completionBox.call(encode(await AnsightRuntime.shared.connect(request)))
            } catch {
                completionBox.call(encode(BridgeHostConnectionResult.failure(error.localizedDescription)))
            }
        }
    }

    @objc(disconnectWithCompletion:)
    public static func disconnect(completion: @escaping (String) -> Void) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            completionBox.call(encode(await AnsightRuntime.shared.disconnect()))
        }
    }

    @objc(savePairingConfig:expectedAppId:)
    public static func savePairingConfig(_ pairingJson: String, expectedAppId: String?) -> String {
        encode(
            AnsightRuntime.shared.savePairingConfig(
                pairingJson,
                expectedAppId: normalized(expectedAppId)
            )
        )
    }

    @objc public static func clearSavedPairing() -> String {
        AnsightRuntime.shared.clearSavedPairing()
        return encode(
            BridgeHostConnectionResult.success(
                "Saved Studio registration cleared.",
                kind: "clearSavedConfig",
                source: "savedConfig"
            )
        )
    }

    @objc public static func clearCachedSession() -> String {
        AnsightRuntime.shared.clearCachedSession()
        return encode(OperationResult.success("Cached pairing session cleared."))
    }

    @objc public static func notifyHostConnectionConfigChanged() -> String {
        encode(AnsightRuntime.shared.notifyHostConnectionConfigChanged())
    }

    @objc(sendClientLog:completion:)
    public static func sendClientLog(_ logLine: String, completion: @escaping (String) -> Void) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            completionBox.call(encode(await AnsightRuntime.shared.sendClientLog(logLine)))
        }
    }

    @objc(captureScreenFrameWithCompletion:)
    public static func captureScreenFrame(completion: @escaping (String) -> Void) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            completionBox.call(encode(await AnsightRuntime.shared.captureScreenFrame()))
        }
    }

    @objc(sendControlRequest:payloadJson:completion:)
    public static func sendControlRequest(
        _ action: String,
        payloadJson: String,
        completion: @escaping (String) -> Void
    ) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            do {
                let payload = try JSONDecoder().decode(
                    JSONValue.self,
                    from: Data(payloadJson.utf8)
                )
                completionBox.call(
                    encode(
                        await AnsightRuntime.shared.sendControlRequest(
                            action: action,
                            payload: payload
                        )
                    )
                )
            } catch {
                completionBox.call(encode(OperationResult.failure(error.localizedDescription)))
            }
        }
    }

    @objc(sendBinary:completion:)
    public static func sendBinary(
        _ payload: Data,
        completion: @escaping (String) -> Void
    ) {
        let completionBox = BridgeStringCompletion(completion)
        Task {
            completionBox.call(
                encode(await AnsightRuntime.shared.sendBinaryData(payload))
            )
        }
    }

    @objc(setToolProtocolHandler:)
    public static func setToolProtocolHandler(
        _ handler: ((String) -> String?)?
    ) {
        guard let handler else {
            AnsightRuntime.shared.setExternalToolProtocolHandler(nil)
            return
        }

        let handlerBox = BridgeToolProtocolHandler(handler)
        AnsightRuntime.shared.setExternalToolProtocolHandler { requestJson in
            handlerBox.handle(requestJson)
        }
    }

    @objc(setToolProtocolResponseSentHandler:)
    public static func setToolProtocolResponseSentHandler(
        _ handler: ((String) -> Void)?
    ) {
        guard let handler else {
            AnsightRuntime.shared.setExternalToolProtocolResponseSentHandler(nil)
            return
        }

        let handlerBox = BridgeToolProtocolResponseSentHandler(handler)
        AnsightRuntime.shared.setExternalToolProtocolResponseSentHandler { requestJson in
            handlerBox.responseSent(requestJson)
        }
    }

    private static func makeOptions(_ optionsJson: String?) throws -> AnsightOptions {
        guard let optionsJson = normalized(optionsJson) else {
            customPropertiesLock.withLock {
                customProperties.removeAll()
            }
            return AnsightOptions()
        }

        let bridgeOptions = try JSONDecoder().decode(
            BridgeOptions.self,
            from: Data(optionsJson.utf8)
        )
        var options = AnsightOptions()

        options.sampleFrequencyMilliseconds =
            bridgeOptions.sampleFrequencyMilliseconds ?? options.sampleFrequencyMilliseconds
        options.retentionPeriodSeconds =
            bridgeOptions.retentionPeriodSeconds ?? options.retentionPeriodSeconds
        options.enableFramesPerSecond =
            bridgeOptions.enableFramesPerSecond ?? options.enableFramesPerSecond
        options.enableBatteryLevel =
            bridgeOptions.enableBatteryLevel ?? options.enableBatteryLevel
        options.additionalChannels = bridgeOptions.additionalChannels?.map(\.nativeChannel) ?? []

        if let rawValue = bridgeOptions.defaultMemoryChannels {
            options.defaultMemoryChannels = DefaultMemoryChannels(rawValue: rawValue)
        }
        if let sessionJpegCapture = bridgeOptions.sessionJpegCapture {
            options.sessionJpegCapture = AnsightSessionJpegCaptureOptions(
                intervalMilliseconds: sessionJpegCapture.intervalMilliseconds,
                quality: sessionJpegCapture.quality,
                maxWidth: sessionJpegCapture.maxWidth,
                captureGpuBackedSurfaces: sessionJpegCapture.captureGpuBackedSurfaces
            )
        } else if bridgeOptions.hasSessionJpegCapture == false {
            options.sessionJpegCapture = nil
        }
        if let touchCapture = bridgeOptions.touchCapture {
            options.touchCapture = AnsightTouchCaptureOptions(
                captureMoveEvents: touchCapture.captureMoveEvents,
                captureCancelEvents: touchCapture.captureCancelEvents,
                moveCaptureDistanceThreshold: touchCapture.moveCaptureDistanceThreshold,
                moveCaptureFramesPerSecond: touchCapture.moveCaptureFramesPerSecond
            )
        } else if bridgeOptions.hasTouchCapture == false {
            options.touchCapture = nil
        }

        options.toolGuard = toolGuard(bridgeOptions.toolGuard)
        options.customProperties = bridgeOptions.customProperties ?? [:]
        customPropertiesLock.withLock {
            customProperties = options.customProperties
        }
        if let hostAutoProbe = bridgeOptions.hostAutoProbe {
            options.hostAutoProbe = AnsightHostAutoProbeOptions(
                enabled: hostAutoProbe.enabled,
                initialDelayMilliseconds: hostAutoProbe.initialDelayMilliseconds,
                probeIntervalMilliseconds: hostAutoProbe.probeIntervalMilliseconds,
                reconnectDelayMilliseconds: hostAutoProbe.reconnectDelayMilliseconds,
                clientName: normalized(hostAutoProbe.clientName)
            )
        }
        if let hostConnection = bridgeOptions.hostConnection {
            options.hostConnection = AnsightHostConnectionOptions(
                savedConfigKey: hostConnection.savedConfigKey ?? "ai.ansight.ios.saved-pairing",
                connectionProfileRetentionSeconds: hostConnection.connectionProfileRetentionSeconds,
                discoveryPort: hostConnection.discoveryPort,
                allowCellularConnections: hostConnection.allowCellularConnections,
                bundledConfigJson: normalized(hostConnection.bundledConfigJson)
            )
        }

        return try options.validated()
    }

    private static func makeHostConnectionRequest(_ requestJson: String) throws -> HostConnectionRequest {
        let request = try JSONDecoder().decode(
            BridgeHostConnectionRequest.self,
            from: Data(requestJson.utf8)
        )
        let kind = HostConnectionRequestKind(rawValue: request.kind) ?? .auto
        return HostConnectionRequest(
            kind: kind,
            payload: request.payload,
            clientName: request.clientName,
            expectedAppId: request.expectedAppId,
            hostAddressOverride: request.hostAddressOverride
        )
    }

    private static func encode<T: Encodable>(_ value: T) -> String {
        guard let data = try? JSONEncoder().encode(value),
              let json = String(data: data, encoding: .utf8) else {
            return #"{"success":false,"message":"The native Ansight bridge could not encode its response."}"#
        }
        return json
    }

    private static func publishCustomProperties() {
        let properties = customPropertiesLock.withLock {
            customProperties
        }
        Task {
            _ = await AnsightRuntime.shared.updateSessionProperties(properties)
        }
    }

    private static func perform(_ action: () throws -> Void) -> String? {
        do {
            try action()
            return nil
        } catch {
            return error.localizedDescription
        }
    }

    private static func normalized(_ value: String?) -> String? {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed?.isEmpty == false ? trimmed : nil
    }

    private static func eventType(_ value: String?) -> AnsightEventType {
        guard let normalized = normalized(value) else {
            return .info
        }
        return AnsightEventType.allCases.first {
            $0.rawValue.caseInsensitiveCompare(normalized) == .orderedSame
        } ?? .info
    }

    private static func lifecycleState(_ value: String) -> AppLifecycleState {
        switch value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case AppLifecycleState.foreground.rawValue:
            return .foreground
        case AppLifecycleState.background.rawValue:
            return .background
        default:
            return .unknown
        }
    }

    private static func toolGuard(_ value: String?) -> AnsightToolGuard {
        switch normalized(value)?.lowercased() {
        case "readonly":
            return .readOnly
        case "readwrite":
            return .readWrite
        case "fullaccess":
            return .fullAccess
        default:
            return .disabled
        }
    }
}

private final class BridgeStringCompletion: @unchecked Sendable {
    private let completion: (String) -> Void

    init(_ completion: @escaping (String) -> Void) {
        self.completion = completion
    }

    func call(_ result: String) {
        completion(result)
    }
}

private final class BridgeToolProtocolHandler: @unchecked Sendable {
    private let handler: (String) -> String?

    init(_ handler: @escaping (String) -> String?) {
        self.handler = handler
    }

    func handle(_ requestJson: String) -> String? {
        handler(requestJson)
    }
}

private final class BridgeTouchCaptureGuard: @unchecked Sendable {
    private let guardCallback: () -> Bool

    init(_ guardCallback: @escaping () -> Bool) {
        self.guardCallback = guardCallback
    }

    func canCapture() -> Bool {
        guardCallback()
    }
}

private final class BridgeToolProtocolResponseSentHandler: @unchecked Sendable {
    private let handler: (String) -> Void

    init(_ handler: @escaping (String) -> Void) {
        self.handler = handler
    }

    func responseSent(_ requestJson: String) {
        handler(requestJson)
    }
}

private struct BridgeHostConnectionRequest: Decodable {
    let kind: String
    let payload: String?
    let clientName: String?
    let expectedAppId: String?
    let hostAddressOverride: String?
}

private struct BridgeTelemetrySnapshot: Encodable {
    let metrics: [RecordedMetric]
    let events: [RecordedEvent]
}

private struct BridgeHostConnectionResult: Encodable {
    let success: Bool
    let message: String
    let kind: String
    let source: String
    let reasonCode: String?

    static func success(
        _ message: String,
        kind: String = "none",
        source: String = "none"
    ) -> BridgeHostConnectionResult {
        BridgeHostConnectionResult(
            success: true,
            message: message,
            kind: kind,
            source: source,
            reasonCode: nil
        )
    }

    static func failure(_ message: String) -> BridgeHostConnectionResult {
        BridgeHostConnectionResult(
            success: false,
            message: message,
            kind: "connect",
            source: "none",
            reasonCode: nil
        )
    }
}

private struct BridgeOptions: Decodable {
    let sampleFrequencyMilliseconds: Int?
    let retentionPeriodSeconds: Int?
    let enableFramesPerSecond: Bool?
    let enableBatteryLevel: Bool?
    let additionalChannels: [BridgeChannel]?
    let defaultMemoryChannels: Int?
    let sessionJpegCapture: BridgeSessionJpegCapture?
    let touchCapture: BridgeTouchCapture?
    let toolGuard: String?
    let customProperties: [String: [String: String]]?
    let hostAutoProbe: BridgeHostAutoProbe?
    let hostConnection: BridgeHostConnection?

    var hasSessionJpegCapture: Bool {
        sessionJpegCapture != nil
    }

    var hasTouchCapture: Bool {
        touchCapture != nil
    }
}

private struct BridgeChannel: Decodable {
    let id: Int
    let name: String
    let color: String?
    let unit: String?
    let type: String?
    let source: String?
    let group: String?
    let kind: String?

    var nativeChannel: AnsightChannel {
        AnsightChannel(
            id: id,
            name: name,
            color: color,
            unit: unit,
            type: type ?? "custom",
            source: source,
            group: group,
            kind: kind
        )
    }
}

private struct BridgeSessionJpegCapture: Decodable {
    let intervalMilliseconds: Int
    let quality: Int
    let maxWidth: Int?
    let captureGpuBackedSurfaces: Bool
}

private struct BridgeTouchCapture: Decodable {
    let captureMoveEvents: Bool
    let captureCancelEvents: Bool
    let moveCaptureDistanceThreshold: Double
    let moveCaptureFramesPerSecond: Int
}

private struct BridgeHostAutoProbe: Decodable {
    let enabled: Bool
    let initialDelayMilliseconds: Int
    let probeIntervalMilliseconds: Int
    let reconnectDelayMilliseconds: Int
    let clientName: String?
}

private struct BridgeHostConnection: Decodable {
    let savedConfigKey: String?
    let connectionProfileRetentionSeconds: Int
    let discoveryPort: Int?
    let allowCellularConnections: Bool
    let bundledConfigJson: String?
}
