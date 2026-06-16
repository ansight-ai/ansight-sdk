import Ansight
import Foundation

final class HarnessStateSnapshotTool: AnsightTool, @unchecked Sendable {
    private let store: HarnessInspectionStore

    init(store: HarnessInspectionStore) {
        self.store = store
    }

    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: "harness.state.snapshot",
            name: "Get Harness State Snapshot",
            description: "Returns custom state for the iOS native harness, including UI, navigation, SceneKit, data, and runtime counters.",
            category: "Harness",
            scope: AnsightToolScope.read.rawValue,
            keywords: "harness custom state inspect swiftui navigation scenekit database",
            security: AnsightToolSecurity(
                level: .low,
                summary: "Inspects test harness diagnostic state.",
                implications: [
                    AnsightToolSecurityImplications.inspectsRuntimeState,
                    AnsightToolSecurityImplications.metadataDisclosure,
                ]
            ),
            resultSchema: HarnessToolSchemas.stateSnapshotResult
        )
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .success(store.snapshotJSON(), message: "Harness state snapshot captured.")
    }
}
