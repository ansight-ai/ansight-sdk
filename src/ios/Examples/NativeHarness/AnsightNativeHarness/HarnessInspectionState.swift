import Ansight
import Foundation

struct HarnessInspectionState {
    var connectionMessage: String
    var selectedTab: HarnessTab
    var keyboardText: String
    var pickerValue: String
    var expeditedBilling: Bool
    var quantity: Int
    var activeModal: String
    var flyoutSelection: String
    var pushDepth: Int
    var sceneMaterial: String
    var sceneRotationEnabled: Bool
    var sceneSpinSpeed: Double
    var selectedSceneNode: String
    var seededAtUtc: String
    var documentsRoot: String
    var databasePath: String
    var databaseRowCount: Int
    var navigationEvents: [String]
    var runtimeSnapshot: AnsightDebugSnapshot

    static func initial(snapshot: AnsightDebugSnapshot) -> HarnessInspectionState {
        HarnessInspectionState(
            connectionMessage: "",
            selectedTab: .dashboard,
            keyboardText: "",
            pickerValue: "Express",
            expeditedBilling: true,
            quantity: 2,
            activeModal: "<none>",
            flyoutSelection: "Overview",
            pushDepth: 0,
            sceneMaterial: "Teal",
            sceneRotationEnabled: true,
            sceneSpinSpeed: 1,
            selectedSceneNode: "<none>",
            seededAtUtc: AnsightClock.isoNow(),
            documentsRoot: "<unresolved>",
            databasePath: "<unresolved>",
            databaseRowCount: 0,
            navigationEvents: [],
            runtimeSnapshot: snapshot
        )
    }
}
