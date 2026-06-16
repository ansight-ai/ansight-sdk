import Ansight
import SwiftUI

struct HarnessTelemetrySectionView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessSection("Telemetry", systemImage: "waveform.path.ecg") {
            VStack(alignment: .leading, spacing: 12) {
                LazyVGrid(
                    columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)],
                    spacing: 8
                ) {
                    HarnessMetricTile(
                        "Metrics",
                        value: "\(harness.snapshot.metricsRecorded)",
                        systemImage: "waveform.path.ecg",
                        tint: .green
                    )
                    HarnessMetricTile(
                        "Events",
                        value: "\(harness.snapshot.eventsRecorded)",
                        systemImage: "flag",
                        tint: .blue
                    )
                    HarnessMetricTile(
                        "Screen",
                        value: harness.snapshot.screenCaptureActive ? "On" : "Off",
                        systemImage: "rectangle.on.rectangle",
                        tint: harness.snapshot.screenCaptureActive ? .green : .secondary
                    )
                    HarnessMetricTile(
                        "Touch",
                        value: harness.snapshot.touchCaptureStreamingActive ? "Live" : "Idle",
                        systemImage: "hand.tap",
                        tint: harness.snapshot.touchCaptureStreamingActive ? .orange : .secondary
                    )
                }

                HarnessActionGrid {
                    HarnessActionButton("Metric", systemImage: "waveform.path.ecg", isBusy: harness.isBusy) {
                        harness.recordMetric()
                    }
                    HarnessActionButton("Event", systemImage: "flag", isBusy: harness.isBusy) {
                        harness.recordEvent()
                    }
                    HarnessActionButton("Screen", systemImage: "rectangle.on.rectangle", isBusy: harness.isBusy) {
                        harness.recordScreen("Harness Manual Screen")
                    }
                    HarnessActionButton("Foreground", systemImage: "sun.max", isBusy: harness.isBusy) {
                        harness.setLifecycle(.foreground)
                    }
                    HarnessActionButton("Background", systemImage: "moon", isBusy: harness.isBusy) {
                        harness.setLifecycle(.background)
                    }
                    HarnessActionButton("Capture Frame", systemImage: "camera.viewfinder", isBusy: harness.isBusy) {
                        harness.runAsync {
                            await harness.captureScreenFrame()
                        }
                    }
                    HarnessActionButton("Enable Touches", systemImage: "hand.tap", isBusy: harness.isBusy) {
                        harness.enableTouchCapture()
                    }
                    HarnessActionButton("Disable Touches", systemImage: "hand.raised", isBusy: harness.isBusy) {
                        harness.disableTouchCapture()
                    }
                    HarnessActionButton("Clear Buffers", systemImage: "eraser", role: .destructive, isBusy: harness.isBusy) {
                        harness.clearRuntimeBuffers()
                    }
                }
            }
        }
    }
}
