import Ansight
import Flutter
import Foundation
import UIKit

public final class AnsightFlutterPlugin: NSObject, FlutterPlugin, AnsightNativeHostApi {
    private final class PendingToolCall {
        let semaphore = DispatchSemaphore(value: 0)
        var result: AnsightToolExecutionResult?
    }

    private struct CustomToolRegistration {
        let descriptor: AnsightToolDescriptor
        let timeoutMilliseconds: Int
    }

    private final class FlutterTool: AnsightTool, @unchecked Sendable {
        let descriptor: AnsightToolDescriptor
        private weak var plugin: AnsightFlutterPlugin?
        private let timeoutMilliseconds: Int

        init(
            descriptor: AnsightToolDescriptor,
            plugin: AnsightFlutterPlugin,
            timeoutMilliseconds: Int
        ) {
            self.descriptor = descriptor
            self.plugin = plugin
            self.timeoutMilliseconds = timeoutMilliseconds
        }

        func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
            guard let plugin else {
                return .failure(
                    "Flutter bridge is no longer available.",
                    errorCode: "dart_bridge_unavailable"
                )
            }
            return plugin.executeDartTool(
                toolId: descriptor.id,
                arguments: arguments,
                timeoutMilliseconds: timeoutMilliseconds
            )
        }
    }

    private final class FlutterVisualTreeProvider: AnsightVisualTreeProvider, @unchecked Sendable {
        let source = "flutter"
        let displayName = "Flutter"
        private weak var plugin: AnsightFlutterPlugin?

        init(plugin: AnsightFlutterPlugin) {
            self.plugin = plugin
        }

        func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
            plugin?.executeDartTool(
                toolId: "__ansight_flutter.visual_tree",
                arguments: arguments,
                timeoutMilliseconds: 30_000,
                requireRegistration: false
            ) ?? .failure("Flutter bridge is unavailable.", errorCode: "dart_bridge_unavailable")
        }

        func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult {
            plugin?.executeDartTool(
                toolId: "__ansight_flutter.inspect_node",
                arguments: arguments,
                timeoutMilliseconds: 30_000,
                requireRegistration: false
            ) ?? .failure("Flutter bridge is unavailable.", errorCode: "dart_bridge_unavailable")
        }
    }

    private let lock = NSLock()
    private var activeCustomToolIds: Set<String> = []
    private var pendingToolCalls: [String: PendingToolCall] = [:]
    private var customToolRegistrations: [String: CustomToolRegistration] = [:]
    private var dartApi: AnsightDartApi?
    private lazy var logCallback = AnsightClosureLogCallback { [weak self] level, message, error in
        self?.emitLogEvent(level: level, message: message, error: error)
    }

    public static func register(with registrar: FlutterPluginRegistrar) {
        let instance = AnsightFlutterPlugin()
        instance.dartApi = AnsightDartApi(binaryMessenger: registrar.messenger())
        AnsightNativeHostApiSetup.setUp(
            binaryMessenger: registrar.messenger(),
            api: instance
        )
        AnsightLogger.registerCallback(instance.logCallback)
        registrar.publish(instance)
    }

    deinit {
        AnsightLogger.removeCallback(logCallback)
        lock.withLock {
            pendingToolCalls.values.forEach { $0.semaphore.signal() }
            pendingToolCalls.removeAll()
        }
    }

    func invoke(
        method: String,
        argumentsJson: String?,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let arguments: NSDictionary
        do {
            arguments = try decodeObject(argumentsJson)
        } catch {
            completion(.failure(error))
            return
        }
        Task {
            do {
                let value = try await dispatch(method: method, arguments: arguments)
                completion(.success(try encodeObject(value)))
            } catch {
                completion(.failure(error))
            }
        }
    }

    func queueBinaryTransfer(
        requestId: String,
        data: FlutterStandardTypedData,
        chunkBytes: Int64,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let transferId = UUID()
        let normalizedChunkBytes = min(max(Int(chunkBytes), 1_024), 512 * 1_024)
        let result = AnsightRuntime.shared.queueBinaryTransfer(
            requestId: requestId,
            transferId: transferId,
            data: data.data,
            chunkBytes: normalizedChunkBytes,
            description: "flutter-artifact:\(compactId(transferId))"
        )
        var payload: [String: Any] = [
            "success": result.success,
            "message": result.message,
            "transferId": compactId(transferId),
            "deliveryMode": "websocket_binary",
            "wireProtocol": PairingFileTransferWireProtocol.protocolName,
            "status": result.success ? "queued" : "failed",
            "chunkBytes": normalizedChunkBytes,
            "sizeBytes": data.data.count,
        ]
        if !result.success {
            payload["errorCode"] = "artifact_transfer_unavailable"
        }
        do {
            completion(.success(try encodeObject(payload)))
        } catch {
            completion(.failure(error))
        }
    }

    private func dispatch(method: String, arguments: NSDictionary) async throws -> Any {
        switch method {
        case "initialize":
            try AnsightRuntime.shared.initialize(options: buildOptions(arguments))
            try AnsightRuntime.shared.registerAnsightRemoteTools(
                options: remoteToolOptions(arguments)
            )
            try installRegisteredCustomTools()
            return snapshotDictionary()
        case "initializeAndActivate":
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk(
                options: buildOptions(arguments),
                remoteToolOptions: remoteToolOptions(arguments)
            )
            try installRegisteredCustomTools()
            return snapshotDictionary()
        case "activate":
            try AnsightRuntime.shared.activate()
            return snapshotDictionary()
        case "deactivate":
            AnsightRuntime.shared.deactivate()
            return snapshotDictionary()
        case "clear":
            AnsightRuntime.shared.clear()
            return snapshotDictionary()
        case "registerMetricChannel":
            try AnsightRuntime.shared.registerMetricChannel(channel(arguments))
            return snapshotDictionary()
        case "recordMetric":
            try AnsightRuntime.shared.metric(
                Int64(doubleValue(arguments, "value", defaultValue: 0)),
                channel: intValue(arguments, "channel", defaultValue: AnsightChannels.unspecified)
            )
            return snapshotDictionary()
        case "recordEvent":
            try AnsightRuntime.shared.event(
                stringValue(arguments, "label") ?? "",
                type: eventType(stringValue(arguments, "type")),
                details: stringValue(arguments, "details"),
                channel: intValue(arguments, "channel", defaultValue: AnsightChannels.unspecified)
            )
            return snapshotDictionary()
        case "screenViewed":
            try AnsightRuntime.shared.screenViewed(
                stringValue(arguments, "name") ?? "",
                details: stringDictionary(arguments["details"] as? NSDictionary)
            )
            return snapshotDictionary()
        case "setAppLifecycleState":
            AnsightRuntime.shared.setAppLifecycleState(
                lifecycleState(stringValue(arguments, "state") ?? "")
            )
            return snapshotDictionary()
        case "connect":
            let clientName = stringValue(arguments, "clientName")
            let expectedAppId = stringValue(arguments, "expectedAppId")
            let hostAddressOverride = stringValue(arguments, "hostAddressOverride")
            let request: HostConnectionRequest
            if let payload = stringValue(arguments, "pairingPayload") {
                request = .payloadText(
                    payload,
                    clientName: clientName,
                    expectedAppId: expectedAppId,
                    hostAddressOverride: hostAddressOverride,
                    sourceDescription: "Flutter"
                )
            } else {
                request = .auto(
                    clientName: clientName,
                    expectedAppId: expectedAppId,
                    hostAddressOverride: hostAddressOverride,
                    sourceDescription: "Flutter"
                )
            }
            return hostConnectionResultDictionary(await AnsightRuntime.shared.connect(request))
        case "scanPairingQrCode":
            let request = HostConnectionRequest.qrCode(
                title: stringValue(arguments, "title") ?? "Scan Ansight Pairing QR",
                clientName: stringValue(arguments, "clientName"),
                expectedAppId: stringValue(arguments, "expectedAppId"),
                hostAddressOverride: stringValue(arguments, "hostAddressOverride"),
                sourceDescription: "Flutter native QR scanner"
            )
            return hostConnectionResultDictionary(
                await AnsightRuntime.shared.connect(request)
            )
        case "openSession":
            let result = try await AnsightRuntime.shared.openLiveSession(
                pairingJson: stringValue(arguments, "pairingPayload") ?? "",
                options: pairingOpenOptions(arguments)
            )
            return openSessionResultDictionary(result)
        case "disconnect":
            return hostConnectionResultDictionary(await AnsightRuntime.shared.disconnect())
        case "completeSession":
            return operationResultDictionary(await AnsightRuntime.shared.completeLiveSession())
        case "closeSession":
            AnsightRuntime.shared.closeSession()
            return operationResultDictionary(.success("Session closed."))
        case "savePairingConfig":
            return hostConnectionResultDictionary(
                AnsightRuntime.shared.savePairingConfig(
                    stringValue(arguments, "pairingPayload") ?? "",
                    expectedAppId: stringValue(arguments, "expectedAppId")
                )
            )
        case "clearSavedPairing":
            AnsightRuntime.shared.clearSavedPairing()
            return hostConnectionResultDictionary(
                HostConnectionResult(
                    success: true,
                    message: "Saved pairing config cleared.",
                    kind: .savedConfig,
                    source: .savedConfig
                )
            )
        case "clearCachedSession":
            AnsightRuntime.shared.clearCachedSession()
            return operationResultDictionary(.success("Cached live session cleared."))
        case "notifyHostConnectionConfigChanged":
            return hostConnectionResultDictionary(
                AnsightRuntime.shared.notifyHostConnectionConfigChanged()
            )
        case "status", "snapshot":
            return snapshotDictionary()
        case "hostConnectionStatus":
            return hostConnectionStatusDictionary(
                AnsightRuntime.shared.hostConnectionStatus()
            )
        case "hostConnectionCapabilities":
            return hostConnectionCapabilitiesDictionary(
                AnsightRuntime.shared.hostConnectionCapabilities()
            )
        case "currentOptions":
            return optionsDictionary(AnsightRuntime.shared.currentOptions())
        case "recordedMetrics":
            let values = AnsightRuntime.shared.recordedMetrics()
            let limit = max(0, intValue(arguments, "limit", defaultValue: 0))
            return [
                "items": (limit > 0 ? Array(values.suffix(limit)) : values)
                    .map(metricDictionary),
            ]
        case "recordedEvents":
            let values = AnsightRuntime.shared.recordedEvents()
            let limit = max(0, intValue(arguments, "limit", defaultValue: 0))
            return [
                "items": (limit > 0 ? Array(values.suffix(limit)) : values)
                    .map(eventDictionary),
            ]
        case "sendClientLog":
            return operationResultDictionary(
                await AnsightRuntime.shared.sendClientLog(
                    stringValue(arguments, "line") ?? ""
                )
            )
        case "captureBuiltInTelemetrySample":
            AnsightRuntime.shared.captureBuiltInTelemetrySample()
            return snapshotDictionary()
        case "isFramesPerSecondEnabled":
            return ["value": AnsightRuntime.shared.isFramesPerSecondEnabled]
        case "enableFramesPerSecond":
            AnsightRuntime.shared.enableFramesPerSecond()
            return snapshotDictionary()
        case "disableFramesPerSecond":
            AnsightRuntime.shared.disableFramesPerSecond()
            return snapshotDictionary()
        case "captureScreenFrame":
            return operationResultDictionary(
                await AnsightRuntime.shared.captureScreenFrame(
                    options: screenCaptureOptions(arguments)
                )
            )
        case "enableTouchCapture":
            AnsightRuntime.shared.enableTouchCapture()
            return operationResultDictionary(.success("Touch capture enabled."))
        case "disableTouchCapture":
            AnsightRuntime.shared.disableTouchCapture()
            return operationResultDictionary(.success("Touch capture disabled."))
        case "updateSessionProperties":
            return operationResultDictionary(
                await AnsightRuntime.shared.updateSessionProperties(
                    groupedStringDictionary(arguments["properties"] as? NSDictionary)
                )
            )
        case "clearSessionProperties":
            return operationResultDictionary(
                await AnsightRuntime.shared.clearSessionProperties()
            )
        case "registerCustomProperty":
            return operationResultDictionary(
                await updateCustomProperty(arguments, remove: false)
            )
        case "removeCustomProperty":
            return operationResultDictionary(
                await updateCustomProperty(arguments, remove: true)
            )
        case "registerCustomTool":
            return try registerCustomTool(
                arguments["definition"] as? NSDictionary ?? NSDictionary()
            )
        case "unregisterCustomTool":
            return unregisterCustomTool(stringValue(arguments, "id") ?? "")
        case "clearRegisteredCustomTools":
            return clearRegisteredCustomTools()
        case "resolveToolCall":
            return resolveToolCall(arguments)
        case "enableFlutterVisualTreeProvider":
            try AnsightVisualTreeProviderRegistry.register(
                FlutterVisualTreeProvider(plugin: self),
                replaceExisting: true
            )
            return operationResultDictionary(
                .success("Flutter visual-tree provider registered."),
                additions: ["source": "flutter"]
            )
        default:
            throw AnsightFlutterError.unknownMethod(method)
        }
    }

    private func updateCustomProperty(
        _ arguments: NSDictionary,
        remove: Bool
    ) async -> OperationResult {
        let group = stringValue(arguments, "group") ?? ""
        let key = stringValue(arguments, "key") ?? ""
        guard !group.isEmpty else {
            return .failure("Custom property group must not be blank.")
        }
        guard !key.isEmpty else {
            return .failure("Custom property key must not be blank.")
        }
        var properties = AnsightRuntime.shared.currentOptions().customProperties
        var groupProperties = properties[group] ?? [:]
        if remove {
            groupProperties.removeValue(forKey: key)
        } else {
            groupProperties[key] = stringValue(arguments, "value") ?? ""
        }
        if groupProperties.isEmpty {
            properties.removeValue(forKey: group)
        } else {
            properties[group] = groupProperties
        }
        return await AnsightRuntime.shared.updateSessionProperties(properties)
    }

    private func registerCustomTool(_ definition: NSDictionary) throws -> NSDictionary {
        let descriptor = try toolDescriptor(definition)
        let registration = CustomToolRegistration(
            descriptor: descriptor,
            timeoutMilliseconds: max(
                250,
                intValue(definition, "timeoutMilliseconds", defaultValue: 30_000)
            )
        )
        lock.withLock {
            customToolRegistrations[descriptor.id] = registration
            activeCustomToolIds.insert(descriptor.id)
        }
        if AnsightRuntime.shared.snapshot().initialized {
            try installCustomTool(registration)
        }
        return [
            "success": true,
            "message": "Dart tool registered.",
            "id": descriptor.id,
            "registered": true,
        ]
    }

    private func unregisterCustomTool(_ toolId: String) -> NSDictionary {
        let id = toolId.trimmingCharacters(in: .whitespacesAndNewlines)
        lock.withLock {
            activeCustomToolIds.remove(id)
            customToolRegistrations.removeValue(forKey: id)
        }
        return [
            "success": true,
            "message": "Dart tool unregistered.",
            "id": id,
            "registered": false,
        ]
    }

    private func clearRegisteredCustomTools() -> NSDictionary {
        lock.withLock {
            activeCustomToolIds.removeAll()
            customToolRegistrations.removeAll()
        }
        return [
            "success": true,
            "message": "Dart tools cleared.",
            "cleared": true,
        ]
    }

    private func resolveToolCall(_ arguments: NSDictionary) -> NSDictionary {
        let requestId = stringValue(arguments, "requestId") ?? ""
        guard let pending = lock.withLock({ pendingToolCalls[requestId] }) else {
            return [
                "success": false,
                "message": "Tool request is no longer pending.",
                "accepted": false,
            ]
        }
        let result = arguments["result"] as? NSDictionary ?? NSDictionary()
        let success = boolValue(result, "success", defaultValue: true)
        let message = stringValue(result, "message")
        let errorCode = stringValue(result, "errorCode")
        let payload = jsonValue(result["result"])
        pending.result = success
            ? .success(payload, message: message)
            : .failure(
                message ?? "Dart tool failed.",
                errorCode: errorCode,
                result: payload
            )
        pending.semaphore.signal()
        return [
            "success": true,
            "message": "Tool result accepted.",
            "accepted": true,
        ]
    }

    fileprivate func executeDartTool(
        toolId: String,
        arguments: [String: String],
        timeoutMilliseconds: Int,
        requireRegistration: Bool = true
    ) -> AnsightToolExecutionResult {
        let requestId = "ios.flutter.\(compactId(UUID()))"
        let pending = PendingToolCall()
        let canDispatch = lock.withLock { () -> Bool in
            guard !requireRegistration || activeCustomToolIds.contains(toolId) else {
                return false
            }
            pendingToolCalls[requestId] = pending
            return dartApi != nil
        }
        guard canDispatch else {
            lock.withLock { pendingToolCalls.removeValue(forKey: requestId) }
            return .failure(
                "Flutter Dart bridge is unavailable for tool '\(toolId)'.",
                errorCode: "dart_bridge_unavailable"
            )
        }
        var request: [String: Any] = [
            "requestId": requestId,
            "toolId": toolId,
            "platform": "ios",
            "arguments": arguments,
        ]
        if let nativeRequestId = arguments[AnsightToolExecutionArgumentNames.requestId] {
            request["nativeRequestId"] = nativeRequestId
        }
        if let sessionId = arguments[AnsightToolExecutionArgumentNames.sessionId] {
            request["sessionId"] = sessionId
        }
        DispatchQueue.main.async { [weak self] in
            guard let self else {
                pending.semaphore.signal()
                return
            }
            do {
                let encoded = try encodeObject(request)
                dartApi?.onToolCall(requestJson: encoded) { result in
                    if case .failure(let error) = result {
                        pending.result = .failure(
                            "Could not dispatch Dart tool '\(toolId)': \(error)",
                            errorCode: "dart_tool_dispatch_failed"
                        )
                        pending.semaphore.signal()
                    }
                }
            } catch {
                pending.result = .failure(
                    "Could not encode Dart tool '\(toolId)': \(error)",
                    errorCode: "dart_tool_dispatch_failed"
                )
                pending.semaphore.signal()
            }
        }
        let deadline = DispatchTime.now() + .milliseconds(timeoutMilliseconds)
        guard pending.semaphore.wait(timeout: deadline) == .success else {
            lock.withLock { pendingToolCalls.removeValue(forKey: requestId) }
            return .failure(
                "Dart handler for tool '\(toolId)' timed out.",
                errorCode: "dart_tool_timeout"
            )
        }
        lock.withLock { pendingToolCalls.removeValue(forKey: requestId) }
        return pending.result ?? .failure(
            "Dart handler for tool '\(toolId)' returned no result.",
            errorCode: "dart_tool_empty_result"
        )
    }

    private func installRegisteredCustomTools() throws {
        let registrations = lock.withLock { Array(customToolRegistrations.values) }
        for registration in registrations {
            try installCustomTool(registration)
        }
    }

    private func installCustomTool(_ registration: CustomToolRegistration) throws {
        try AnsightRuntime.shared.registerTool(
            FlutterTool(
                descriptor: registration.descriptor,
                plugin: self,
                timeoutMilliseconds: registration.timeoutMilliseconds
            ),
            replaceExisting: true
        )
    }

    private func buildOptions(_ dictionary: NSDictionary) throws -> AnsightOptions {
        let useDefaults = boolValue(
            dictionary,
            "useNativeAllInOneDefaults",
            defaultValue: false
        )
        var options = useDefaults ? AnsightOptions.ansightDeveloperDefaults : AnsightOptions()
        if let value = stringValue(dictionary, "pairingConfigJson") {
            if useDefaults {
                options.hostConnection.bundledDeveloperConfigJson = value
            } else {
                options.hostConnection.bundledConfigJson = value
            }
        }
        if useDefaults && stringValue(dictionary, "toolGuard") == nil {
            options.toolGuard = .readOnly
        }
        if let value = stringValue(dictionary, "clientName") {
            options.hostAutoProbe.clientName = value
        }
        if hasNumber(dictionary, "sampleFrequencyMilliseconds") {
            options.sampleFrequencyMilliseconds = intValue(
                dictionary,
                "sampleFrequencyMilliseconds",
                defaultValue: options.sampleFrequencyMilliseconds
            )
        }
        if hasNumber(dictionary, "retentionPeriodSeconds") {
            options.retentionPeriodSeconds = intValue(
                dictionary,
                "retentionPeriodSeconds",
                defaultValue: options.retentionPeriodSeconds
            )
        }
        if hasBool(dictionary, "enableFramesPerSecond") {
            options.enableFramesPerSecond = boolValue(
                dictionary,
                "enableFramesPerSecond",
                defaultValue: options.enableFramesPerSecond
            )
        }
        if hasBool(dictionary, "enableBatteryLevel") {
            options.enableBatteryLevel = boolValue(
                dictionary,
                "enableBatteryLevel",
                defaultValue: options.enableBatteryLevel
            )
        }
        if let memory = dictionary["defaultMemoryChannels"] as? NSDictionary {
            var channels: DefaultMemoryChannels = []
            if boolValue(
                memory,
                "managedHeap",
                defaultValue: boolValue(memory, "javaHeap", defaultValue: false)
            ) {
                channels.insert(.managedHeap)
            }
            if boolValue(memory, "nativeHeap", defaultValue: false) {
                channels.insert(.nativeHeap)
            }
            if boolValue(
                memory,
                "residentSetSize",
                defaultValue: boolValue(memory, "rss", defaultValue: false)
            ) {
                channels.insert(.residentSetSize)
            }
            if boolValue(memory, "physicalFootprint", defaultValue: false) {
                channels.insert(.physicalFootprint)
            }
            options.defaultMemoryChannels = channels
        }
        if let channels = dictionary["additionalChannels"] as? [NSDictionary] {
            options.additionalChannels = channels.map(channel)
        }
        if let raw = dictionary["sessionJpegCapture"] {
            if let enabled = raw as? Bool, !enabled {
                options.sessionJpegCapture = nil
            } else if let jpeg = raw as? NSDictionary {
                options.sessionJpegCapture = screenCaptureOptions(jpeg)
            }
        }
        if let raw = dictionary["touchCapture"] {
            if let enabled = raw as? Bool, !enabled {
                options.touchCapture = nil
            } else if let touch = raw as? NSDictionary {
                options.touchCapture = AnsightTouchCaptureOptions(
                    captureMoveEvents: boolValue(
                        touch,
                        "captureMoveEvents",
                        defaultValue: true
                    ),
                    captureCancelEvents: boolValue(
                        touch,
                        "captureCancelEvents",
                        defaultValue: true
                    ),
                    moveCaptureDistanceThreshold: doubleValue(
                        touch,
                        "moveCaptureDistanceThreshold",
                        defaultValue: AnsightTouchCaptureOptions
                            .defaultMoveCaptureDistanceThreshold
                    ),
                    moveCaptureFramesPerSecond: intValue(
                        touch,
                        "moveCaptureFramesPerSecond",
                        defaultValue: AnsightTouchCaptureOptions
                            .defaultMoveCaptureFramesPerSecond
                    )
                )
            }
        }
        if let lifecycle = dictionary["lifecycleCapture"] as? NSDictionary {
            options.lifecycleCapture = AnsightLifecycleCaptureOptions(
                enabled: boolValue(
                    lifecycle,
                    "enabled",
                    defaultValue: options.lifecycleCapture.enabled
                ),
                captureAppLifecycle: boolValue(
                    lifecycle,
                    "captureAppLifecycle",
                    defaultValue: options.lifecycleCapture.captureAppLifecycle
                ),
                captureScreenViews: boolValue(
                    lifecycle,
                    "captureScreenViews",
                    defaultValue: options.lifecycleCapture.captureScreenViews
                ),
                minimumScreenViewIntervalMilliseconds: intValue(
                    lifecycle,
                    "minimumScreenViewIntervalMilliseconds",
                    defaultValue:
                        options.lifecycleCapture.minimumScreenViewIntervalMilliseconds
                )
            )
        }
        if let value = stringValue(dictionary, "toolGuard") {
            options.toolGuard = toolGuard(value)
        }
        if let properties = dictionary["customProperties"] as? NSDictionary {
            options.customProperties = groupedStringDictionary(properties)
        }
        if let probe = dictionary["hostAutoProbe"] as? NSDictionary {
            options.hostAutoProbe = AnsightHostAutoProbeOptions(
                enabled: boolValue(
                    probe,
                    "enabled",
                    defaultValue: options.hostAutoProbe.enabled
                ),
                initialDelayMilliseconds: intValue(
                    probe,
                    "initialDelayMilliseconds",
                    defaultValue: options.hostAutoProbe.initialDelayMilliseconds
                ),
                probeIntervalMilliseconds: intValue(
                    probe,
                    "probeIntervalMilliseconds",
                    defaultValue: options.hostAutoProbe.probeIntervalMilliseconds
                ),
                reconnectDelayMilliseconds: intValue(
                    probe,
                    "reconnectDelayMilliseconds",
                    defaultValue: options.hostAutoProbe.reconnectDelayMilliseconds
                ),
                clientName:
                    stringValue(probe, "clientName") ?? options.hostAutoProbe.clientName
            )
        }
        if let host = dictionary["hostConnection"] as? NSDictionary {
            options.hostConnection = AnsightHostConnectionOptions(
                savedConfigKey:
                    stringValue(host, "savedConfigKey")
                    ?? options.hostConnection.savedConfigKey,
                connectionProfileRetentionSeconds: intValue(
                    host,
                    "connectionProfileRetentionSeconds",
                    defaultValue: options.hostConnection.connectionProfileRetentionSeconds
                ),
                discoveryPort:
                    optionalInt(host, "discoveryPort") ?? options.hostConnection.discoveryPort,
                bundledDeveloperConfigJson:
                    stringValue(host, "bundledDeveloperConfigJson")
                    ?? options.hostConnection.bundledDeveloperConfigJson,
                bundledConfigJson:
                    stringValue(host, "bundledConfigJson")
                    ?? options.hostConnection.bundledConfigJson
            )
        }
        return try options.validated()
    }

    private func remoteToolOptions(_ dictionary: NSDictionary) -> AnsightRemoteToolOptions {
        let remote = dictionary["remoteTools"] as? NSDictionary
        let defaults = boolValue(
            dictionary,
            "useNativeAllInOneDefaults",
            defaultValue: false
        )
        return AnsightRemoteToolOptions(
            visualTree: toolSuiteEnabled(remote?["visualTree"], defaultValue: defaults),
            database: AnsightDatabaseToolsOptions(
                additionalRoots: rootDictionaries(
                    (remote?["database"] as? NSDictionary)?["additionalRoots"]
                ).map {
                    AnsightDatabaseRoot(
                        alias: stringValue($0, "alias") ?? "",
                        path: stringValue($0, "path") ?? ""
                    )
                },
                includePlatformRoots: boolValue(
                    remote?["database"] as? NSDictionary,
                    "includePlatformRoots",
                    defaultValue: true
                )
            ),
            fileSystem: AnsightFileSystemToolsOptions(
                additionalRoots: rootDictionaries(
                    (remote?["fileSystem"] as? NSDictionary)?["additionalRoots"]
                ).map {
                    AnsightFileSystemRoot(
                        alias: stringValue($0, "alias") ?? "",
                        path: stringValue($0, "path") ?? ""
                    )
                }
            ),
            preferences: AnsightPreferencesToolOptions(
                defaultStore: stringValue(
                    remote?["preferences"] as? NSDictionary,
                    "defaultStore"
                ),
                allowedStores: stringArray(
                    remote?["preferences"] as? NSDictionary,
                    "allowedStores"
                ),
                allowedKeys: stringArray(
                    remote?["preferences"] as? NSDictionary,
                    "allowedKeys"
                ),
                allowedKeyPrefixes: stringArray(
                    remote?["preferences"] as? NSDictionary,
                    "allowedKeyPrefixes"
                )
            ),
            reflection: AnsightReflectionToolsOptions(
                includeBuiltInRoots: boolValue(
                    remote?["reflection"] as? NSDictionary,
                    "includeBuiltInRoots",
                    defaultValue: true
                ),
                allowedRootIds: stringArray(
                    remote?["reflection"] as? NSDictionary,
                    "allowedRootIds"
                ),
                allowedTypePrefixes: stringArray(
                    remote?["reflection"] as? NSDictionary,
                    "allowedTypePrefixes"
                )
            ),
            secureStorage: {
                let secure =
                    remote?["secureStorage"] as? NSDictionary
                    ?? dictionary["secureStorage"] as? NSDictionary
                return AnsightSecureStorageToolsOptions(
                    appleService: stringValue(secure, "appleService"),
                    allowedKeys: stringArray(secure, "allowedKeys"),
                    allowedKeyPrefixes:
                        stringArray(secure, "allowedKeyPrefixes")
                        + stringArray(secure, "allowedPrefixes")
                )
            }()
        )
    }

    private func toolDescriptor(_ dictionary: NSDictionary) throws
        -> AnsightToolDescriptor
    {
        AnsightToolDescriptor(
            id: stringValue(dictionary, "id") ?? "",
            name:
                stringValue(dictionary, "name")
                ?? stringValue(dictionary, "id")
                ?? "",
            description: stringValue(dictionary, "description") ?? "",
            category: stringValue(dictionary, "category") ?? "custom",
            scope: toolScope(stringValue(dictionary, "scope")).rawValue,
            keywords: keywords(dictionary["keywords"]),
            security: toolSecurity(dictionary["security"] as? NSDictionary),
            argumentsSchema: AnsightToolSchema(
                json: jsonValue(dictionary["argumentsSchema"]) ?? .object([:])
            ),
            resultSchema: AnsightToolSchema(
                json: jsonValue(dictionary["resultSchema"]) ?? .object([:])
            )
        )
    }

    private func snapshotDictionary() -> NSDictionary {
        let value = AnsightRuntime.shared.snapshot()
        var result: [String: Any] = [
            "initialized": value.initialized,
            "active": value.active,
            "sessionOpen": value.sessionOpen,
            "lifecycleState": value.lifecycleState.rawValue,
            "metricsRecorded": value.metricsRecorded,
            "eventsRecorded": value.eventsRecorded,
            "touchesRecorded": value.touchesCaptured,
            "touchesCaptured": value.touchesCaptured,
            "touchesSent": value.touchesSent,
            "registeredTools": value.registeredTools,
            "executableTools": value.executableTools,
            "connectionStatus": hostConnectionStatusDictionary(
                value.hostConnectionStatus
            ),
            "channels": value.channels.map(channelDictionary),
        ]
        if let current = value.currentScreen {
            result["currentScreen"] = [
                "name": current.name,
                "capturedAtUtc": current.capturedAtUtc,
                "details": current.details,
            ]
        }
        if let metric = value.lastMetric {
            result["lastMetric"] = metricDictionary(metric)
        }
        if let event = value.lastEvent {
            result["lastEvent"] = eventDictionary(event)
        }
        if let message = value.sessionMessage {
            result["sessionMessage"] = message
        }
        return result as NSDictionary
    }

    private func hostConnectionStatusDictionary(_ value: HostConnectionStatus)
        -> NSDictionary
    {
        [
            "isRuntimeActive": value.isRuntimeActive,
            "isConnected": value.isConnected,
            "connectionState": value.connectionState.rawValue,
            "hasCachedSession": value.hasCachedSession,
            "hasSavedConfig": value.hasSavedConfig,
            "hasBundledConfig": value.hasBundledConfig,
            "summaryKind": value.summaryKind.rawValue,
            "summaryMessage": value.summaryMessage,
        ]
    }

    private func hostConnectionCapabilitiesDictionary(
        _ value: HostConnectionCapabilities
    ) -> NSDictionary {
        [
            "canConnectUsingSavedConfig": value.canConnectUsingSavedConfig,
            "canConnectUsingBundledConfig": value.canConnectUsingBundledConfig,
            "canChooseConfigFile": value.canChooseConfigFile,
            "canScanConfigQrCode": value.canScanConfigQrCode,
            "canClearSavedConfigs": value.canClearSavedConfigs,
        ]
    }

    private func hostConnectionResultDictionary(_ value: HostConnectionResult)
        -> NSDictionary
    {
        var result: [String: Any] = [
            "success": value.success,
            "message": value.message,
            "kind": value.kind.rawValue,
            "source": value.source.rawValue,
        ]
        if let reason = value.reasonCode ?? value.openSession?.reasonCode {
            result["reasonCode"] = reason
        }
        if let session = value.openSession {
            result["accepted"] = session.accepted
            result["usedEmbeddedDeveloperPairing"] =
                session.usedEmbeddedDeveloperPairing
            addIfPresent(&result, "sessionId", session.sessionId)
            addIfPresent(&result, "configId", session.configId)
            addIfPresent(&result, "appId", session.appId)
            addIfPresent(&result, "resolvedHostAddress", session.resolvedHostAddress)
            addIfPresent(&result, "discoverySource", session.discoverySource)
            addIfPresent(&result, "hostId", session.hostId)
            addIfPresent(&result, "hostName", session.hostName)
        }
        return result as NSDictionary
    }

    private func openSessionResultDictionary(_ value: OpenSessionResult) -> NSDictionary {
        var result: [String: Any] = [
            "success": value.success,
            "message": value.message,
            "accepted": value.accepted,
            "usedEmbeddedDeveloperPairing": value.usedEmbeddedDeveloperPairing,
        ]
        addIfPresent(&result, "sessionId", value.sessionId)
        addIfPresent(&result, "configId", value.configId)
        addIfPresent(&result, "appId", value.appId)
        addIfPresent(&result, "resolvedHostAddress", value.resolvedHostAddress)
        addIfPresent(&result, "discoverySource", value.discoverySource)
        addIfPresent(&result, "reasonCode", value.reasonCode)
        addIfPresent(&result, "hostId", value.hostId)
        addIfPresent(&result, "hostName", value.hostName)
        return result as NSDictionary
    }

    private func operationResultDictionary(
        _ value: OperationResult,
        additions: [String: Any] = [:]
    ) -> NSDictionary {
        var result: [String: Any] = [
            "success": value.success,
            "message": value.message,
        ]
        additions.forEach { result[$0.key] = $0.value }
        return result as NSDictionary
    }

    private func optionsDictionary(_ value: AnsightOptions) -> NSDictionary {
        [
            "sampleFrequencyMilliseconds": value.sampleFrequencyMilliseconds,
            "retentionPeriodSeconds": value.retentionPeriodSeconds,
            "enableFramesPerSecond": value.enableFramesPerSecond,
            "enableBatteryLevel": value.enableBatteryLevel,
            "toolGuard": toolGuardName(value.toolGuard),
            "customProperties": value.customProperties,
            "additionalChannels": value.additionalChannels.map(channelDictionary),
        ]
    }

    private func channel(_ value: NSDictionary) -> AnsightChannel {
        AnsightChannel(
            id: intValue(value, "id", defaultValue: -1),
            name: stringValue(value, "name") ?? "",
            colorHex: stringValue(value, "colorHex"),
            unit: stringValue(value, "unit"),
            type: stringValue(value, "type") ?? "custom",
            source: stringValue(value, "source"),
            group: stringValue(value, "group"),
            kind: stringValue(value, "kind")
        )
    }

    private func channelDictionary(_ value: AnsightChannel) -> [String: Any] {
        var result: [String: Any] = [
            "id": value.id,
            "name": value.name,
            "type": value.type,
        ]
        addIfPresent(&result, "unit", value.unit)
        addIfPresent(&result, "colorHex", value.colorHex)
        addIfPresent(&result, "source", value.source)
        addIfPresent(&result, "group", value.group)
        addIfPresent(&result, "kind", value.kind)
        return result
    }

    private func metricDictionary(_ value: RecordedMetric) -> [String: Any] {
        [
            "value": value.value,
            "capturedAtUtc": value.capturedAtUtc,
            "capturedAtEpochMs": value.capturedAtEpochMs,
            "channel": value.channel,
            "sequence": value.sequence,
        ]
    }

    private func eventDictionary(_ value: RecordedEvent) -> [String: Any] {
        var result: [String: Any] = [
            "id": value.id,
            "label": value.label,
            "type": eventTypeName(value.type),
            "capturedAtUtc": value.capturedAtUtc,
            "capturedAtEpochMs": value.capturedAtEpochMs,
            "channel": value.channel,
            "sequence": value.sequence,
        ]
        addIfPresent(&result, "details", value.details)
        addIfPresent(&result, "externalId", value.externalId)
        return result
    }

    private func pairingOpenOptions(_ value: NSDictionary) -> PairingOpenOptions {
        PairingOpenOptions(
            clientName: stringValue(value, "clientName") ?? "Flutter",
            expectedAppId: stringValue(value, "expectedAppId"),
            hostAddressOverride: stringValue(value, "hostAddressOverride"),
            discoveryPort: optionalInt(value, "discoveryPort")
        )
    }

    private func screenCaptureOptions(_ value: NSDictionary)
        -> AnsightSessionJpegCaptureOptions
    {
        AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: intValue(
                value,
                "intervalMilliseconds",
                defaultValue: AnsightSessionJpegCaptureOptions.defaultIntervalMilliseconds
            ),
            quality: intValue(
                value,
                "quality",
                defaultValue: AnsightSessionJpegCaptureOptions.defaultQuality
            ),
            maxWidth:
                optionalInt(value, "maxWidth")
                ?? AnsightSessionJpegCaptureOptions.defaultMaxWidth,
            captureGpuBackedSurfaces: boolValue(
                value,
                "captureGpuBackedSurfaces",
                defaultValue:
                    AnsightSessionJpegCaptureOptions.defaultCaptureGpuBackedSurfaces
            )
        )
    }

    private func emitLogEvent(
        level: AnsightLogLevel,
        message: String,
        error: Error?
    ) {
        var payload: [String: Any] = [
            "level": level.rawValue,
            "message": message,
            "platform": "ios",
        ]
        if let error {
            payload["error"] = error.localizedDescription
        }
        DispatchQueue.main.async { [weak self] in
            guard let api = self?.dartApi,
                  let encoded = try? encodeObject(payload) else {
                return
            }
            api.onNativeEvent(name: "log", payloadJson: encoded) { _ in }
        }
    }
}

private enum AnsightFlutterError: LocalizedError {
    case invalidJson
    case unknownMethod(String)

    var errorDescription: String? {
        switch self {
        case .invalidJson:
            return "Ansight Flutter bridge received invalid JSON."
        case .unknownMethod(let method):
            return "Unknown Ansight Flutter method '\(method)'."
        }
    }
}

private extension NSLock {
    @discardableResult
    func withLock<T>(_ body: () -> T) -> T {
        lock()
        defer { unlock() }
        return body()
    }
}

private func decodeObject(_ json: String?) throws -> NSDictionary {
    guard let json, !json.isEmpty else {
        return NSDictionary()
    }
    guard let dictionary = try JSONSerialization.jsonObject(
        with: Data(json.utf8)
    ) as? NSDictionary else {
        throw AnsightFlutterError.invalidJson
    }
    return dictionary
}

private func encodeObject(_ value: Any) throws -> String {
    let data = try JSONSerialization.data(
        withJSONObject: value,
        options: [.sortedKeys]
    )
    guard let result = String(data: data, encoding: .utf8) else {
        throw AnsightFlutterError.invalidJson
    }
    return result
}

private func compactId(_ value: UUID) -> String {
    value.uuidString.replacingOccurrences(of: "-", with: "").lowercased()
}

private func addIfPresent(
    _ dictionary: inout [String: Any],
    _ key: String,
    _ value: Any?
) {
    if let value {
        dictionary[key] = value
    }
}

private func stringValue(_ dictionary: NSDictionary?, _ key: String) -> String? {
    guard let value = dictionary?[key], !(value is NSNull) else {
        return nil
    }
    let result = (value as? String ?? "\(value)")
        .trimmingCharacters(in: .whitespacesAndNewlines)
    return result.isEmpty ? nil : result
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

private func boolValue(
    _ dictionary: NSDictionary?,
    _ key: String,
    defaultValue: Bool
) -> Bool {
    dictionary?[key] as? Bool ?? defaultValue
}

private func intValue(
    _ dictionary: NSDictionary?,
    _ key: String,
    defaultValue: Int
) -> Int {
    (dictionary?[key] as? NSNumber)?.intValue ?? defaultValue
}

private func optionalInt(_ dictionary: NSDictionary?, _ key: String) -> Int? {
    (dictionary?[key] as? NSNumber)?.intValue
}

private func doubleValue(
    _ dictionary: NSDictionary?,
    _ key: String,
    defaultValue: Double
) -> Double {
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

private func groupedStringDictionary(
    _ dictionary: NSDictionary?
) -> [String: [String: String]] {
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

private func stringArray(_ dictionary: NSDictionary?, _ key: String) -> [String] {
    guard let array = dictionary?[key] as? [Any] else {
        return []
    }
    return array.compactMap {
        guard !($0 is NSNull) else {
            return nil
        }
        let value = ($0 as? String ?? "\($0)")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }
}

private func rootDictionaries(_ value: Any?) -> [NSDictionary] {
    (value as? [Any])?.compactMap { $0 as? NSDictionary } ?? []
}

private func toolSuiteEnabled(_ value: Any?, defaultValue: Bool) -> Bool {
    if let value = value as? Bool {
        return value
    }
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
    if value == .readOnly { return "readOnly" }
    if value == .readWrite { return "readWrite" }
    if value == .fullAccess { return "fullAccess" }
    return "disabled"
}

private func toolScope(_ value: String?) -> AnsightToolScope {
    switch value?.lowercased() {
    case "write": return .write
    case "delete": return .delete
    default: return .read
    }
}

private func toolSecurity(_ value: NSDictionary?) -> AnsightToolSecurity {
    guard let value else {
        return .unspecified
    }
    let level: AnsightToolSecurityLevel
    switch stringValue(value, "level")?.lowercased() {
    case "low": level = .low
    case "medium", "moderate": level = .moderate
    case "high": level = .high
    case "critical": level = .critical
    default: level = .unspecified
    }
    return AnsightToolSecurity(
        level: level,
        summary: stringValue(value, "summary") ?? "",
        implications: stringArray(value, "implications")
    )
}

private func keywords(_ value: Any?) -> String {
    if let value = value as? String {
        return value
    }
    if let value = value as? [Any] {
        return value.compactMap { $0 as? String }.joined(separator: " ")
    }
    return "flutter dart custom tool"
}

private func jsonValue(_ value: Any?) -> JSONValue? {
    guard let value, !(value is NSNull) else {
        return nil
    }
    if let dictionary = value as? NSDictionary {
        var result: [String: JSONValue] = [:]
        for (key, value) in dictionary {
            guard let key = key as? String, let converted = jsonValue(value) else {
                continue
            }
            result[key] = converted
        }
        return .object(result)
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
        return double.rounded() == double
            ? .integer(number.int64Value)
            : .number(double)
    }
    return .string("\(value)")
}
