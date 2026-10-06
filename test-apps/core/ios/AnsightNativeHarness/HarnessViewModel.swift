import Ansight
import Foundation
import SwiftUI

@MainActor
final class HarnessViewModel: ObservableObject {
    @Published var snapshot = AnsightRuntime.shared.snapshot()
    @Published var connectionMessage = ""
    @Published var isBusy = false
    @Published var keyboardText = ""
    @Published var pickerValue = "Express"
    @Published var expeditedBilling = true
    @Published var quantity = 2.0
    @Published var seededAtUtc = AnsightClock.isoNow()
    @Published var selectedTab = HarnessTab.dashboard
    @Published var activeModal = "<none>"
    @Published var flyoutSelection = "Overview"
    @Published var pushDepth = 0
    @Published var sceneMaterial = "Teal"
    @Published var sceneRotationEnabled = true
    @Published var sceneSpinSpeed = 1.0
    @Published var selectedSceneNode = "<none>"
    @Published var databaseRowCount = 0
    @Published var navigationEvents: [String] = []

    let shippingSpeeds = ["Standard", "Express", "Priority", "Overnight"]
    let sceneMaterials = ["Teal", "Graphite", "Plum", "Safety Orange"]
    let flyoutItems = ["Overview", "Orders", "Inventory", "Diagnostics", "Settings"]

    let inspectionStore = HarnessInspectionStore()
    var hasBootstrapped = false
    var metricCounter: Int64 = 0
    var customToolsRegistered = false

    var statusText: String {
        if snapshot.sessionOpen, let sessionMessage = snapshot.sessionMessage {
            return sessionMessage
        }

        if !connectionMessage.isEmpty {
            return connectionMessage
        }

        return snapshot.sessionMessage ?? "Ready"
    }

    func runAsync(_ operation: @MainActor @escaping () async -> Void) {
        isBusy = true
        Task { @MainActor in
            await operation()
            isBusy = false
            refresh()
        }
    }
}
