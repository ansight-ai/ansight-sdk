import Ansight
import Foundation

final class HarnessInspectionStore: @unchecked Sendable {
    private let lock = NSLock()
    private var currentState = HarnessInspectionState.initial(snapshot: AnsightRuntime.shared.snapshot())

    func update(_ state: HarnessInspectionState) {
        lock.lock()
        currentState = state
        lock.unlock()
    }

    func snapshotJSON() -> JSONValue {
        let state = snapshot()
        return .object([
            "connection": .object([
                "message": .string(state.connectionMessage),
                "state": .string(state.runtimeSnapshot.hostConnectionStatus.connectionState.rawValue),
                "summary": .string(state.runtimeSnapshot.hostConnectionStatus.summaryMessage),
                "sessionOpen": .bool(state.runtimeSnapshot.sessionOpen),
                "lastPairingConfigId": .string(state.runtimeSnapshot.lastPairingConfigId ?? ""),
                "resolvedHostAddress": .string(state.runtimeSnapshot.resolvedHostAddress ?? ""),
            ]),
            "ui": .object([
                "selectedTab": .string(state.selectedTab.rawValue),
                "keyboardText": .string(state.keyboardText),
                "pickerValue": .string(state.pickerValue),
                "expeditedBilling": .bool(state.expeditedBilling),
                "quantity": .integer(Int64(state.quantity)),
                "activeModal": .string(state.activeModal),
                "flyoutSelection": .string(state.flyoutSelection),
                "pushDepth": .integer(Int64(state.pushDepth)),
            ]),
            "scene": sceneJSON(state),
            "data": dataJSON(state),
            "telemetry": telemetryJSON(state),
            "navigationEvents": .array(state.navigationEvents.map(JSONValue.string)),
        ])
    }

    func rootsJSON() -> JSONValue {
        .array(rootDescriptors.map { descriptor in
            .object([
                "rootId": .string(descriptor.id),
                "name": .string(descriptor.name),
                "kind": .string(descriptor.kind),
                "description": .string(descriptor.description),
                "hostRuntime": hostRuntimeJSON(descriptor.hostRuntime),
            ])
        })
    }

    func inspectRootJSON(rootId: String) -> JSONValue? {
        let normalizedId = rootId.trimmingCharacters(in: .whitespacesAndNewlines)
        let state = snapshot()
        switch normalizedId {
        case "ui.orderDraft":
            return .object([
                "rootId": .string(normalizedId),
                "keyboardText": .string(state.keyboardText),
                "shippingSpeed": .string(state.pickerValue),
                "expeditedBilling": .bool(state.expeditedBilling),
                "quantity": .integer(Int64(state.quantity)),
            ])
        case "navigation.flow":
            return .object([
                "rootId": .string(normalizedId),
                "selectedTab": .string(state.selectedTab.rawValue),
                "activeModal": .string(state.activeModal),
                "flyoutSelection": .string(state.flyoutSelection),
                "pushDepth": .integer(Int64(state.pushDepth)),
                "events": .array(state.navigationEvents.map(JSONValue.string)),
            ])
        case "scene.inline3d":
            return sceneJSON(state)
        case "data.seededStore":
            return dataJSON(state)
        case "runtime.snapshot":
            return telemetryJSON(state)
        default:
            return nil
        }
    }

    private func snapshot() -> HarnessInspectionState {
        lock.lock()
        let value = currentState
        lock.unlock()
        return value
    }

    private func sceneJSON(_ state: HarnessInspectionState) -> JSONValue {
        .object([
            "rootId": .string("scene.inline3d"),
            "material": .string(state.sceneMaterial),
            "rotationEnabled": .bool(state.sceneRotationEnabled),
            "spinSpeed": .number(state.sceneSpinSpeed),
            "selectedNode": .string(state.selectedSceneNode),
        ])
    }

    private func dataJSON(_ state: HarnessInspectionState) -> JSONValue {
        .object([
            "rootId": .string("data.seededStore"),
            "seededAtUtc": .string(state.seededAtUtc),
            "documentsRoot": .string(state.documentsRoot),
            "databasePath": .string(state.databasePath),
            "databaseRowCount": .integer(Int64(state.databaseRowCount)),
            "preferencePrefix": .string(HarnessConstants.preferencePrefix),
            "secureStorageService": .string(HarnessConstants.secureStorageService),
        ])
    }

    private func telemetryJSON(_ state: HarnessInspectionState) -> JSONValue {
        let snapshot = state.runtimeSnapshot
        return .object([
            "rootId": .string("runtime.snapshot"),
            "initialized": .bool(snapshot.initialized),
            "active": .bool(snapshot.active),
            "registeredTools": .integer(Int64(snapshot.registeredTools)),
            "executableTools": .integer(Int64(snapshot.executableTools)),
            "metricsRecorded": .integer(Int64(snapshot.metricsRecorded)),
            "eventsRecorded": .integer(Int64(snapshot.eventsRecorded)),
            "screenFramesCaptured": .integer(Int64(snapshot.screenFramesCaptured)),
            "screenFramesSent": .integer(Int64(snapshot.screenFramesSent)),
            "touchesCaptured": .integer(Int64(snapshot.touchesCaptured)),
            "touchesSent": .integer(Int64(snapshot.touchesSent)),
            "lastFrameRate": snapshot.lastFrameRate.map { .number(Double($0)) } ?? .null,
        ])
    }

    private func hostRuntimeJSON(_ descriptor: HarnessReflectionHostRuntimeDescriptor) -> JSONValue {
        .object([
            "kind": .string(descriptor.kind),
            "displayName": .string(descriptor.displayName),
            "platform": .string(descriptor.platform),
            "engine": .string(descriptor.engine),
        ])
    }

    private var rootDescriptors: [HarnessReflectionRootDescriptor] {
        [
            HarnessReflectionRootDescriptor(
                id: "ui.orderDraft",
                name: "Order Draft UI State",
                kind: "swiftui-state",
                description: "Bound form state for text input, picker overlay, toggle, and slider controls.",
                hostRuntime: .nativeSwift
            ),
            HarnessReflectionRootDescriptor(
                id: "navigation.flow",
                name: "Navigation Flow State",
                kind: "navigation",
                description: "Current tab, pushed depth, modal state, flyout selection, and recent navigation events.",
                hostRuntime: .nativeSwift
            ),
            HarnessReflectionRootDescriptor(
                id: "scene.inline3d",
                name: "Inline 3D Scene",
                kind: "scenekit",
                description: "Scene material, rotation settings, and selected SceneKit node.",
                hostRuntime: .nativeSwift
            ),
            HarnessReflectionRootDescriptor(
                id: "data.seededStore",
                name: "Seeded Data Store",
                kind: "storage",
                description: "Harness file, database, preferences, and secure storage seed metadata.",
                hostRuntime: .nativeSwift
            ),
            HarnessReflectionRootDescriptor(
                id: "runtime.snapshot",
                name: "Ansight Runtime Snapshot",
                kind: "runtime",
                description: "Current Ansight runtime counters and capture status.",
                hostRuntime: .nativeSwift
            ),
        ]
    }
}
