import AnsightKit
import Foundation
import React

@objc(AnsightBridgeModule)
final class AnsightBridgeModule: NSObject {
    @objc
    static func requiresMainQueueSetup() -> Bool {
        false
    }

    @objc(initialize:resolver:rejecter:)
    func initialize(
        _ options: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.initialize(options: options.toOptions())
            resolve(nil)
        } catch {
            reject("ansight_initialize_failed", error.localizedDescription, error)
        }
    }

    @objc(activate:rejecter:)
    func activate(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.activate()
            resolve(nil)
        } catch {
            reject("ansight_activate_failed", error.localizedDescription, error)
        }
    }

    @objc(deactivate:rejecter:)
    func deactivate(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        AnsightRuntime.shared.deactivate()
        resolve(nil)
    }

    @objc(clear:rejecter:)
    func clear(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        AnsightRuntime.shared.clear()
        resolve(nil)
    }

    @objc(metric:channel:resolver:rejecter:)
    func metric(
        _ value: String,
        channel: NSNumber?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.metric(Int64(value) ?? 0, channel: channel?.intValue ?? AnsightChannels.unspecified)
            resolve(nil)
        } catch {
            reject("ansight_metric_failed", error.localizedDescription, error)
        }
    }

    @objc(event:options:resolver:rejecter:)
    func event(
        _ label: String,
        options: NSDictionary?,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            let type = (options?["type"] as? String).flatMap(AnsightEventType.init(rawValue:)) ?? .info
            try AnsightRuntime.shared.event(
                label,
                type: type,
                details: options?["details"] as? String,
                channel: (options?["channel"] as? NSNumber)?.intValue ?? AnsightChannels.unspecified,
                id: options?["id"] as? String ?? UUID().uuidString
            )
            resolve(nil)
        } catch {
            reject("ansight_event_failed", error.localizedDescription, error)
        }
    }

    @objc(openSession:options:resolver:rejecter:)
    func openSession(
        _ pairingJson: String,
        options: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            let result = try AnsightRuntime.shared.openSession(
                pairingJson: pairingJson,
                options: PairingOpenOptions(
                    clientName: options["clientName"] as? String ?? "",
                    expectedAppId: options["expectedAppId"] as? String,
                    profileOverride: options["profileOverride"] as? [String: String] ?? [:]
                )
            )

            resolve([
                "success": result.success,
                "message": result.message,
                "sessionId": result.sessionId as Any,
                "configId": result.configId as Any,
                "appId": result.appId as Any,
                "resolvedHostAddress": result.resolvedHostAddress as Any,
                "usedEmbeddedDeveloperPairing": result.usedEmbeddedDeveloperPairing,
                "discoverySource": result.discoverySource as Any,
            ])
        } catch {
            reject("ansight_open_session_failed", error.localizedDescription, error)
        }
    }

    @objc(completeSession:rejecter:)
    func completeSession(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        AnsightRuntime.shared.completeSession()
        resolve(nil)
    }

    @objc(closeSession:rejecter:)
    func closeSession(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        AnsightRuntime.shared.closeSession()
        resolve(nil)
    }

    @objc(registerTool:resolver:rejecter:)
    func registerTool(
        _ tool: NSDictionary,
        resolver resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        do {
            try AnsightRuntime.shared.registerTool(
                AnsightToolDescriptor(
                    id: tool["id"] as? String ?? "",
                    name: tool["name"] as? String ?? "",
                    scope: tool["scope"] as? String ?? "Read"
                )
            )
            resolve(nil)
        } catch {
            reject("ansight_register_tool_failed", error.localizedDescription, error)
        }
    }

    @objc(getDebugSnapshot:rejecter:)
    func getDebugSnapshot(
        _ resolve: RCTPromiseResolveBlock,
        rejecter reject: RCTPromiseRejectBlock
    ) {
        let snapshot = AnsightRuntime.shared.snapshot()
        resolve([
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
    }
}

private extension NSDictionary? {
    func toOptions() -> AnsightOptions {
        guard let options = self else {
            return AnsightOptions()
        }

        let channels = (options["additionalChannels"] as? [[String: Any]] ?? []).compactMap { raw in
            guard let id = raw["id"] as? NSNumber, let name = raw["name"] as? String else {
                return nil
            }

            return AnsightChannel(id: id.intValue, name: name, colorHex: raw["colorHex"] as? String)
        }

        return AnsightOptions(
            sampleFrequencyMilliseconds: (options["sampleFrequencyMilliseconds"] as? NSNumber)?.intValue ?? 500,
            retentionPeriodSeconds: (options["retentionPeriodSeconds"] as? NSNumber)?.intValue ?? 600,
            enableFramesPerSecond: (options["enableFramesPerSecond"] as? NSNumber)?.boolValue ?? true,
            additionalChannels: channels,
            toolGuard: (options["toolAccess"] as? String).map { rawValue in
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
