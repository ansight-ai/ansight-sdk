import Ansight
import Foundation
import React

@objc(AnsightReactNative)
final class AnsightReactNative: RCTEventEmitter {
    private final class PendingToolCall {
        let semaphore = DispatchSemaphore(value: 0)
        var result: AnsightToolExecutionResult?
    }

    private final class ReactNativeMemorySamplerBox: @unchecked Sendable {
        private let sampler: NSObject

        init(sampler: NSObject) {
            self.sampler = sampler
        }

        func attach(to bridge: RCTBridge?) {
            guard let bridge else {
                return
            }

            let selector = NSSelectorFromString("attachToBridge:")
            guard sampler.responds(to: selector) else {
                return
            }
            _ = sampler.perform(selector, with: bridge)
        }

        func sample(selectorName: String) -> Int64? {
            let selector = NSSelectorFromString(selectorName)
            guard sampler.responds(to: selector),
                  let result = sampler.perform(selector)?.takeUnretainedValue() as? NSNumber else {
                return nil
            }
            return result.int64Value
        }
    }

    private struct ReactNativeMemoryProfilingOptions {
        var enabled = true
        var jsHeapUsed = true
        var jsHeapTotal = true

        static let defaults = ReactNativeMemoryProfilingOptions()
        static let disabled = ReactNativeMemoryProfilingOptions(enabled: false, jsHeapUsed: false, jsHeapTotal: false)

        init(enabled: Bool = true, jsHeapUsed: Bool = true, jsHeapTotal: Bool = true) {
            self.enabled = enabled
            self.jsHeapUsed = jsHeapUsed
            self.jsHeapTotal = jsHeapTotal
        }

        init(dictionary: NSDictionary?) {
            guard let raw = dictionary?["reactNativeMemory"], !(raw is NSNull) else {
                if dictionary?.object(forKey: "reactNativeMemory") is NSNull {
                    self = .disabled
                } else {
                    self = .defaults
                }
                return
            }

            if let enabled = raw as? NSNumber {
                self = enabled.boolValue ? .defaults : .disabled
                return
            }

            guard let options = raw as? NSDictionary else {
                self = .defaults
                return
            }

            let enabled = boolValue(options, "enabled", defaultValue: true)
            let jsHeap = boolValue(options, "jsHeap", defaultValue: true)
            self.init(
                enabled: enabled,
                jsHeapUsed: boolValue(options, "jsHeapUsed", defaultValue: jsHeap),
                jsHeapTotal: boolValue(options, "jsHeapTotal", defaultValue: jsHeap)
            )
        }

        var dictionary: [String: Any] {
            [
                "enabled": enabled,
                "jsHeapUsed": jsHeapUsed,
                "jsHeapTotal": jsHeapTotal,
            ]
        }
    }

    private enum ReactNativeMemoryChannels {
        static let jsHeapUsed = AnsightChannel(
            id: 32,
            name: "React Native JS heap used",
            colorHex: "#61DAFB",
            unit: "bytes",
            type: "memory",
            source: "reactNative",
            group: "React Native",
            kind: "react_native_js_heap_used"
        )

        static let jsHeapTotal = AnsightChannel(
            id: 33,
            name: "React Native JS heap total",
            colorHex: "#0A84FF",
            unit: "bytes",
            type: "memory",
            source: "reactNative",
            group: "React Native",
            kind: "react_native_js_heap_total"
        )
    }

    private final class ReactNativeTool: AnsightTool, @unchecked Sendable {
        let descriptor: AnsightToolDescriptor
        private weak var module: AnsightReactNative?
        private let timeoutMilliseconds: Int

        init(descriptor: AnsightToolDescriptor, module: AnsightReactNative, timeoutMilliseconds: Int) {
            self.descriptor = descriptor
            self.module = module
            self.timeoutMilliseconds = timeoutMilliseconds
        }

        func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
            guard let module else {
                return .failure("React Native bridge is no longer available.", errorCode: "javascript_bridge_unavailable")
            }
            return module.executeJavaScriptTool(
                toolId: descriptor.id,
                arguments: arguments,
                timeoutMilliseconds: timeoutMilliseconds
            )
        }
    }

    private let lock = NSLock()
    private var hasListeners = false
    private var activeCustomToolIds: Set<String> = []
    private var pendingToolCalls: [String: PendingToolCall] = [:]
    private var reactNativeMemorySampler: ReactNativeMemorySamplerBox?
    private var currentReactNativeMemoryOptions = ReactNativeMemoryProfilingOptions.defaults
    private lazy var logCallback = AnsightClosureLogCallback { [weak self] level, message, error in
        self?.emitLogEvent(level: level, message: message, error: error)
    }

    override init() {
        super.init()
        AnsightLogger.registerCallback(logCallback)
    }

    deinit {
        AnsightLogger.removeCallback(logCallback)
    }

    override static func requiresMainQueueSetup() -> Bool {
        false
    }

    override func supportedEvents() -> [String]! {
        ["AnsightToolCall", "AnsightLog"]
    }

    override func startObserving() {
        _ = lock.withLock {
            hasListeners = true
        }
    }

    override func stopObserving() {
        _ = lock.withLock {
            hasListeners = false
        }
    }

    @objc(initialize:resolver:rejecter:)
    func initialize(
        _ options: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            let toolOptions = remoteToolOptions(options)
            try AnsightRuntime.shared.initialize(options: buildOptions(options))
            try configureReactNativeMemoryProfiling(options)
            try AnsightRuntime.shared.registerAnsightRemoteTools(options: toolOptions)
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(initializeAndActivate:resolver:rejecter:)
    func initializeAndActivate(
        _ options: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk(
                options: buildOptions(options),
                remoteToolOptions: remoteToolOptions(options)
            )
            try configureReactNativeMemoryProfiling(options)
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(activate:rejecter:)
    func activate(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        do {
            try AnsightRuntime.shared.activate()
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(deactivate:rejecter:)
    func deactivate(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.deactivate()
        resolve(snapshotDictionary())
    }

    @objc(clear:rejecter:)
    func clear(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.clear()
        resolve(snapshotDictionary())
    }

    @objc(registerMetricChannel:resolver:rejecter:)
    func registerMetricChannel(
        _ channel: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.registerMetricChannel(
                AnsightChannel(
                    id: intValue(channel, "id", defaultValue: -1),
                    name: stringValue(channel, "name") ?? "",
                    colorHex: stringValue(channel, "colorHex"),
                    unit: stringValue(channel, "unit"),
                    type: stringValue(channel, "type") ?? "custom",
                    source: stringValue(channel, "source"),
                    group: stringValue(channel, "group"),
                    kind: stringValue(channel, "kind")
                )
            )
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(recordMetric:channel:resolver:rejecter:)
    func recordMetric(
        _ value: Double,
        channel: Double,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.metric(Int64(value), channel: Int(channel))
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(recordEvent:resolver:rejecter:)
    func recordEvent(
        _ input: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.event(
                stringValue(input, "label") ?? "",
                type: eventType(stringValue(input, "type")),
                details: stringValue(input, "details"),
                channel: intValue(input, "channel", defaultValue: AnsightChannels.unspecified)
            )
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(screenViewed:details:resolver:rejecter:)
    func screenViewed(
        _ name: NSString,
        details: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.screenViewed(name as String, details: stringDictionary(details))
            resolve(snapshotDictionary())
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(setAppLifecycleState:resolver:rejecter:)
    func setAppLifecycleState(
        _ state: NSString,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        AnsightRuntime.shared.setAppLifecycleState(lifecycleState(state as String))
        resolve(snapshotDictionary())
    }

    @objc(connect:options:resolver:rejecter:)
    func connect(
        _ pairingPayload: NSString?,
        options: NSDictionary?,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let clientName = stringValue(options, "clientName")
            let expectedAppId = stringValue(options, "expectedAppId")
            let hostAddressOverride = stringValue(options, "hostAddressOverride")
            let request: HostConnectionRequest
            if let payload = pairingPayload as String?, !payload.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                request = .payloadText(
                    payload,
                    clientName: clientName,
                    expectedAppId: expectedAppId,
                    hostAddressOverride: hostAddressOverride,
                    sourceDescription: "React Native"
                )
            } else {
                request = .auto(
                    clientName: clientName,
                    expectedAppId: expectedAppId,
                    hostAddressOverride: hostAddressOverride,
                    sourceDescription: "React Native"
                )
            }
            let result = await AnsightRuntime.shared.connect(request)
            resolve(hostConnectionResultDictionary(result))
        }
    }

    @objc(openSession:options:resolver:rejecter:)
    func openSession(
        _ pairingPayload: NSString?,
        options: NSDictionary?,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            do {
                let result = try await AnsightRuntime.shared.openLiveSession(
                    pairingJson: pairingPayload as String? ?? "",
                    options: pairingOpenOptions(options)
                )
                resolve(openSessionResultDictionary(result))
            } catch {
                reject("ansight_error", error.localizedDescription, error)
            }
        }
    }

    @objc(disconnect:rejecter:)
    func disconnect(_ resolve: @escaping RCTPromiseResolveBlock, rejecter reject: @escaping RCTPromiseRejectBlock) {
        Task {
            let result = await AnsightRuntime.shared.disconnect()
            resolve(hostConnectionResultDictionary(result))
        }
    }

    @objc(completeSession:rejecter:)
    func completeSession(_ resolve: @escaping RCTPromiseResolveBlock, rejecter reject: @escaping RCTPromiseRejectBlock) {
        Task {
            let result = await AnsightRuntime.shared.completeLiveSession()
            resolve(operationResultDictionary(result))
        }
    }

    @objc(closeSession:rejecter:)
    func closeSession(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.closeSession()
        resolve(operationResultDictionary(.success("Session closed.")))
    }

    @objc(savePairingConfig:options:resolver:rejecter:)
    func savePairingConfig(
        _ pairingPayload: NSString?,
        options: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let result = AnsightRuntime.shared.savePairingConfig(
            pairingPayload as String? ?? "",
            expectedAppId: stringValue(options, "expectedAppId")
        )
        resolve(hostConnectionResultDictionary(result))
    }

    @objc(clearSavedPairing:rejecter:)
    func clearSavedPairing(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.clearSavedPairing()
        resolve(hostConnectionResultDictionary(HostConnectionResult(
            success: true,
            message: "Saved pairing config cleared.",
            kind: .savedConfig,
            source: .savedConfig
        )))
    }

    @objc(clearCachedSession:rejecter:)
    func clearCachedSession(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.clearCachedSession()
        resolve(operationResultDictionary(.success("Cached live session cleared.")))
    }

    @objc(notifyHostConnectionConfigChanged:rejecter:)
    func notifyHostConnectionConfigChanged(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(hostConnectionResultDictionary(AnsightRuntime.shared.notifyHostConnectionConfigChanged()))
    }

    @objc(status:rejecter:)
    func status(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(snapshotDictionary())
    }

    @objc(snapshot:rejecter:)
    func snapshot(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(snapshotDictionary())
    }

    @objc(hostConnectionStatus:rejecter:)
    func hostConnectionStatus(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(hostConnectionStatusDictionary(AnsightRuntime.shared.hostConnectionStatus()))
    }

    @objc(hostConnectionCapabilities:rejecter:)
    func hostConnectionCapabilities(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(hostConnectionCapabilitiesDictionary(AnsightRuntime.shared.hostConnectionCapabilities()))
    }

    @objc(currentOptions:rejecter:)
    func currentOptions(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(optionsDictionary(AnsightRuntime.shared.currentOptions()))
    }

    @objc(recordedMetrics:resolver:rejecter:)
    func recordedMetrics(
        _ limit: NSNumber,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let metrics = AnsightRuntime.shared.recordedMetrics()
        let count = max(0, limit.intValue)
        resolve((count > 0 ? Array(metrics.suffix(count)) : metrics).map(metricDictionary))
    }

    @objc(recordedEvents:resolver:rejecter:)
    func recordedEvents(
        _ limit: NSNumber,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let events = AnsightRuntime.shared.recordedEvents()
        let count = max(0, limit.intValue)
        resolve((count > 0 ? Array(events.suffix(count)) : events).map(eventDictionary))
    }

    @objc(sendClientLog:resolver:rejecter:)
    func sendClientLog(
        _ line: NSString,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let result = await AnsightRuntime.shared.sendClientLog(line as String)
            resolve(operationResultDictionary(result))
        }
    }

    @objc(captureBuiltInTelemetrySample:rejecter:)
    func captureBuiltInTelemetrySample(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.captureBuiltInTelemetrySample()
        resolve(snapshotDictionary())
    }

    @objc(isFramesPerSecondEnabled:rejecter:)
    func isFramesPerSecondEnabled(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(AnsightRuntime.shared.isFramesPerSecondEnabled)
    }

    @objc(enableFramesPerSecond:rejecter:)
    func enableFramesPerSecond(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.enableFramesPerSecond()
        resolve(snapshotDictionary())
    }

    @objc(disableFramesPerSecond:rejecter:)
    func disableFramesPerSecond(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.disableFramesPerSecond()
        resolve(snapshotDictionary())
    }

    @objc(captureScreenFrame:resolver:rejecter:)
    func captureScreenFrame(
        _ options: NSDictionary?,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let result = await AnsightRuntime.shared.captureScreenFrame(options: screenCaptureOptions(options))
            resolve(operationResultDictionary(result))
        }
    }

    @objc(enableTouchCapture:rejecter:)
    func enableTouchCapture(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.enableTouchCapture()
        resolve(snapshotDictionary())
    }

    @objc(disableTouchCapture:rejecter:)
    func disableTouchCapture(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        AnsightRuntime.shared.disableTouchCapture()
        resolve(snapshotDictionary())
    }

    @objc(updateSessionProperties:resolver:rejecter:)
    func updateSessionProperties(
        _ properties: NSDictionary?,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let result = await AnsightRuntime.shared.updateSessionProperties(groupedStringDictionary(properties))
            resolve(operationResultDictionary(result))
        }
    }

    @objc(clearSessionProperties:rejecter:)
    func clearSessionProperties(
        _ resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let result = await AnsightRuntime.shared.clearSessionProperties()
            resolve(operationResultDictionary(result))
        }
    }

    @objc(registerCustomProperty:key:value:resolver:rejecter:)
    func registerCustomProperty(
        _ group: NSString,
        key: NSString,
        value: NSString,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let normalizedGroup = (group as String).trimmingCharacters(in: .whitespacesAndNewlines)
            let normalizedKey = (key as String).trimmingCharacters(in: .whitespacesAndNewlines)
            guard !normalizedGroup.isEmpty else {
                resolve(operationResultDictionary(.failure("Custom property group must not be blank.")))
                return
            }
            guard !normalizedKey.isEmpty else {
                resolve(operationResultDictionary(.failure("Custom property key must not be blank.")))
                return
            }
            var properties = AnsightRuntime.shared.currentOptions().customProperties
            var groupProperties = properties[normalizedGroup] ?? [:]
            groupProperties[normalizedKey] = (value as String).trimmingCharacters(in: .whitespacesAndNewlines)
            properties[normalizedGroup] = groupProperties
            let result = await AnsightRuntime.shared.updateSessionProperties(properties)
            resolve(operationResultDictionary(result))
        }
    }

    @objc(removeCustomProperty:key:resolver:rejecter:)
    func removeCustomProperty(
        _ group: NSString,
        key: NSString,
        resolver resolve: @escaping RCTPromiseResolveBlock,
        rejecter reject: @escaping RCTPromiseRejectBlock
    ) {
        Task {
            let normalizedGroup = (group as String).trimmingCharacters(in: .whitespacesAndNewlines)
            let normalizedKey = (key as String).trimmingCharacters(in: .whitespacesAndNewlines)
            guard !normalizedGroup.isEmpty else {
                resolve(operationResultDictionary(.failure("Custom property group must not be blank.")))
                return
            }
            guard !normalizedKey.isEmpty else {
                resolve(operationResultDictionary(.failure("Custom property key must not be blank.")))
                return
            }
            var properties = AnsightRuntime.shared.currentOptions().customProperties
            if var groupProperties = properties[normalizedGroup] {
                groupProperties.removeValue(forKey: normalizedKey)
                if groupProperties.isEmpty {
                    properties.removeValue(forKey: normalizedGroup)
                } else {
                    properties[normalizedGroup] = groupProperties
                }
            }
            let result = await AnsightRuntime.shared.updateSessionProperties(properties)
            resolve(operationResultDictionary(result))
        }
    }

    @objc(registerCustomTool:resolver:rejecter:)
    func registerCustomTool(
        _ definition: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            let descriptor = try toolDescriptor(definition)
            let timeout = max(250, intValue(definition, "timeoutMilliseconds", defaultValue: 30_000))
            _ = lock.withLock {
                activeCustomToolIds.insert(descriptor.id)
            }
            try AnsightRuntime.shared.registerTool(
                ReactNativeTool(descriptor: descriptor, module: self, timeoutMilliseconds: timeout),
                replaceExisting: true
            )
            resolve(["id": descriptor.id, "registered": true])
        } catch {
            reject("ansight_error", error.localizedDescription, error)
        }
    }

    @objc(unregisterCustomTool:resolver:rejecter:)
    func unregisterCustomTool(
        _ toolId: NSString,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let id = (toolId as String).trimmingCharacters(in: .whitespacesAndNewlines)
        _ = lock.withLock {
            activeCustomToolIds.remove(id)
        }
        resolve(["id": id, "registered": false])
    }

    @objc(clearRegisteredCustomTools:rejecter:)
    func clearRegisteredCustomTools(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        _ = lock.withLock {
            activeCustomToolIds.removeAll()
        }
        resolve(["cleared": true])
    }

    @objc(resolveToolCall:result:resolver:rejecter:)
    func resolveToolCall(
        _ requestId: NSString,
        result: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let id = requestId as String
        let pending = lock.withLock { pendingToolCalls[id] }
        guard let pending else {
            resolve(["requestId": id, "accepted": false])
            return
        }

        let success = boolValue(result, "success", defaultValue: true)
        let message = stringValue(result, "message")
        let errorCode = stringValue(result, "errorCode")
        let payload = jsonValue(result["result"])
        pending.result = success
            ? .success(payload, message: message)
            : .failure(message ?? "JavaScript tool failed.", errorCode: errorCode, result: payload)
        pending.semaphore.signal()
        resolve(["requestId": id, "accepted": true])
    }

    @objc(queueBinaryTransfer:base64Data:chunkBytes:resolver:rejecter:)
    func queueBinaryTransfer(
        _ requestId: NSString,
        base64Data: NSString,
        chunkBytes: NSNumber,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let normalizedRequestId = (requestId as String).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedRequestId.isEmpty else {
            resolve([
                "success": false,
                "message": "Binary transfer requires a live tool request id.",
                "errorCode": "artifact_request_unavailable",
            ])
            return
        }

        guard let data = Data(base64Encoded: base64Data as String) else {
            resolve([
                "success": false,
                "message": "Binary transfer payload must be base64 encoded.",
                "errorCode": "artifact_payload_invalid",
            ])
            return
        }

        let transferId = UUID()
        let normalizedChunkBytes = min(max(chunkBytes.intValue, 1_024), 512 * 1_024)
        let result = AnsightRuntime.shared.queueBinaryTransfer(
            requestId: normalizedRequestId,
            transferId: transferId,
            data: data,
            chunkBytes: normalizedChunkBytes,
            description: "react-native-artifact:\(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
        )
        var payload: [String: Any] = [
            "success": result.success,
            "message": result.message,
        ]
        payload["transferId"] = transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        payload["deliveryMode"] = "websocket_binary"
        payload["wireProtocol"] = PairingFileTransferWireProtocol.protocolName
        payload["status"] = result.success ? "queued" : "failed"
        payload["chunkBytes"] = normalizedChunkBytes
        payload["sizeBytes"] = data.count
        if !result.success {
            payload["errorCode"] = "artifact_transfer_unavailable"
        }
        resolve(payload)
    }

    fileprivate func executeJavaScriptTool(
        toolId: String,
        arguments: [String: String],
        timeoutMilliseconds: Int
    ) -> AnsightToolExecutionResult {
        let requestId = "ios.\(UUID().uuidString.replacingOccurrences(of: "-", with: ""))"
        let pending = PendingToolCall()
        let canCallJavaScript = lock.withLock { () -> Bool in
            guard activeCustomToolIds.contains(toolId) else {
                return false
            }
            pendingToolCalls[requestId] = pending
            return hasListeners
        }

        guard canCallJavaScript else {
            _ = lock.withLock {
                pendingToolCalls.removeValue(forKey: requestId)
            }
            return .failure("React Native JavaScript bridge is not listening for Ansight tool calls.", errorCode: "javascript_bridge_unavailable")
        }

        DispatchQueue.main.async {
            var body: [String: Any] = [
                "requestId": requestId,
                "toolId": toolId,
                "platform": "ios",
                "arguments": arguments,
            ]
            if let nativeRequestId = arguments[AnsightToolExecutionArgumentNames.requestId] {
                body["nativeRequestId"] = nativeRequestId
            }
            if let sessionId = arguments[AnsightToolExecutionArgumentNames.sessionId] {
                body["sessionId"] = sessionId
            }

            self.sendEvent(
                withName: "AnsightToolCall",
                body: body
            )
        }

        let deadline = DispatchTime.now() + .milliseconds(timeoutMilliseconds)
        guard pending.semaphore.wait(timeout: deadline) == .success else {
            _ = lock.withLock {
                pendingToolCalls.removeValue(forKey: requestId)
            }
            return .failure("JavaScript handler for tool '\(toolId)' timed out.", errorCode: "javascript_tool_timeout")
        }

        _ = lock.withLock {
            pendingToolCalls.removeValue(forKey: requestId)
        }
        return pending.result ?? .failure("JavaScript handler for tool '\(toolId)' returned no result.", errorCode: "javascript_tool_empty_result")
    }

    private func emitLogEvent(level: AnsightLogLevel, message: String, error: Error?) {
        let shouldEmit = lock.withLock { hasListeners }
        guard shouldEmit else {
            return
        }

        var body: [String: Any] = [
            "level": level.rawValue,
            "message": message,
            "platform": "ios",
        ]
        if let error {
            body["error"] = error.localizedDescription
        }

        DispatchQueue.main.async {
            self.sendEvent(withName: "AnsightLog", body: body)
        }
    }

    private func configureReactNativeMemoryProfiling(_ dictionary: NSDictionary?) throws {
        let options = ReactNativeMemoryProfilingOptions(dictionary: dictionary)
        currentReactNativeMemoryOptions = options

        guard options.enabled, let sampler = reactNativeMemorySamplerBox() else {
            return
        }

        sampler.attach(to: bridge)
        if options.jsHeapUsed {
            try AnsightRuntime.shared.registerMetricStream(
                AnsightMetricStream(channel: ReactNativeMemoryChannels.jsHeapUsed) {
                    sampler.sample(selectorName: "jsHeapUsedBytes")
                }
            )
        }
        if options.jsHeapTotal {
            try AnsightRuntime.shared.registerMetricStream(
                AnsightMetricStream(channel: ReactNativeMemoryChannels.jsHeapTotal) {
                    sampler.sample(selectorName: "jsHeapTotalBytes")
                }
            )
        }
    }

    private func reactNativeMemorySamplerBox() -> ReactNativeMemorySamplerBox? {
        if let reactNativeMemorySampler {
            return reactNativeMemorySampler
        }

        guard let samplerType = NSClassFromString("AnsightReactNativeMemorySampler") as? NSObject.Type else {
            return nil
        }

        let sampler = ReactNativeMemorySamplerBox(sampler: samplerType.init())
        reactNativeMemorySampler = sampler
        return sampler
    }

    private func buildOptions(_ dictionary: NSDictionary?) throws -> AnsightOptions {
        let developerMode = boolValue(dictionary, "developerMode", defaultValue: true)
        var options = developerMode ? AnsightOptions.ansightDeveloperDefaults : AnsightOptions()

        if let value = stringValue(dictionary, "pairingConfigJson") {
            if developerMode {
                options.hostConnection.bundledDeveloperConfigJson = value
            } else {
                options.hostConnection.bundledConfigJson = value
            }
        }
        if let value = stringValue(dictionary, "clientName") {
            options.hostAutoProbe.clientName = value
        }
        if hasNumber(dictionary, "sampleFrequencyMilliseconds") {
            options.sampleFrequencyMilliseconds = intValue(dictionary, "sampleFrequencyMilliseconds", defaultValue: options.sampleFrequencyMilliseconds)
        }
        if hasNumber(dictionary, "retentionPeriodSeconds") {
            options.retentionPeriodSeconds = intValue(dictionary, "retentionPeriodSeconds", defaultValue: options.retentionPeriodSeconds)
        }
        if hasBool(dictionary, "enableFramesPerSecond") {
            options.enableFramesPerSecond = boolValue(dictionary, "enableFramesPerSecond", defaultValue: options.enableFramesPerSecond)
        }
        if hasBool(dictionary, "enableBatteryLevel") {
            options.enableBatteryLevel = boolValue(dictionary, "enableBatteryLevel", defaultValue: options.enableBatteryLevel)
        }
        if let memory = dictionary?["defaultMemoryChannels"] as? NSDictionary {
            var channels: DefaultMemoryChannels = []
            if boolValue(memory, "managedHeap", defaultValue: boolValue(memory, "javaHeap", defaultValue: false)) {
                channels.insert(.managedHeap)
            }
            if boolValue(memory, "nativeHeap", defaultValue: false) {
                channels.insert(.nativeHeap)
            }
            if boolValue(memory, "residentSetSize", defaultValue: boolValue(memory, "rss", defaultValue: false)) {
                channels.insert(.residentSetSize)
            }
            if boolValue(memory, "physicalFootprint", defaultValue: boolValue(memory, "rss", defaultValue: false)) {
                channels.insert(.physicalFootprint)
            }
            options.defaultMemoryChannels = channels
        }
        if let channels = dictionary?["additionalChannels"] as? [NSDictionary] {
            options.additionalChannels = channels.map {
                AnsightChannel(
                    id: intValue($0, "id", defaultValue: -1),
                    name: stringValue($0, "name") ?? "",
                    colorHex: stringValue($0, "colorHex"),
                    unit: stringValue($0, "unit"),
                    type: stringValue($0, "type") ?? "custom",
                    source: stringValue($0, "source"),
                    group: stringValue($0, "group"),
                    kind: stringValue($0, "kind")
                )
            }
        }
        if let raw = dictionary?["sessionJpegCapture"] {
            if let enabled = raw as? Bool, enabled == false {
                options.sessionJpegCapture = nil
            } else if let jpeg = raw as? NSDictionary {
                options.sessionJpegCapture = AnsightSessionJpegCaptureOptions(
                    intervalMilliseconds: intValue(jpeg, "intervalMilliseconds", defaultValue: 1_000),
                    quality: intValue(jpeg, "quality", defaultValue: 75),
                    maxWidth: optionalInt(jpeg, "maxWidth")
                )
            }
        }
        if let raw = dictionary?["touchCapture"] {
            if let enabled = raw as? Bool, enabled == false {
                options.touchCapture = nil
            } else if let touch = raw as? NSDictionary {
                options.touchCapture = AnsightTouchCaptureOptions(
                    captureMoveEvents: boolValue(touch, "captureMoveEvents", defaultValue: true),
                    captureCancelEvents: boolValue(touch, "captureCancelEvents", defaultValue: true),
                    moveCaptureDistanceThreshold: doubleValue(touch, "moveCaptureDistanceThreshold", defaultValue: AnsightTouchCaptureOptions.defaultMoveCaptureDistanceThreshold),
                    moveCaptureFramesPerSecond: intValue(touch, "moveCaptureFramesPerSecond", defaultValue: AnsightTouchCaptureOptions.defaultMoveCaptureFramesPerSecond)
                )
            }
        }
        if let lifecycle = dictionary?["lifecycleCapture"] as? NSDictionary {
            options.lifecycleCapture = AnsightLifecycleCaptureOptions(
                enabled: boolValue(lifecycle, "enabled", defaultValue: options.lifecycleCapture.enabled),
                captureAppLifecycle: boolValue(lifecycle, "captureAppLifecycle", defaultValue: options.lifecycleCapture.captureAppLifecycle),
                captureScreenViews: boolValue(lifecycle, "captureScreenViews", defaultValue: options.lifecycleCapture.captureScreenViews),
                minimumScreenViewIntervalMilliseconds: intValue(
                    lifecycle,
                    "minimumScreenViewIntervalMilliseconds",
                    defaultValue: options.lifecycleCapture.minimumScreenViewIntervalMilliseconds
                )
            )
        }
        if let guardName = stringValue(dictionary, "toolGuard") {
            options.toolGuard = toolGuard(guardName)
        }
        if let properties = dictionary?["customProperties"] as? NSDictionary {
            options.customProperties = groupedStringDictionary(properties)
        }
        if let autoProbe = dictionary?["hostAutoProbe"] as? NSDictionary {
            options.hostAutoProbe = AnsightHostAutoProbeOptions(
                enabled: boolValue(autoProbe, "enabled", defaultValue: options.hostAutoProbe.enabled),
                initialDelayMilliseconds: intValue(autoProbe, "initialDelayMilliseconds", defaultValue: options.hostAutoProbe.initialDelayMilliseconds),
                probeIntervalMilliseconds: intValue(autoProbe, "probeIntervalMilliseconds", defaultValue: options.hostAutoProbe.probeIntervalMilliseconds),
                reconnectDelayMilliseconds: intValue(autoProbe, "reconnectDelayMilliseconds", defaultValue: options.hostAutoProbe.reconnectDelayMilliseconds),
                clientName: stringValue(autoProbe, "clientName") ?? options.hostAutoProbe.clientName
            )
        }
        if let host = dictionary?["hostConnection"] as? NSDictionary {
            options.hostConnection = AnsightHostConnectionOptions(
                savedConfigKey: stringValue(host, "savedConfigKey") ?? options.hostConnection.savedConfigKey,
                connectionProfileRetentionSeconds: intValue(host, "connectionProfileRetentionSeconds", defaultValue: options.hostConnection.connectionProfileRetentionSeconds),
                discoveryPort: optionalInt(host, "discoveryPort") ?? options.hostConnection.discoveryPort,
                bundledDeveloperConfigJson: stringValue(host, "bundledDeveloperConfigJson") ?? options.hostConnection.bundledDeveloperConfigJson,
                bundledConfigJson: stringValue(host, "bundledConfigJson") ?? options.hostConnection.bundledConfigJson
            )
        }
        return try options.validated()
    }

    private func remoteToolOptions(_ dictionary: NSDictionary?) -> AnsightRemoteToolOptions {
        let remoteTools = dictionary?["remoteTools"] as? NSDictionary
        return AnsightRemoteToolOptions(
            database: databaseToolsOptions(remoteTools?["database"] as? NSDictionary),
            fileSystem: fileSystemToolsOptions(remoteTools?["fileSystem"] as? NSDictionary),
            preferences: preferencesToolsOptions(remoteTools?["preferences"] as? NSDictionary),
            reflection: reflectionToolsOptions(remoteTools?["reflection"] as? NSDictionary),
            secureStorage: secureStorageToolsOptions(
                remoteTools?["secureStorage"] as? NSDictionary ?? dictionary?["secureStorage"] as? NSDictionary
            )
        )
    }

    private func fileSystemToolsOptions(_ dictionary: NSDictionary?) -> AnsightFileSystemToolsOptions {
        AnsightFileSystemToolsOptions(
            additionalRoots: rootDictionaries(dictionary?["additionalRoots"]).map {
                AnsightFileSystemRoot(
                    alias: stringValue($0, "alias") ?? "",
                    path: stringValue($0, "path") ?? ""
                )
            }
        )
    }

    private func databaseToolsOptions(_ dictionary: NSDictionary?) -> AnsightDatabaseToolsOptions {
        AnsightDatabaseToolsOptions(
            additionalRoots: rootDictionaries(dictionary?["additionalRoots"]).map {
                AnsightDatabaseRoot(
                    alias: stringValue($0, "alias") ?? "",
                    path: stringValue($0, "path") ?? ""
                )
            },
            includePlatformRoots: boolValue(dictionary, "includePlatformRoots", defaultValue: true)
        )
    }

    private func preferencesToolsOptions(_ dictionary: NSDictionary?) -> AnsightPreferencesToolOptions {
        AnsightPreferencesToolOptions(
            defaultStore: stringValue(dictionary, "defaultStore"),
            allowedStores: stringArray(dictionary, "allowedStores"),
            allowedKeys: stringArray(dictionary, "allowedKeys"),
            allowedKeyPrefixes: stringArray(dictionary, "allowedKeyPrefixes")
        )
    }

    private func reflectionToolsOptions(_ dictionary: NSDictionary?) -> AnsightReflectionToolsOptions {
        AnsightReflectionToolsOptions(
            includeBuiltInRoots: boolValue(dictionary, "includeBuiltInRoots", defaultValue: true),
            allowedRootIds: stringArray(dictionary, "allowedRootIds"),
            allowedTypePrefixes: stringArray(dictionary, "allowedTypePrefixes")
        )
    }

    private func secureStorageToolsOptions(_ dictionary: NSDictionary?) -> AnsightSecureStorageToolsOptions {
        AnsightSecureStorageToolsOptions(
            appleService: stringValue(dictionary, "appleService"),
            allowedKeys: stringArray(dictionary, "allowedKeys"),
            allowedKeyPrefixes: stringArray(dictionary, "allowedKeyPrefixes") + stringArray(dictionary, "allowedPrefixes")
        )
    }

    private func toolDescriptor(_ dictionary: NSDictionary) throws -> AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: stringValue(dictionary, "id") ?? "",
            name: stringValue(dictionary, "name") ?? stringValue(dictionary, "id") ?? "",
            description: stringValue(dictionary, "description") ?? "",
            category: stringValue(dictionary, "category") ?? "custom",
            scope: toolScope(stringValue(dictionary, "scope")).rawValue,
            keywords: keywords(dictionary["keywords"]),
            security: toolSecurity(dictionary["security"] as? NSDictionary),
            argumentsSchema: AnsightToolSchema(json: jsonValue(dictionary["argumentsSchema"]) ?? .object([:])),
            resultSchema: AnsightToolSchema(json: jsonValue(dictionary["resultSchema"]) ?? .object([:]))
        )
    }

    private func snapshotDictionary() -> NSDictionary {
        let snapshot = AnsightRuntime.shared.snapshot()
        var result: [String: Any] = [
            "initialized": snapshot.initialized,
            "active": snapshot.active,
            "sessionOpen": snapshot.sessionOpen,
            "lifecycleState": snapshot.lifecycleState.rawValue,
            "metricsRecorded": snapshot.metricsRecorded,
            "eventsRecorded": snapshot.eventsRecorded,
            "executableTools": snapshot.executableTools,
            "toolDiscoveryEnabled": snapshot.toolDiscoveryEnabled,
            "toolExecutionEnabled": snapshot.toolExecutionEnabled,
            "embeddedDeveloperPairingAvailable": snapshot.embeddedDeveloperPairingAvailable,
            "detectedBundledTools": snapshot.detectedBundledTools,
            "touchesRecorded": snapshot.touchesCaptured,
            "touchesCaptured": snapshot.touchesCaptured,
            "touchesSent": snapshot.touchesSent,
            "touchCaptureEnabled": snapshot.touchCaptureEnabled,
            "touchCaptureActive": snapshot.touchCaptureActive,
            "touchCaptureStreamingActive": snapshot.touchCaptureStreamingActive,
            "screenCaptureActive": snapshot.screenCaptureActive,
            "screenFramesCaptured": snapshot.screenFramesCaptured,
            "screenFramesSent": snapshot.screenFramesSent,
            "frameRateCaptureActive": snapshot.frameRateCaptureActive,
            "registeredTools": snapshot.registeredTools,
            "connectionStatus": hostConnectionStatusDictionary(snapshot.hostConnectionStatus),
            "channels": snapshot.channels.map(channelDictionary),
        ]
        if let metric = snapshot.lastMetric {
            result["lastMetric"] = metricDictionary(metric)
        }
        if let event = snapshot.lastEvent {
            result["lastEvent"] = eventDictionary(event)
        }
        if let message = snapshot.sessionMessage {
            result["sessionMessage"] = message
        }
        if let pairingConfigId = snapshot.lastPairingConfigId {
            result["lastPairingConfigId"] = pairingConfigId
        }
        if let hostAddress = snapshot.resolvedHostAddress {
            result["resolvedHostAddress"] = hostAddress
        }
        if let message = snapshot.lastScreenCaptureMessage {
            result["lastScreenCaptureMessage"] = message
        }
        if let frameRate = snapshot.lastFrameRate {
            result["lastFrameRate"] = frameRate
        }
        if let message = snapshot.lastTouchCaptureMessage {
            result["lastTouchCaptureMessage"] = message
        }
        if let screen = snapshot.currentScreen {
            result["currentScreen"] = [
                "name": screen.name,
                "capturedAtUtc": screen.capturedAtUtc,
                "details": screen.details,
            ]
        }
        return result as NSDictionary
    }

    private func hostConnectionStatusDictionary(_ status: HostConnectionStatus) -> NSDictionary {
        [
            "isRuntimeActive": status.isRuntimeActive,
            "isConnected": status.isConnected,
            "connectionState": status.connectionState.rawValue,
            "hasCachedSession": status.hasCachedSession,
            "hasSavedConfig": status.hasSavedConfig,
            "hasBundledConfig": status.hasBundledConfig,
            "summaryKind": status.summaryKind.rawValue,
            "summaryMessage": status.summaryMessage,
        ] as NSDictionary
    }

    private func hostConnectionCapabilitiesDictionary(_ capabilities: HostConnectionCapabilities) -> NSDictionary {
        [
            "canConnectUsingSavedConfig": capabilities.canConnectUsingSavedConfig,
            "canConnectUsingBundledConfig": capabilities.canConnectUsingBundledConfig,
            "canChooseConfigFile": capabilities.canChooseConfigFile,
            "canScanConfigQrCode": capabilities.canScanConfigQrCode,
            "canClearSavedConfigs": capabilities.canClearSavedConfigs,
        ] as NSDictionary
    }

    private func hostConnectionResultDictionary(_ result: HostConnectionResult) -> NSDictionary {
        var dictionary: [String: Any] = [
            "success": result.success,
            "message": result.message,
            "kind": result.kind.rawValue,
            "source": result.source.rawValue,
        ]
        if let reasonCode = result.reasonCode ?? result.openSession?.reasonCode {
            dictionary["reasonCode"] = reasonCode
        }
        if let session = result.openSession {
            dictionary["accepted"] = session.accepted
            dictionary["usedEmbeddedDeveloperPairing"] = session.usedEmbeddedDeveloperPairing
            if let value = session.sessionId {
                dictionary["sessionId"] = value
            }
            if let value = session.configId {
                dictionary["configId"] = value
            }
            if let value = session.appId {
                dictionary["appId"] = value
            }
            if let value = session.resolvedHostAddress {
                dictionary["resolvedHostAddress"] = value
            }
            if let value = session.discoverySource {
                dictionary["discoverySource"] = value
            }
            if let value = session.hostId {
                dictionary["hostId"] = value
            }
            if let value = session.hostName {
                dictionary["hostName"] = value
            }
        }
        return dictionary as NSDictionary
    }

    private func openSessionResultDictionary(_ result: OpenSessionResult) -> NSDictionary {
        var dictionary: [String: Any] = [
            "success": result.success,
            "message": result.message,
            "accepted": result.accepted,
            "usedEmbeddedDeveloperPairing": result.usedEmbeddedDeveloperPairing,
        ]
        if let value = result.sessionId {
            dictionary["sessionId"] = value
        }
        if let value = result.configId {
            dictionary["configId"] = value
        }
        if let value = result.appId {
            dictionary["appId"] = value
        }
        if let value = result.resolvedHostAddress {
            dictionary["resolvedHostAddress"] = value
        }
        if let value = result.discoverySource {
            dictionary["discoverySource"] = value
        }
        if let value = result.reasonCode {
            dictionary["reasonCode"] = value
        }
        if let value = result.hostId {
            dictionary["hostId"] = value
        }
        if let value = result.hostName {
            dictionary["hostName"] = value
        }
        return dictionary as NSDictionary
    }

    private func operationResultDictionary(_ result: OperationResult) -> NSDictionary {
        [
            "success": result.success,
            "message": result.message,
        ] as NSDictionary
    }

    private func screenCaptureOptions(_ dictionary: NSDictionary?) -> AnsightSessionJpegCaptureOptions? {
        guard let dictionary else {
            return nil
        }
        return AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: intValue(dictionary, "intervalMilliseconds", defaultValue: 1_000),
            quality: intValue(dictionary, "quality", defaultValue: 80),
            maxWidth: optionalInt(dictionary, "maxWidth")
        )
    }

    private func optionsDictionary(_ options: AnsightOptions) -> NSDictionary {
        var dictionary: [String: Any] = [
            "sampleFrequencyMilliseconds": options.sampleFrequencyMilliseconds,
            "retentionPeriodSeconds": options.retentionPeriodSeconds,
            "enableFramesPerSecond": options.enableFramesPerSecond,
            "enableBatteryLevel": options.enableBatteryLevel,
            "defaultMemoryChannels": [
                "managedHeap": options.defaultMemoryChannels.contains(.managedHeap),
                "javaHeap": options.defaultMemoryChannels.contains(.managedHeap),
                "nativeHeap": options.defaultMemoryChannels.contains(.nativeHeap),
                "residentSetSize": options.defaultMemoryChannels.contains(.residentSetSize),
                "rss": options.defaultMemoryChannels.contains(.residentSetSize),
                "physicalFootprint": options.defaultMemoryChannels.contains(.physicalFootprint),
            ],
            "reactNativeMemory": currentReactNativeMemoryOptions.dictionary,
            "additionalChannels": options.additionalChannels.map(channelDictionary),
            "toolGuard": toolGuardName(options.toolGuard),
            "customProperties": options.customProperties,
            "lifecycleCapture": [
                "enabled": options.lifecycleCapture.enabled,
                "captureAppLifecycle": options.lifecycleCapture.captureAppLifecycle,
                "captureScreenViews": options.lifecycleCapture.captureScreenViews,
                "minimumScreenViewIntervalMilliseconds": options.lifecycleCapture.minimumScreenViewIntervalMilliseconds,
            ],
            "hostAutoProbe": [
                "enabled": options.hostAutoProbe.enabled,
                "initialDelayMilliseconds": options.hostAutoProbe.initialDelayMilliseconds,
                "probeIntervalMilliseconds": options.hostAutoProbe.probeIntervalMilliseconds,
                "reconnectDelayMilliseconds": options.hostAutoProbe.reconnectDelayMilliseconds,
                "clientName": options.hostAutoProbe.clientName as Any,
            ],
            "hostConnection": [
                "savedConfigKey": options.hostConnection.savedConfigKey,
                "connectionProfileRetentionSeconds": options.hostConnection.connectionProfileRetentionSeconds,
                "discoveryPort": options.hostConnection.discoveryPort as Any,
                "hasBundledDeveloperConfigJson": options.hostConnection.bundledDeveloperConfigJson != nil,
                "hasBundledConfigJson": options.hostConnection.bundledConfigJson != nil,
            ],
        ]
        if let capture = options.sessionJpegCapture {
            dictionary["sessionJpegCapture"] = [
                "intervalMilliseconds": capture.intervalMilliseconds,
                "quality": capture.quality,
                "maxWidth": capture.maxWidth as Any,
            ]
        } else {
            dictionary["sessionJpegCapture"] = NSNull()
        }
        if let touch = options.touchCapture {
            dictionary["touchCapture"] = [
                "captureMoveEvents": touch.captureMoveEvents,
                "captureCancelEvents": touch.captureCancelEvents,
                "moveCaptureDistanceThreshold": touch.moveCaptureDistanceThreshold,
                "moveCaptureFramesPerSecond": touch.moveCaptureFramesPerSecond,
            ]
        } else {
            dictionary["touchCapture"] = NSNull()
        }
        return dictionary as NSDictionary
    }

    private func pairingOpenOptions(_ dictionary: NSDictionary?) -> PairingOpenOptions {
        PairingOpenOptions(
            clientName: stringValue(dictionary, "clientName") ?? "React Native",
            expectedAppId: stringValue(dictionary, "expectedAppId"),
            hostAddressOverride: stringValue(dictionary, "hostAddressOverride"),
            discoveryPort: optionalInt(dictionary, "discoveryPort")
        )
    }

    private func channelDictionary(_ channel: AnsightChannel) -> [String: Any] {
        var dictionary: [String: Any] = [
            "id": channel.id,
            "name": channel.name,
            "type": channel.type,
        ]
        if let unit = channel.unit {
            dictionary["unit"] = unit
        }
        if let color = channel.colorHex {
            dictionary["colorHex"] = color
        }
        if let source = channel.source {
            dictionary["source"] = source
        }
        if let group = channel.group {
            dictionary["group"] = group
        }
        if let kind = channel.kind {
            dictionary["kind"] = kind
        }
        return dictionary
    }

    private func metricDictionary(_ metric: RecordedMetric) -> [String: Any] {
        [
            "value": metric.value,
            "capturedAtUtc": metric.capturedAtUtc,
            "capturedAtEpochMs": metric.capturedAtEpochMs,
            "channel": metric.channel,
            "sequence": metric.sequence,
        ]
    }

    private func eventDictionary(_ event: RecordedEvent) -> [String: Any] {
        var dictionary: [String: Any] = [
            "id": event.id,
            "label": event.label,
            "type": eventTypeName(event.type),
            "capturedAtUtc": event.capturedAtUtc,
            "capturedAtEpochMs": event.capturedAtEpochMs,
            "channel": event.channel,
            "sequence": event.sequence,
        ]
        if let details = event.details {
            dictionary["details"] = details
        }
        if let externalId = event.externalId {
            dictionary["externalId"] = externalId
        }
        return dictionary
    }

    private func eventTypeName(_ type: AnsightEventType) -> String {
        switch type {
        case .event:
            return "Event"
        case .debug:
            return "Debug"
        case .info:
            return "Info"
        case .warning:
            return "Warning"
        case .error:
            return "Error"
        case .exception:
            return "Exception"
        case .gc:
            return "Gc"
        case .navigation:
            return "Navigation"
        case .screenViewed:
            return "ScreenViewed"
        case .lifecycle:
            return "Lifecycle"
        }
    }

    private func toolGuardName(_ guardPolicy: AnsightToolGuard) -> String {
        if guardPolicy == .disabled {
            return "disabled"
        }
        if guardPolicy == .readOnly {
            return "readOnly"
        }
        if guardPolicy == .readWrite {
            return "readWrite"
        }
        if guardPolicy == .fullAccess {
            return "fullAccess"
        }
        return "custom"
    }
}

private extension NSLock {
    func withLock<T>(_ body: () -> T) -> T {
        lock()
        defer { unlock() }
        return body()
    }
}

private func stringValue(_ dictionary: NSDictionary?, _ key: String) -> String? {
    guard let value = dictionary?[key], !(value is NSNull) else {
        return nil
    }
    if let string = value as? String {
        let trimmed = string.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
    return "\(value)"
}

private func hasNumber(_ dictionary: NSDictionary?, _ key: String) -> Bool {
    dictionary?[key] is NSNumber
}

private func hasBool(_ dictionary: NSDictionary?, _ key: String) -> Bool {
    guard let number = dictionary?[key] as? NSNumber else {
        return false
    }
    return CFGetTypeID(number) == CFBooleanGetTypeID()
}

private func boolValue(_ dictionary: NSDictionary?, _ key: String, defaultValue: Bool) -> Bool {
    dictionary?[key] as? Bool ?? defaultValue
}

private func intValue(_ dictionary: NSDictionary?, _ key: String, defaultValue: Int) -> Int {
    (dictionary?[key] as? NSNumber)?.intValue ?? defaultValue
}

private func optionalInt(_ dictionary: NSDictionary?, _ key: String) -> Int? {
    (dictionary?[key] as? NSNumber)?.intValue
}

private func doubleValue(_ dictionary: NSDictionary?, _ key: String, defaultValue: Double) -> Double {
    (dictionary?[key] as? NSNumber)?.doubleValue ?? defaultValue
}

private func stringDictionary(_ dictionary: NSDictionary?) -> [String: String] {
    guard let dictionary else {
        return [:]
    }
    var result: [String: String] = [:]
    for (key, value) in dictionary {
        guard let key = key as? String, !(value is NSNull) else {
            continue
        }
        result[key] = value as? String ?? "\(value)"
    }
    return result
}

private func stringArray(_ dictionary: NSDictionary?, _ key: String) -> [String] {
    guard let array = dictionary?[key] as? [Any] else {
        return []
    }
    return array.compactMap { value in
        if value is NSNull {
            return nil
        }
        let normalized = (value as? String ?? "\(value)").trimmingCharacters(in: .whitespacesAndNewlines)
        return normalized.isEmpty ? nil : normalized
    }
}

private func rootDictionaries(_ value: Any?) -> [NSDictionary] {
    guard let array = value as? [Any] else {
        return []
    }
    return array.compactMap { $0 as? NSDictionary }
}

private func groupedStringDictionary(_ dictionary: NSDictionary?) -> [String: [String: String]] {
    guard let dictionary else {
        return [:]
    }
    var result: [String: [String: String]] = [:]
    for (key, value) in dictionary {
        guard let key = key as? String, let group = value as? NSDictionary else {
            continue
        }
        result[key] = stringDictionary(group)
    }
    return result
}

private func eventType(_ rawValue: String?) -> AnsightEventType {
    switch rawValue?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "event":
        return .event
    case "debug":
        return .debug
    case "warning", "warn":
        return .warning
    case "error":
        return .error
    case "exception":
        return .exception
    case "gc":
        return .gc
    case "navigation":
        return .navigation
    case "screenviewed", "screen_viewed":
        return .screenViewed
    case "lifecycle":
        return .lifecycle
    default:
        return .info
    }
}

private func lifecycleState(_ rawValue: String) -> AppLifecycleState {
    switch rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "foreground", "active":
        return .foreground
    case "background", "inactive":
        return .background
    default:
        return .unknown
    }
}

private func toolGuard(_ rawValue: String) -> AnsightToolGuard {
    switch rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "readonly", "read_only", "read":
        return .readOnly
    case "readwrite", "read_write", "write":
        return .readWrite
    case "full", "fullaccess", "full_access":
        return .fullAccess
    default:
        return .disabled
    }
}

private func toolScope(_ rawValue: String?) -> AnsightToolScope {
    switch rawValue?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "write":
        return .write
    case "delete":
        return .delete
    default:
        return .read
    }
}

private func toolSecurity(_ dictionary: NSDictionary?) -> AnsightToolSecurity {
    guard let dictionary else {
        return .unspecified
    }
    let level: AnsightToolSecurityLevel
    switch stringValue(dictionary, "level")?.lowercased() {
    case "low":
        level = .low
    case "medium", "moderate":
        level = .moderate
    case "high":
        level = .high
    case "critical":
        level = .critical
    default:
        level = .unspecified
    }
    return AnsightToolSecurity(
        level: level,
        summary: stringValue(dictionary, "summary") ?? "",
        implications: (dictionary["implications"] as? [Any])?.compactMap { $0 as? String } ?? []
    )
}

private func keywords(_ value: Any?) -> String {
    if let string = value as? String {
        return string
    }
    if let array = value as? [Any] {
        return array.compactMap { $0 as? String }.joined(separator: " ")
    }
    return "react native custom tool"
}

private func jsonValue(_ value: Any?) -> JSONValue? {
    guard let value, !(value is NSNull) else {
        return nil
    }
    if let dictionary = value as? NSDictionary {
        var object: [String: JSONValue] = [:]
        for (key, value) in dictionary {
            guard let key = key as? String, let converted = jsonValue(value) else {
                continue
            }
            object[key] = converted
        }
        return .object(object)
    }
    if let array = value as? [Any] {
        return .array(array.map { jsonValue($0) ?? .null })
    }
    if let string = value as? String {
        return .string(string)
    }
    if let number = value as? NSNumber {
        if CFGetTypeID(number) == CFBooleanGetTypeID() {
            return .bool(number.boolValue)
        }
        let double = number.doubleValue
        if double.rounded() == double {
            return .integer(number.int64Value)
        }
        return .number(double)
    }
    if let bool = value as? Bool {
        return .bool(bool)
    }
    return .string("\(value)")
}
