import Ansight
import AnsightMotion
import CoreMotion
import SwiftUI
import UIKit

@main
struct AnsightMotionTestApp: App {
    var body: some Scene {
        WindowGroup { MotionTestView() }
    }
}

@MainActor
private final class MotionTestModel: ObservableObject {
    @Published var status = "Starting Ansight"
    @Published var sampleCount = 0
    @Published var shakeCount = 0
    @Published var sampling = false

    var shakeStatus: String { shakeCount > 0 ? "Shake detected" : "Shake waiting" }

    private let motionManager = CMMotionManager()

    func start() {
        guard status == "Starting Ansight" else { return }
        do {
            let options = try AnsightOptionsBuilder(.ansightDeveloperDefaults)
                .withMotionCapture()
                .withUnattendedProvisioning()
                .build()
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)
            status = "Ansight active"
        } catch {
            status = "Ansight error: \(error.localizedDescription)"
        }
    }

    func recordShake(source: String) {
        AnsightMotion.recordShake(source: source)
        shakeCount += 1
    }

    func recordManualSample() {
        AnsightMotion.recordAccelerometer(x: 1.5, y: -2.0, z: 9.80665)
        sampleCount += 1
    }

    func reset() {
        sampleCount = 0
        shakeCount = 0
    }

    func startSampling() {
        guard motionManager.isAccelerometerAvailable else {
            status = "Accelerometer unavailable on this device"
            return
        }
        motionManager.accelerometerUpdateInterval = 0.05
        motionManager.startAccelerometerUpdates(to: .main) { [weak self] data, error in
            guard let self, let data, error == nil else { return }
            let acceleration = data.acceleration
            AnsightMotion.recordAccelerometer(
                x: acceleration.x * 9.80665,
                y: acceleration.y * 9.80665,
                z: acceleration.z * 9.80665
            )
            self.sampleCount += 1
        }
        sampling = true
        status = "Accelerometer sampling"
    }

    func stopSampling() {
        motionManager.stopAccelerometerUpdates()
        sampling = false
        status = "Accelerometer stopped"
    }
}

private struct MotionTestView: View {
    @StateObject private var model = MotionTestModel()

    var body: some View {
        VStack(spacing: 18) {
            Text("Motion capture test").font(.title)
            Text(model.status).accessibilityIdentifier("motion.status")
            Text("Samples: \(model.sampleCount)").accessibilityIdentifier("motion.samples")
            Text("Shakes: \(model.shakeCount)").accessibilityIdentifier("motion.shakes")
            Text(model.shakeStatus).accessibilityIdentifier("motion.shake-status")
            Button("Start accelerometer") { model.startSampling() }
                .accessibilityIdentifier("motion.enable")
            Button("Stop accelerometer") { model.stopSampling() }
                .accessibilityIdentifier("motion.disable")
            Button("Record sample manually") { model.recordManualSample() }
                .accessibilityIdentifier("motion.manual-sample")
            Button("Record shake manually") { model.recordShake(source: "test-button") }
                .accessibilityIdentifier("motion.manual-shake")
            Button("Reset counters") { model.reset() }
                .accessibilityIdentifier("motion.reset")
        }
        .padding()
        .background(MotionShakeResponder { model.recordShake(source: "uikit") })
        .onAppear { model.start() }
    }
}

private struct MotionShakeResponder: UIViewRepresentable {
    let onShake: @MainActor () -> Void

    func makeUIView(context: Context) -> ShakeView {
        let view = ShakeView()
        view.onShake = onShake
        return view
    }

    func updateUIView(_ view: ShakeView, context: Context) {
        view.onShake = onShake
    }

    final class ShakeView: UIView {
        var onShake: (@MainActor () -> Void)?
        override var canBecomeFirstResponder: Bool { true }

        override func didMoveToWindow() {
            super.didMoveToWindow()
            if window != nil { becomeFirstResponder() }
        }

        override func motionEnded(_ motion: UIEvent.EventSubtype, with event: UIEvent?) {
            if motion == .motionShake { onShake?() }
            super.motionEnded(motion, with: event)
        }
    }
}
