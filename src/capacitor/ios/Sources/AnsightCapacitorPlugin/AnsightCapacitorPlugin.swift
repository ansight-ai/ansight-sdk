import Ansight
import Capacitor
import Foundation

@objc(AnsightCapacitorPlugin)
public final class AnsightCapacitorPlugin: CAPPlugin, CAPBridgedPlugin {
    public let identifier = "AnsightCapacitorPlugin"
    public let jsName = "Ansight"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "initialize", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "initializeAndActivate", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "activate", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "deactivate", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "clear", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "registerMetricChannel", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "recordMetric", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "recordEvent", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "recordCrashCandidate", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "screenViewed", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "setAppLifecycleState", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "connect", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "scanPairingQrCode", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "openSession", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "disconnect", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "completeSession", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "closeSession", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "savePairingConfig", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "clearSavedPairing", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "clearCachedSession", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "notifyHostConnectionConfigChanged", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "status", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "snapshot", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "hostConnectionStatus", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "hostConnectionCapabilities", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "currentOptions", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "recordedMetrics", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "recordedEvents", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "sendClientLog", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "sendSessionEvent", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "captureBuiltInTelemetrySample", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "isFramesPerSecondEnabled", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "enableFramesPerSecond", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "disableFramesPerSecond", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "captureScreenFrame", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "enableTouchCapture", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "disableTouchCapture", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "updateSessionProperties", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "clearSessionProperties", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "registerCustomProperty", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "removeCustomProperty", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "registerCustomTool", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "unregisterCustomTool", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "clearRegisteredCustomTools", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "resolveToolCall", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "queueBinaryTransfer", returnType: CAPPluginReturnPromise),
    ]

    private final class PendingToolCall {
        let semaphore = DispatchSemaphore(value: 0)
        var result: AnsightToolExecutionResult?
    }

    private final class CapacitorTool: AnsightTool, @unchecked Sendable {
        let descriptor: AnsightToolDescriptor
        private weak var plugin: AnsightCapacitorPlugin?
        private let timeoutMilliseconds: Int

        init(descriptor: AnsightToolDescriptor, plugin: AnsightCapacitorPlugin, timeoutMilliseconds: Int) {
            self.descriptor = descriptor
            self.plugin = plugin
            self.timeoutMilliseconds = timeoutMilliseconds
        }

        func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
            guard let plugin else {
                return .failure("Capacitor bridge is unavailable.", errorCode: "javascript_bridge_unavailable")
            }
            return plugin.executeJavaScriptTool(
                toolId: descriptor.id,
                arguments: arguments,
                timeoutMilliseconds: timeoutMilliseconds
            )
        }
    }

    private let lock = NSLock()
    private var activeCustomToolIds: Set<String> = []
    private var pendingToolCalls: [String: PendingToolCall] = [:]
    private lazy var logCallback = AnsightClosureLogCallback { [weak self] level, message, error in
        var data: [String: Any] = [
            "level": level.rawValue,
            "message": message,
            "platform": "ios",
        ]
        if let error {
            data["error"] = error.localizedDescription
        }
        self?.notifyListeners("ansightLog", data: data)
    }

    public override func load() {
        AnsightLogger.registerCallback(logCallback)
    }

    deinit {
        AnsightLogger.removeCallback(logCallback)
    }

    @objc func initialize(_ call: CAPPluginCall) {
        do {
            let options = dictionary(call)
            try AnsightRuntime.shared.initialize(options: buildOptions(options))
            try AnsightRuntime.shared.registerAnsightRemoteTools(options: remoteToolOptions(options))
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func initializeAndActivate(_ call: CAPPluginCall) {
        do {
            let options = dictionary(call)
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk(
                options: buildOptions(options),
                remoteToolOptions: remoteToolOptions(options)
            )
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func activate(_ call: CAPPluginCall) {
        do {
            try AnsightRuntime.shared.activate()
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func deactivate(_ call: CAPPluginCall) {
        AnsightRuntime.shared.deactivate()
        call.resolve(snapshotDictionary())
    }

    @objc func clear(_ call: CAPPluginCall) {
        AnsightRuntime.shared.clear()
        call.resolve(snapshotDictionary())
    }

    @objc func registerMetricChannel(_ call: CAPPluginCall) {
        let channel = nestedDictionary(call, "channel")
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
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func recordMetric(_ call: CAPPluginCall) {
        do {
            try AnsightRuntime.shared.metric(
                Int64(call.getDouble("value") ?? 0),
                channel: call.getInt("channel") ?? AnsightChannels.unspecified
            )
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func recordEvent(_ call: CAPPluginCall) {
        do {
            try AnsightRuntime.shared.event(
                call.getString("label") ?? "",
                type: eventType(call.getString("type")),
                details: call.getString("details"),
                channel: call.getInt("channel") ?? AnsightChannels.unspecified
            )
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func recordCrashCandidate(_ call: CAPPluginCall) {
        let metadata: [String: String]
        if let metadataJson = call.getString("metadata"),
           let data = metadataJson.data(using: .utf8),
           let decoded = try? JSONDecoder().decode([String: String].self, from: data) {
            metadata = decoded
        } else {
            metadata = [:]
        }
        let candidateId = AnsightRuntime.shared.recordCrashCandidate(
            runtime: call.getString("runtime") ?? "capacitor-javascript",
            kind: call.getString("kind") ?? "unhandled_javascript_error",
            message: call.getString("message"),
            stack: call.getString("stack"),
            fatal: call.getBool("fatal") ?? false,
            metadata: metadata
        )
        call.resolve(candidateId.map { ["candidateId": $0] } ?? [:])
    }

    @objc func screenViewed(_ call: CAPPluginCall) {
        do {
            try AnsightRuntime.shared.screenViewed(
                call.getString("name") ?? "",
                details: stringDictionary(nestedDictionary(call, "details"))
            )
            call.resolve(snapshotDictionary())
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func setAppLifecycleState(_ call: CAPPluginCall) {
        AnsightRuntime.shared.setAppLifecycleState(lifecycleState(call.getString("state") ?? "unknown"))
        call.resolve(snapshotDictionary())
    }

    @objc func connect(_ call: CAPPluginCall) {
        Task {
            let payload = call.getString("pairingPayload")
            let request: HostConnectionRequest
            if let payload, !payload.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                request = .payloadText(
                    payload,
                    clientName: call.getString("clientName"),
                    expectedAppId: call.getString("expectedAppId"),
                    hostAddressOverride: call.getString("hostAddressOverride"),
                    sourceDescription: "Capacitor"
                )
            } else {
                request = .auto(
                    clientName: call.getString("clientName"),
                    expectedAppId: call.getString("expectedAppId"),
                    hostAddressOverride: call.getString("hostAddressOverride"),
                    sourceDescription: "Capacitor"
                )
            }
            call.resolve(hostConnectionResultDictionary(await AnsightRuntime.shared.connect(request)))
        }
    }

    @objc func scanPairingQrCode(_ call: CAPPluginCall) {
        Task {
            let request = HostConnectionRequest.qrCode(
                title: call.getString("title") ?? "Scan Ansight Enrollment QR",
                clientName: call.getString("clientName"),
                expectedAppId: call.getString("expectedAppId"),
                hostAddressOverride: call.getString("hostAddressOverride"),
                sourceDescription: "Capacitor native QR scanner"
            )
            call.resolve(hostConnectionResultDictionary(await AnsightRuntime.shared.connect(request)))
        }
    }

    @objc func openSession(_ call: CAPPluginCall) {
        Task {
            do {
                let result = try await AnsightRuntime.shared.openLiveSession(
                    pairingJson: call.getString("pairingPayload") ?? "",
                    options: pairingOpenOptions(dictionary(call))
                )
                call.resolve(openSessionResultDictionary(result))
            } catch {
                call.reject(error.localizedDescription, "ansight_error", error)
            }
        }
    }

    @objc func disconnect(_ call: CAPPluginCall) {
        Task {
            call.resolve(hostConnectionResultDictionary(await AnsightRuntime.shared.disconnect()))
        }
    }

    @objc func completeSession(_ call: CAPPluginCall) {
        Task {
            call.resolve(operationResultDictionary(await AnsightRuntime.shared.completeLiveSession()))
        }
    }

    @objc func closeSession(_ call: CAPPluginCall) {
        AnsightRuntime.shared.closeSession()
        call.resolve(["success": true, "message": "Session closed."])
    }

    @objc func savePairingConfig(_ call: CAPPluginCall) {
        call.resolve(hostConnectionResultDictionary(
            AnsightRuntime.shared.savePairingConfig(
                call.getString("pairingPayload") ?? "",
                expectedAppId: call.getString("expectedAppId")
            )
        ))
    }

    @objc func clearSavedPairing(_ call: CAPPluginCall) {
        AnsightRuntime.shared.clearSavedPairing()
        call.resolve([
            "success": true,
            "message": "Saved pairing config cleared.",
            "kind": "savedConfig",
            "source": "savedConfig",
        ])
    }

    @objc func clearCachedSession(_ call: CAPPluginCall) {
        AnsightRuntime.shared.clearCachedSession()
        call.resolve(["success": true, "message": "Cached live session cleared."])
    }

    @objc func notifyHostConnectionConfigChanged(_ call: CAPPluginCall) {
        call.resolve(hostConnectionResultDictionary(AnsightRuntime.shared.notifyHostConnectionConfigChanged()))
    }

    @objc func status(_ call: CAPPluginCall) {
        call.resolve(snapshotDictionary())
    }

    @objc func snapshot(_ call: CAPPluginCall) {
        call.resolve(snapshotDictionary())
    }

    @objc func hostConnectionStatus(_ call: CAPPluginCall) {
        call.resolve(hostConnectionStatusDictionary(AnsightRuntime.shared.hostConnectionStatus()))
    }

    @objc func hostConnectionCapabilities(_ call: CAPPluginCall) {
        call.resolve(hostConnectionCapabilitiesDictionary(AnsightRuntime.shared.hostConnectionCapabilities()))
    }

    @objc func currentOptions(_ call: CAPPluginCall) {
        call.resolve(optionsDictionary(AnsightRuntime.shared.currentOptions()))
    }

    @objc func recordedMetrics(_ call: CAPPluginCall) {
        let metrics = AnsightRuntime.shared.recordedMetrics()
        let limit = max(0, call.getInt("limit") ?? 0)
        call.resolve(["items": (limit > 0 ? Array(metrics.suffix(limit)) : metrics).map(metricDictionary)])
    }

    @objc func recordedEvents(_ call: CAPPluginCall) {
        let events = AnsightRuntime.shared.recordedEvents()
        let limit = max(0, call.getInt("limit") ?? 0)
        call.resolve(["items": (limit > 0 ? Array(events.suffix(limit)) : events).map(eventDictionary)])
    }

    @objc func sendClientLog(_ call: CAPPluginCall) {
        Task {
            call.resolve(operationResultDictionary(
                await AnsightRuntime.shared.sendClientLog(call.getString("line") ?? "")
            ))
        }
    }

    @objc func sendSessionEvent(_ call: CAPPluginCall) {
        Task {
            guard case .object(let payload) = jsonValue(call.getObject("payload")) else {
                call.resolve(operationResultDictionary(.failure("Session event payload must be an object.")))
                return
            }
            call.resolve(operationResultDictionary(
                await AnsightRuntime.shared.sendSessionEvent(
                    type: call.getString("type") ?? "",
                    payload: payload
                )
            ))
        }
    }

    @objc func captureBuiltInTelemetrySample(_ call: CAPPluginCall) {
        AnsightRuntime.shared.captureBuiltInTelemetrySample()
        call.resolve(snapshotDictionary())
    }

    @objc func isFramesPerSecondEnabled(_ call: CAPPluginCall) {
        call.resolve(["value": AnsightRuntime.shared.isFramesPerSecondEnabled])
    }

    @objc func enableFramesPerSecond(_ call: CAPPluginCall) {
        AnsightRuntime.shared.enableFramesPerSecond()
        call.resolve(snapshotDictionary())
    }

    @objc func disableFramesPerSecond(_ call: CAPPluginCall) {
        AnsightRuntime.shared.disableFramesPerSecond()
        call.resolve(snapshotDictionary())
    }

    @objc func captureScreenFrame(_ call: CAPPluginCall) {
        Task {
            call.resolve(operationResultDictionary(
                await AnsightRuntime.shared.captureScreenFrame(options: screenCaptureOptions(dictionary(call)))
            ))
        }
    }

    @objc func enableTouchCapture(_ call: CAPPluginCall) {
        AnsightRuntime.shared.enableTouchCapture()
        call.resolve(snapshotDictionary())
    }

    @objc func disableTouchCapture(_ call: CAPPluginCall) {
        AnsightRuntime.shared.disableTouchCapture()
        call.resolve(snapshotDictionary())
    }

    @objc func updateSessionProperties(_ call: CAPPluginCall) {
        Task {
            call.resolve(operationResultDictionary(
                await AnsightRuntime.shared.updateSessionProperties(
                    groupedStringDictionary(nestedDictionary(call, "properties"))
                )
            ))
        }
    }

    @objc func clearSessionProperties(_ call: CAPPluginCall) {
        Task {
            call.resolve(operationResultDictionary(await AnsightRuntime.shared.clearSessionProperties()))
        }
    }

    @objc func registerCustomProperty(_ call: CAPPluginCall) {
        Task {
            let group = (call.getString("group") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            let key = (call.getString("key") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !group.isEmpty, !key.isEmpty else {
                call.resolve(["success": false, "message": "Custom property group and key must not be blank."])
                return
            }
            var properties = AnsightRuntime.shared.currentOptions().customProperties
            var groupProperties = properties[group] ?? [:]
            groupProperties[key] = call.getString("value") ?? ""
            properties[group] = groupProperties
            call.resolve(operationResultDictionary(await AnsightRuntime.shared.updateSessionProperties(properties)))
        }
    }

    @objc func removeCustomProperty(_ call: CAPPluginCall) {
        Task {
            let group = (call.getString("group") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            let key = (call.getString("key") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            var properties = AnsightRuntime.shared.currentOptions().customProperties
            properties[group]?.removeValue(forKey: key)
            if properties[group]?.isEmpty == true {
                properties.removeValue(forKey: group)
            }
            call.resolve(operationResultDictionary(await AnsightRuntime.shared.updateSessionProperties(properties)))
        }
    }

    @objc func registerCustomTool(_ call: CAPPluginCall) {
        do {
            let definition = nestedDictionary(call, "definition")
            let descriptor = try toolDescriptor(definition)
            let timeout = max(250, intValue(definition, "timeoutMilliseconds", defaultValue: 30_000))
            _ = lock.withLock { activeCustomToolIds.insert(descriptor.id) }
            try AnsightRuntime.shared.registerTool(
                CapacitorTool(descriptor: descriptor, plugin: self, timeoutMilliseconds: timeout),
                replaceExisting: true
            )
            call.resolve(["success": true, "message": "Tool registered.", "id": descriptor.id])
        } catch {
            call.reject(error.localizedDescription, "ansight_error", error)
        }
    }

    @objc func unregisterCustomTool(_ call: CAPPluginCall) {
        let id = call.getString("id") ?? ""
        _ = lock.withLock { activeCustomToolIds.remove(id) }
        call.resolve(["success": true, "message": "Tool unregistered.", "id": id])
    }

    @objc func clearRegisteredCustomTools(_ call: CAPPluginCall) {
        lock.withLock { activeCustomToolIds.removeAll() }
        call.resolve(["success": true, "message": "JavaScript tools cleared."])
    }

    @objc func resolveToolCall(_ call: CAPPluginCall) {
        let requestId = call.getString("requestId") ?? ""
        guard let pending = lock.withLock({ pendingToolCalls[requestId] }) else {
            call.resolve(["success": false, "message": "Tool request is no longer pending.", "accepted": false])
            return
        }
        let result = nestedDictionary(call, "result")
        let success = boolValue(result, "success", defaultValue: true)
        let payload = jsonValue(result?["result"])
        pending.result = success
            ? .success(payload, message: stringValue(result, "message"))
            : .failure(
                stringValue(result, "message") ?? "JavaScript tool failed.",
                errorCode: stringValue(result, "errorCode"),
                result: payload
            )
        pending.semaphore.signal()
        call.resolve(["success": true, "message": "Tool result accepted.", "accepted": true])
    }

    @objc func queueBinaryTransfer(_ call: CAPPluginCall) {
        let requestId = (call.getString("requestId") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !requestId.isEmpty,
              let data = Data(base64Encoded: call.getString("base64Data") ?? "") else {
            call.resolve([
                "success": false,
                "message": "Binary transfer requires a request id and base64 payload.",
                "errorCode": "artifact_payload_invalid",
            ])
            return
        }
        let transferId = UUID()
        let chunkBytes = min(max(call.getInt("chunkBytes") ?? 65_536, 1_024), 512 * 1_024)
        let result = AnsightRuntime.shared.queueBinaryTransfer(
            requestId: requestId,
            transferId: transferId,
            data: data,
            chunkBytes: chunkBytes,
            description: "capacitor-artifact:\(transferId.uuidString.lowercased())"
        )
        call.resolve([
            "success": result.success,
            "message": result.message,
            "transferId": transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
            "deliveryMode": "websocket_binary",
            "wireProtocol": PairingFileTransferWireProtocol.protocolName,
            "status": result.success ? "queued" : "failed",
            "chunkBytes": chunkBytes,
            "sizeBytes": data.count,
        ])
    }

    fileprivate func executeJavaScriptTool(
        toolId: String,
        arguments: [String: String],
        timeoutMilliseconds: Int
    ) -> AnsightToolExecutionResult {
        let requestId = "ios.capacitor.\(UUID().uuidString.replacingOccurrences(of: "-", with: ""))"
        let pending = PendingToolCall()
        let active = lock.withLock { () -> Bool in
            guard activeCustomToolIds.contains(toolId) else { return false }
            pendingToolCalls[requestId] = pending
            return true
        }
        guard active else {
            return .failure("JavaScript tool is not registered.", errorCode: "javascript_tool_not_registered")
        }
        notifyListeners("ansightToolCall", data: [
            "requestId": requestId,
            "nativeRequestId": requestId,
            "toolId": toolId,
            "arguments": arguments,
            "platform": "ios",
        ])
        let wait = pending.semaphore.wait(timeout: .now() + .milliseconds(timeoutMilliseconds))
        _ = lock.withLock { pendingToolCalls.removeValue(forKey: requestId) }
        if wait == .timedOut {
            return .failure("JavaScript tool timed out.", errorCode: "javascript_tool_timeout")
        }
        return pending.result ?? .failure(
            "JavaScript tool completed without a result.",
            errorCode: "javascript_tool_result_missing"
        )
    }

    private func buildOptions(_ dictionary: NSDictionary?) throws -> AnsightOptions {
        let useDefaults = boolValue(dictionary, "useNativeAllInOneDefaults", defaultValue: false)
        var options = useDefaults ? AnsightOptions.ansightDeveloperDefaults : AnsightOptions()
        if let value = stringValue(dictionary, "clientName") {
            options.hostAutoProbe.clientName = value
        }
        if hasNumber(dictionary, "sampleFrequencyMilliseconds") {
            options.sampleFrequencyMilliseconds = intValue(
                dictionary, "sampleFrequencyMilliseconds", defaultValue: options.sampleFrequencyMilliseconds
            )
        }
        if hasNumber(dictionary, "retentionPeriodSeconds") {
            options.retentionPeriodSeconds = intValue(
                dictionary, "retentionPeriodSeconds", defaultValue: options.retentionPeriodSeconds
            )
        }
        if hasBool(dictionary, "enableFramesPerSecond") {
            options.enableFramesPerSecond = boolValue(
                dictionary, "enableFramesPerSecond", defaultValue: options.enableFramesPerSecond
            )
        }
        if hasBool(dictionary, "enableBatteryLevel") {
            options.enableBatteryLevel = boolValue(
                dictionary, "enableBatteryLevel", defaultValue: options.enableBatteryLevel
            )
        }
        if hasBool(dictionary, "enableOpenFileHandleTracking") {
            options.enableOpenFileHandleTracking = boolValue(
                dictionary,
                "enableOpenFileHandleTracking",
                defaultValue: options.enableOpenFileHandleTracking
            )
        }
        if let guardName = stringValue(dictionary, "toolGuard") {
            options.toolGuard = toolGuard(guardName)
        } else if useDefaults {
            options.toolGuard = .readOnly
        }
        if let properties = dictionary?["customProperties"] as? NSDictionary {
            options.customProperties = groupedStringDictionary(properties)
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
            if boolValue(memory, "physicalFootprint", defaultValue: false) {
                channels.insert(.physicalFootprint)
            }
            options.defaultMemoryChannels = channels
        }
        if let raw = dictionary?["sessionJpegCapture"] {
            if (raw as? Bool) == false {
                options.sessionJpegCapture = nil
            } else if let capture = raw as? NSDictionary {
                options.sessionJpegCapture = screenCaptureOptions(capture)
            }
        }
        if let raw = dictionary?["touchCapture"] {
            if (raw as? Bool) == false {
                options.touchCapture = nil
            } else if let touch = raw as? NSDictionary {
                options.touchCapture = AnsightTouchCaptureOptions(
                    captureMoveEvents: boolValue(touch, "captureMoveEvents", defaultValue: true),
                    captureCancelEvents: boolValue(touch, "captureCancelEvents", defaultValue: true),
                    moveCaptureDistanceThreshold: doubleValue(
                        touch,
                        "moveCaptureDistanceThreshold",
                        defaultValue: AnsightTouchCaptureOptions.defaultMoveCaptureDistanceThreshold
                    ),
                    moveCaptureFramesPerSecond: intValue(
                        touch,
                        "moveCaptureFramesPerSecond",
                        defaultValue: AnsightTouchCaptureOptions.defaultMoveCaptureFramesPerSecond
                    )
                )
            }
        }
        if let raw = dictionary?["crashCapture"] {
            if (raw as? Bool) == false {
                options.crashCapture.enabled = false
            } else if let crash = raw as? NSDictionary {
                options.crashCapture = AnsightCrashCaptureOptions(
                    enabled: boolValue(crash, "enabled", defaultValue: true),
                    studioHandoffEnabled: boolValue(crash, "studioHandoffEnabled", defaultValue: true),
                    offlineCaptureAttachmentEnabled: boolValue(crash, "offlineCaptureAttachmentEnabled", defaultValue: true),
                    maximumPendingReports: intValue(crash, "maximumPendingReports", defaultValue: 8),
                    retentionDays: intValue(crash, "retentionDays", defaultValue: 7),
                    maximumBreadcrumbs: intValue(crash, "maximumBreadcrumbs", defaultValue: 64),
                    maximumTraceBytes: intValue(crash, "maximumTraceBytes", defaultValue: 1_048_576)
                )
            }
        }
        if let lifecycle = dictionary?["lifecycleCapture"] as? NSDictionary {
            options.lifecycleCapture = AnsightLifecycleCaptureOptions(
                enabled: boolValue(lifecycle, "enabled", defaultValue: options.lifecycleCapture.enabled),
                captureAppLifecycle: boolValue(
                    lifecycle, "captureAppLifecycle", defaultValue: options.lifecycleCapture.captureAppLifecycle
                ),
                captureScreenViews: boolValue(
                    lifecycle, "captureScreenViews", defaultValue: options.lifecycleCapture.captureScreenViews
                ),
                minimumScreenViewIntervalMilliseconds: intValue(
                    lifecycle,
                    "minimumScreenViewIntervalMilliseconds",
                    defaultValue: options.lifecycleCapture.minimumScreenViewIntervalMilliseconds
                )
            )
        }
        if let autoProbe = dictionary?["hostAutoProbe"] as? NSDictionary {
            options.hostAutoProbe = AnsightHostAutoProbeOptions(
                enabled: boolValue(autoProbe, "enabled", defaultValue: options.hostAutoProbe.enabled),
                initialDelayMilliseconds: intValue(
                    autoProbe, "initialDelayMilliseconds", defaultValue: options.hostAutoProbe.initialDelayMilliseconds
                ),
                probeIntervalMilliseconds: intValue(
                    autoProbe, "probeIntervalMilliseconds", defaultValue: options.hostAutoProbe.probeIntervalMilliseconds
                ),
                reconnectDelayMilliseconds: intValue(
                    autoProbe, "reconnectDelayMilliseconds", defaultValue: options.hostAutoProbe.reconnectDelayMilliseconds
                ),
                clientName: stringValue(autoProbe, "clientName") ?? options.hostAutoProbe.clientName
            )
        }
        if let host = dictionary?["hostConnection"] as? NSDictionary {
            options.hostConnection = AnsightHostConnectionOptions(
                savedConfigKey: stringValue(host, "savedConfigKey") ?? options.hostConnection.savedConfigKey,
                connectionProfileRetentionSeconds: intValue(
                    host,
                    "connectionProfileRetentionSeconds",
                    defaultValue: options.hostConnection.connectionProfileRetentionSeconds
                ),
                discoveryPort: optionalInt(host, "discoveryPort") ?? options.hostConnection.discoveryPort,
                allowCellularConnections: boolValue(
                    host,
                    "allowCellularConnections",
                    defaultValue: options.hostConnection.allowCellularConnections
                ),
                bundledConfigJson: stringValue(host, "bundledConfigJson")
                    ?? options.hostConnection.bundledConfigJson
            )
        }
        if let channels = dictionary?["additionalChannels"] as? [NSDictionary] {
            options.additionalChannels = channels.map(channelFromDictionary)
        }
        return try options.validated()
    }

    private func remoteToolOptions(_ dictionary: NSDictionary?) -> AnsightRemoteToolOptions {
        let remoteTools = dictionary?["remoteTools"] as? NSDictionary
        let defaultEnabled = boolValue(dictionary, "useNativeAllInOneDefaults", defaultValue: false)
        return AnsightRemoteToolOptions(
            visualTree: toolSuiteEnabled(remoteTools?["visualTree"], defaultValue: defaultEnabled),
            database: AnsightDatabaseToolsOptions(
                additionalRoots: rootDictionaries((remoteTools?["database"] as? NSDictionary)?["additionalRoots"]).map {
                    AnsightDatabaseRoot(alias: stringValue($0, "alias") ?? "", path: stringValue($0, "path") ?? "")
                },
                includePlatformRoots: boolValue(
                    remoteTools?["database"] as? NSDictionary, "includePlatformRoots", defaultValue: true
                )
            ),
            fileSystem: AnsightFileSystemToolsOptions(
                additionalRoots: rootDictionaries((remoteTools?["fileSystem"] as? NSDictionary)?["additionalRoots"]).map {
                    AnsightFileSystemRoot(alias: stringValue($0, "alias") ?? "", path: stringValue($0, "path") ?? "")
                }
            ),
            preferences: AnsightPreferencesToolOptions(
                defaultStore: stringValue(remoteTools?["preferences"] as? NSDictionary, "defaultStore"),
                allowedStores: stringArray(remoteTools?["preferences"] as? NSDictionary, "allowedStores"),
                allowedKeys: stringArray(remoteTools?["preferences"] as? NSDictionary, "allowedKeys"),
                allowedKeyPrefixes: stringArray(
                    remoteTools?["preferences"] as? NSDictionary, "allowedKeyPrefixes"
                )
            ),
            reflection: AnsightReflectionToolsOptions(
                includeBuiltInRoots: boolValue(
                    remoteTools?["reflection"] as? NSDictionary, "includeBuiltInRoots", defaultValue: true
                ),
                allowedRootIds: stringArray(remoteTools?["reflection"] as? NSDictionary, "allowedRootIds"),
                allowedTypePrefixes: stringArray(
                    remoteTools?["reflection"] as? NSDictionary, "allowedTypePrefixes"
                )
            ),
            secureStorage: secureStorageToolsOptions(
                remoteTools?["secureStorage"] as? NSDictionary ?? dictionary?["secureStorage"] as? NSDictionary
            )
        )
    }

    private func secureStorageToolsOptions(_ dictionary: NSDictionary?) -> AnsightSecureStorageToolsOptions {
        AnsightSecureStorageToolsOptions(
            appleService: stringValue(dictionary, "appleService"),
            allowedKeys: stringArray(dictionary, "allowedKeys"),
            allowedKeyPrefixes: stringArray(dictionary, "allowedKeyPrefixes")
                + stringArray(dictionary, "allowedPrefixes")
        )
    }

    private func toolDescriptor(_ dictionary: NSDictionary?) throws -> AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: stringValue(dictionary, "id") ?? "",
            name: stringValue(dictionary, "name") ?? stringValue(dictionary, "id") ?? "",
            description: stringValue(dictionary, "description") ?? "",
            category: stringValue(dictionary, "category") ?? "custom",
            scope: toolScope(stringValue(dictionary, "scope")).rawValue,
            keywords: keywords(dictionary?["keywords"]),
            security: toolSecurity(dictionary?["security"] as? NSDictionary),
            argumentsSchema: AnsightToolSchema(json: jsonValue(dictionary?["argumentsSchema"]) ?? .object([:])),
            resultSchema: AnsightToolSchema(json: jsonValue(dictionary?["resultSchema"]) ?? .object([:]))
        )
    }

    private func snapshotDictionary() -> [String: Any] {
        let snapshot = AnsightRuntime.shared.snapshot()
        var result: [String: Any] = [
            "initialized": snapshot.initialized,
            "active": snapshot.active,
            "sessionOpen": snapshot.sessionOpen,
            "lifecycleState": snapshot.lifecycleState.rawValue,
            "metricsRecorded": snapshot.metricsRecorded,
            "eventsRecorded": snapshot.eventsRecorded,
            "registeredTools": snapshot.registeredTools,
            "executableTools": snapshot.executableTools,
            "touchesRecorded": snapshot.touchesCaptured,
            "touchesCaptured": snapshot.touchesCaptured,
            "touchesSent": snapshot.touchesSent,
            "touchCaptureEnabled": snapshot.touchCaptureEnabled,
            "screenFramesCaptured": snapshot.screenFramesCaptured,
            "screenFramesSent": snapshot.screenFramesSent,
            "connectionStatus": hostConnectionStatusDictionary(snapshot.hostConnectionStatus),
            "channels": snapshot.channels.map(channelDictionary),
        ]
        if let value = snapshot.lastMetric { result["lastMetric"] = metricDictionary(value) }
        if let value = snapshot.lastEvent { result["lastEvent"] = eventDictionary(value) }
        if let value = snapshot.sessionMessage { result["sessionMessage"] = value }
        if let value = snapshot.currentScreen {
            result["currentScreen"] = [
                "name": value.name,
                "capturedAtUtc": value.capturedAtUtc,
                "details": value.details,
            ]
        }
        return result
    }

    private func hostConnectionStatusDictionary(_ status: HostConnectionStatus) -> [String: Any] {
        [
            "isRuntimeActive": status.isRuntimeActive,
            "isConnected": status.isConnected,
            "connectionState": status.connectionState.rawValue,
            "hasCachedSession": status.hasCachedSession,
            "hasSavedConfig": status.hasSavedConfig,
            "hasBundledConfig": status.hasBundledConfig,
            "summaryKind": status.summaryKind.rawValue,
            "summaryMessage": status.summaryMessage,
        ]
    }

    private func hostConnectionCapabilitiesDictionary(_ capabilities: HostConnectionCapabilities) -> [String: Any] {
        [
            "canConnectUsingSavedConfig": capabilities.canConnectUsingSavedConfig,
            "canConnectUsingBundledConfig": capabilities.canConnectUsingBundledConfig,
            "canChooseConfigFile": capabilities.canChooseConfigFile,
            "canScanConfigQrCode": capabilities.canScanConfigQrCode,
            "canClearSavedConfigs": capabilities.canClearSavedConfigs,
        ]
    }

    private func hostConnectionResultDictionary(_ result: HostConnectionResult) -> [String: Any] {
        var dictionary: [String: Any] = [
            "success": result.success,
            "message": result.message,
            "kind": result.kind.rawValue,
            "source": result.source.rawValue,
        ]
        if let value = result.reasonCode ?? result.openSession?.reasonCode { dictionary["reasonCode"] = value }
        if let session = result.openSession {
            dictionary["accepted"] = session.accepted
            if let value = session.sessionId { dictionary["sessionId"] = value }
            if let value = session.configId { dictionary["configId"] = value }
            if let value = session.appId { dictionary["appId"] = value }
            if let value = session.resolvedHostAddress { dictionary["resolvedHostAddress"] = value }
            if let value = session.discoverySource { dictionary["discoverySource"] = value }
            if let value = session.hostId { dictionary["hostId"] = value }
            if let value = session.hostName { dictionary["hostName"] = value }
        }
        return dictionary
    }

    private func openSessionResultDictionary(_ result: OpenSessionResult) -> [String: Any] {
        var dictionary: [String: Any] = [
            "success": result.success,
            "message": result.message,
            "accepted": result.accepted,
        ]
        if let value = result.sessionId { dictionary["sessionId"] = value }
        if let value = result.configId { dictionary["configId"] = value }
        if let value = result.appId { dictionary["appId"] = value }
        if let value = result.resolvedHostAddress { dictionary["resolvedHostAddress"] = value }
        if let value = result.discoverySource { dictionary["discoverySource"] = value }
        if let value = result.reasonCode { dictionary["reasonCode"] = value }
        if let value = result.hostId { dictionary["hostId"] = value }
        if let value = result.hostName { dictionary["hostName"] = value }
        return dictionary
    }

    private func operationResultDictionary(_ result: OperationResult) -> [String: Any] {
        ["success": result.success, "message": result.message]
    }

    private func optionsDictionary(_ options: AnsightOptions) -> [String: Any] {
        [
            "sampleFrequencyMilliseconds": options.sampleFrequencyMilliseconds,
            "retentionPeriodSeconds": options.retentionPeriodSeconds,
            "enableFramesPerSecond": options.enableFramesPerSecond,
            "enableBatteryLevel": options.enableBatteryLevel,
            "enableOpenFileHandleTracking": options.enableOpenFileHandleTracking,
            "enableJniReferenceCountTracking": false,
            "toolGuard": toolGuardName(options.toolGuard),
            "additionalChannels": options.additionalChannels.map(channelDictionary),
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
            ],
            "hostConnection": [
                "savedConfigKey": options.hostConnection.savedConfigKey,
                "connectionProfileRetentionSeconds": options.hostConnection.connectionProfileRetentionSeconds,
                "allowCellularConnections": options.hostConnection.allowCellularConnections,
                "hasBundledConfigJson": options.hostConnection.bundledConfigJson != nil,
            ],
        ]
    }

    private func screenCaptureOptions(_ dictionary: NSDictionary?) -> AnsightSessionJpegCaptureOptions {
        AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: intValue(
                dictionary,
                "intervalMilliseconds",
                defaultValue: AnsightSessionJpegCaptureOptions.defaultIntervalMilliseconds
            ),
            quality: intValue(
                dictionary, "quality", defaultValue: AnsightSessionJpegCaptureOptions.defaultQuality
            ),
            maxWidth: optionalInt(dictionary, "maxWidth") ?? AnsightSessionJpegCaptureOptions.defaultMaxWidth,
            captureGpuBackedSurfaces: boolValue(
                dictionary,
                "captureGpuBackedSurfaces",
                defaultValue: AnsightSessionJpegCaptureOptions.defaultCaptureGpuBackedSurfaces
            ),
            mode: AnsightSessionJpegCaptureMode(
                rawValue: stringValue(dictionary, "mode") ?? ""
            ) ?? .screenshotOnly
        )
    }

    private func pairingOpenOptions(_ dictionary: NSDictionary?) -> PairingOpenOptions {
        PairingOpenOptions(
            clientName: stringValue(dictionary, "clientName") ?? "Capacitor",
            expectedAppId: stringValue(dictionary, "expectedAppId"),
            hostAddressOverride: stringValue(dictionary, "hostAddressOverride"),
            discoveryPort: optionalInt(dictionary, "discoveryPort")
        )
    }

    private func channelFromDictionary(_ dictionary: NSDictionary) -> AnsightChannel {
        AnsightChannel(
            id: intValue(dictionary, "id", defaultValue: -1),
            name: stringValue(dictionary, "name") ?? "",
            colorHex: stringValue(dictionary, "colorHex"),
            unit: stringValue(dictionary, "unit"),
            type: stringValue(dictionary, "type") ?? "custom",
            source: stringValue(dictionary, "source"),
            group: stringValue(dictionary, "group"),
            kind: stringValue(dictionary, "kind")
        )
    }

    private func channelDictionary(_ channel: AnsightChannel) -> [String: Any] {
        var result: [String: Any] = ["id": channel.id, "name": channel.name, "type": channel.type]
        if let value = channel.unit { result["unit"] = value }
        if let value = channel.colorHex { result["colorHex"] = value }
        if let value = channel.source { result["source"] = value }
        if let value = channel.group { result["group"] = value }
        if let value = channel.kind { result["kind"] = value }
        return result
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
        var result: [String: Any] = [
            "id": event.id,
            "label": event.label,
            "type": eventTypeName(event.type),
            "capturedAtUtc": event.capturedAtUtc,
            "capturedAtEpochMs": event.capturedAtEpochMs,
            "channel": event.channel,
            "sequence": event.sequence,
        ]
        if let value = event.details { result["details"] = value }
        if let value = event.externalId { result["externalId"] = value }
        return result
    }
}

private extension NSLock {
    func withLock<T>(_ body: () -> T) -> T {
        lock()
        defer { unlock() }
        return body()
    }
}

private func dictionary(_ call: CAPPluginCall) -> NSDictionary {
    call.dictionaryRepresentation
}

private func nestedDictionary(_ call: CAPPluginCall, _ key: String) -> NSDictionary? {
    call.dictionaryRepresentation[key] as? NSDictionary
}

private func stringValue(_ dictionary: NSDictionary?, _ key: String) -> String? {
    guard let value = dictionary?[key], !(value is NSNull) else { return nil }
    let string = value as? String ?? "\(value)"
    let trimmed = string.trimmingCharacters(in: .whitespacesAndNewlines)
    return trimmed.isEmpty ? nil : trimmed
}

private func hasNumber(_ dictionary: NSDictionary?, _ key: String) -> Bool {
    dictionary?[key] is NSNumber
}

private func hasBool(_ dictionary: NSDictionary?, _ key: String) -> Bool {
    guard let number = dictionary?[key] as? NSNumber else { return false }
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
    guard let dictionary else { return [:] }
    var result: [String: String] = [:]
    for (key, value) in dictionary {
        guard let key = key as? String, !(value is NSNull) else { continue }
        result[key] = value as? String ?? "\(value)"
    }
    return result
}

private func groupedStringDictionary(_ dictionary: NSDictionary?) -> [String: [String: String]] {
    guard let dictionary else { return [:] }
    var result: [String: [String: String]] = [:]
    for (key, value) in dictionary {
        guard let key = key as? String, let group = value as? NSDictionary else { continue }
        result[key] = stringDictionary(group)
    }
    return result
}

private func stringArray(_ dictionary: NSDictionary?, _ key: String) -> [String] {
    (dictionary?[key] as? [Any])?.compactMap {
        guard !($0 is NSNull) else { return nil }
        let value = ($0 as? String ?? "\($0)").trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    } ?? []
}

private func rootDictionaries(_ value: Any?) -> [NSDictionary] {
    (value as? [Any])?.compactMap { $0 as? NSDictionary } ?? []
}

private func toolSuiteEnabled(_ value: Any?, defaultValue: Bool) -> Bool {
    if let value = value as? Bool { return value }
    if let value = value as? NSDictionary {
        return boolValue(value, "enabled", defaultValue: true)
    }
    return defaultValue
}

private func eventType(_ value: String?) -> AnsightEventType {
    switch value?.lowercased() {
    case "event": return .event
    case "debug": return .debug
    case "warning", "warn": return .warning
    case "error": return .error
    case "exception": return .exception
    case "gc": return .gc
    case "navigation": return .navigation
    case "screenviewed", "screen_viewed": return .screenViewed
    case "lifecycle": return .lifecycle
    default: return .info
    }
}

private func eventTypeName(_ value: AnsightEventType) -> String {
    switch value {
    case .event: return "Event"
    case .debug: return "Debug"
    case .info: return "Info"
    case .warning: return "Warning"
    case .error: return "Error"
    case .exception: return "Exception"
    case .gc: return "Gc"
    case .navigation: return "Navigation"
    case .screenViewed: return "ScreenViewed"
    case .lifecycle: return "Lifecycle"
    }
}

private func lifecycleState(_ value: String) -> AppLifecycleState {
    switch value.lowercased() {
    case "foreground", "active": return .foreground
    case "background", "inactive": return .background
    default: return .unknown
    }
}

private func toolGuard(_ value: String) -> AnsightToolGuard {
    switch value.lowercased() {
    case "readonly", "read_only", "read": return .readOnly
    case "readwrite", "read_write", "write": return .readWrite
    case "full", "fullaccess", "full_access": return .fullAccess
    default: return .disabled
    }
}

private func toolGuardName(_ value: AnsightToolGuard) -> String {
    if value == .disabled { return "disabled" }
    if value == .readOnly { return "readOnly" }
    if value == .readWrite { return "readWrite" }
    if value == .fullAccess { return "fullAccess" }
    return "custom"
}

private func toolScope(_ value: String?) -> AnsightToolScope {
    switch value?.lowercased() {
    case "write": return .write
    case "delete": return .delete
    default: return .read
    }
}

private func toolSecurity(_ dictionary: NSDictionary?) -> AnsightToolSecurity {
    guard let dictionary else { return .unspecified }
    let level: AnsightToolSecurityLevel
    switch stringValue(dictionary, "level")?.lowercased() {
    case "low": level = .low
    case "medium", "moderate": level = .moderate
    case "high": level = .high
    case "critical": level = .critical
    default: level = .unspecified
    }
    return AnsightToolSecurity(
        level: level,
        summary: stringValue(dictionary, "summary") ?? "",
        implications: (dictionary["implications"] as? [Any])?.compactMap { $0 as? String } ?? []
    )
}

private func keywords(_ value: Any?) -> String {
    if let value = value as? String { return value }
    if let value = value as? [Any] { return value.compactMap { $0 as? String }.joined(separator: " ") }
    return "capacitor javascript custom tool"
}

private func jsonValue(_ value: Any?) -> JSONValue? {
    guard let value, !(value is NSNull) else { return nil }
    if let dictionary = value as? NSDictionary {
        var object: [String: JSONValue] = [:]
        for (key, value) in dictionary {
            guard let key = key as? String, let converted = jsonValue(value) else { continue }
            object[key] = converted
        }
        return .object(object)
    }
    if let array = value as? [Any] { return .array(array.map { jsonValue($0) ?? .null }) }
    if let string = value as? String { return .string(string) }
    if let number = value as? NSNumber {
        if CFGetTypeID(number) == CFBooleanGetTypeID() { return .bool(number.boolValue) }
        let double = number.doubleValue
        return double.rounded() == double ? .integer(number.int64Value) : .number(double)
    }
    if let bool = value as? Bool { return .bool(bool) }
    return .string("\(value)")
}
