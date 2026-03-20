import AnsightKit
import Flutter
import UIKit

public final class AnsightFlutterPlugin: NSObject, FlutterPlugin {
    public static func register(with registrar: FlutterPluginRegistrar) {
        let channel = FlutterMethodChannel(name: "ansight_flutter", binaryMessenger: registrar.messenger())
        let instance = AnsightFlutterPlugin()
        registrar.addMethodCallDelegate(instance, channel: channel)
    }

    public func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
        do {
            switch call.method {
            case "initialize":
                let options = (call.arguments as? [String: Any] ?? [:]).toOptions()
                try AnsightRuntime.shared.initialize(options: options)
                result(nil)
            case "activate":
                try AnsightRuntime.shared.activate()
                result(nil)
            case "deactivate":
                AnsightRuntime.shared.deactivate()
                result(nil)
            case "clear":
                AnsightRuntime.shared.clear()
                result(nil)
            case "metric":
                let args = call.arguments as? [String: Any] ?? [:]
                try AnsightRuntime.shared.metric(
                    Int64((args["value"] as? String) ?? "0") ?? 0,
                    channel: (args["channel"] as? NSNumber)?.intValue ?? AnsightChannels.unspecified
                )
                result(nil)
            case "event":
                let args = call.arguments as? [String: Any] ?? [:]
                let type = (args["type"] as? String).flatMap(AnsightEventType.init(rawValue:)) ?? .info
                try AnsightRuntime.shared.event(
                    args["label"] as? String ?? "",
                    type: type,
                    details: args["details"] as? String,
                    channel: (args["channel"] as? NSNumber)?.intValue ?? AnsightChannels.unspecified,
                    id: args["id"] as? String ?? UUID().uuidString
                )
                result(nil)
            case "openSession":
                let args = call.arguments as? [String: Any] ?? [:]
                let options = args["options"] as? [String: Any] ?? [:]
                let session = try AnsightRuntime.shared.openSession(
                    pairingJson: args["pairingJson"] as? String ?? "",
                    options: PairingOpenOptions(
                        clientName: options["clientName"] as? String ?? "",
                        manualHostAddress: options["manualHostAddress"] as? String ?? "",
                        expectedAppId: options["expectedAppId"] as? String,
                        profileOverride: options["profileOverride"] as? [String: String] ?? [:],
                        allowDiscoveryHintHostFallback: (options["allowDiscoveryHintHostFallback"] as? NSNumber)?.boolValue ?? true
                    )
                )
                result([
                    "success": session.success,
                    "message": session.message,
                    "sessionId": session.sessionId as Any,
                    "configId": session.configId as Any,
                    "appId": session.appId as Any,
                    "resolvedHostAddress": session.resolvedHostAddress as Any,
                    "usedEmbeddedDeveloperPairing": session.usedEmbeddedDeveloperPairing,
                    "discoverySource": session.discoverySource as Any,
                ])
            case "completeSession":
                AnsightRuntime.shared.completeSession()
                result(nil)
            case "closeSession":
                AnsightRuntime.shared.closeSession()
                result(nil)
            case "registerTool":
                let args = call.arguments as? [String: Any] ?? [:]
                try AnsightRuntime.shared.registerTool(
                    AnsightToolDescriptor(
                        id: args["id"] as? String ?? "",
                        name: args["name"] as? String ?? "",
                        scope: args["scope"] as? String ?? "Read"
                    )
                )
                result(nil)
            case "getDebugSnapshot":
                let snapshot = AnsightRuntime.shared.snapshot()
                result([
                    "initialized": snapshot.initialized,
                    "active": snapshot.active,
                    "sessionOpen": snapshot.sessionOpen,
                    "metricsRecorded": snapshot.metricsRecorded,
                    "eventsRecorded": snapshot.eventsRecorded,
                    "registeredTools": snapshot.registeredTools,
                    "executableTools": snapshot.executableTools,
                    "toolDiscoveryEnabled": snapshot.toolDiscoveryEnabled,
                    "toolExecutionEnabled": snapshot.toolExecutionEnabled,
                    "embeddedDeveloperPairingAvailable": snapshot.embeddedDeveloperPairingAvailable,
                    "detectedBundledTools": snapshot.detectedBundledTools,
                    "sessionMessage": snapshot.sessionMessage as Any,
                    "lastPairingConfigId": snapshot.lastPairingConfigId as Any,
                    "resolvedHostAddress": snapshot.resolvedHostAddress as Any,
                    "lastMetric": snapshot.lastMetric.map {
                        [
                            "value": $0.value,
                            "channel": $0.channel,
                            "capturedAtEpochMs": $0.capturedAtEpochMs,
                        ]
                    } as Any,
                    "lastEvent": snapshot.lastEvent.map {
                        [
                            "id": $0.id,
                            "label": $0.label,
                            "type": $0.type.rawValue,
                            "details": $0.details as Any,
                            "channel": $0.channel,
                            "capturedAtEpochMs": $0.capturedAtEpochMs,
                        ]
                    } as Any,
                ])
            default:
                result(FlutterMethodNotImplemented)
            }
        } catch {
            result(
                FlutterError(
                    code: "ansight_flutter_error",
                    message: error.localizedDescription,
                    details: nil
                )
            )
        }
    }
}

private extension Dictionary where Key == String, Value == Any {
    func toOptions() -> AnsightOptions {
        let channels = (self["additionalChannels"] as? [[String: Any]] ?? []).compactMap { raw -> AnsightChannel? in
            guard let id = raw["id"] as? NSNumber, let name = raw["name"] as? String else {
                return nil
            }

            return AnsightChannel(id: id.intValue, name: name, colorHex: raw["colorHex"] as? String)
        }

        return AnsightOptions(
            sampleFrequencyMilliseconds: (self["sampleFrequencyMilliseconds"] as? NSNumber)?.intValue ?? 500,
            retentionPeriodSeconds: (self["retentionPeriodSeconds"] as? NSNumber)?.intValue ?? 600,
            enableFramesPerSecond: (self["enableFramesPerSecond"] as? NSNumber)?.boolValue ?? true,
            additionalChannels: channels,
            toolGuard: (self["toolAccess"] as? String).map { rawValue in
                switch rawValue.lowercased() {
                case "readonly", "read":
                    return .readOnly
                case "all", "full":
                    return .fullAccess
                default:
                    return .disabled
                }
            } ?? .disabled
        )
    }
}
