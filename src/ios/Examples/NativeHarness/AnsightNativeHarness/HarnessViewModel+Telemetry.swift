import Ansight
import Foundation

extension HarnessViewModel {
    func captureScreenFrame() async {
        let result = await AnsightRuntime.shared.captureScreenFrame()
        connectionMessage = result.message
        refresh()
    }

    func recordMetric() {
        metricCounter += 1
        let value = Int64(Date().timeIntervalSince1970 * 1000) + metricCounter
        do {
            try AnsightRuntime.shared.metric(value, channel: HarnessConstants.customChannel)
            connectionMessage = "Recorded harness metric \(value)."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func recordEvent(label: String = "ios_harness_tapped") {
        do {
            try AnsightRuntime.shared.event(
                label,
                type: .navigation,
                details: "source=native-harness;tab=\(selectedTab.rawValue);picker=\(pickerValue);keyboard=\(keyboardText)",
                channel: HarnessConstants.customChannel
            )
            connectionMessage = "Recorded harness event."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func recordScreen(_ name: String) {
        do {
            try AnsightRuntime.shared.screenViewed(
                name,
                details: [
                    "route": "/ios/native-harness/\(selectedTab.rawValue)",
                    "picker": pickerValue,
                    "quantity": String(Int(quantity)),
                ]
            )
            connectionMessage = "Recorded screen \(name)."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func setLifecycle(_ state: AppLifecycleState) {
        AnsightRuntime.shared.setAppLifecycleState(state)
        connectionMessage = "Lifecycle state set to \(state.rawValue)."
        refresh()
    }

    func enableTouchCapture() {
        AnsightRuntime.shared.enableTouchCapture()
        connectionMessage = "Touch capture enabled."
        refresh()
    }

    func disableTouchCapture() {
        AnsightRuntime.shared.disableTouchCapture()
        connectionMessage = "Touch capture disabled."
        refresh()
    }

    func clearRuntimeBuffers() {
        AnsightRuntime.shared.clear()
        connectionMessage = "Runtime buffers cleared."
        refresh()
    }
}
