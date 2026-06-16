import SwiftUI

struct HarnessRuntimeSnapshotView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("Snapshot") {
            LazyVGrid(
                columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)],
                spacing: 8
            ) {
                HarnessMetricTile(
                    "Tools",
                    value: "\(harness.snapshot.registeredTools)",
                    systemImage: "wrench.and.screwdriver",
                    tint: .blue
                )
                HarnessMetricTile(
                    "Executable",
                    value: "\(harness.snapshot.executableTools)",
                    systemImage: "terminal",
                    tint: .green
                )
                HarnessMetricTile(
                    "Lifecycle",
                    value: harness.snapshot.lifecycleState.rawValue,
                    systemImage: "app.badge",
                    tint: .orange
                )
                HarnessMetricTile(
                    "FPS Active",
                    value: harness.snapshot.frameRateCaptureActive ? "Yes" : "No",
                    systemImage: "speedometer",
                    tint: harness.snapshot.frameRateCaptureActive ? .green : .secondary
                )
            }

            HarnessSection("Actions", systemImage: "bolt.horizontal.circle") {
                HarnessActionGrid {
                    HarnessActionButton("Refresh", systemImage: "arrow.clockwise", isBusy: harness.isBusy) {
                        harness.recordEvent(label: "ios_harness_snapshot_refresh")
                    }
                    HarnessActionButton("Capture Frame", systemImage: "camera.viewfinder", isBusy: harness.isBusy) {
                        harness.runAsync {
                            await harness.captureScreenFrame()
                        }
                    }
                    HarnessActionButton("Record Metric", systemImage: "waveform.path.ecg", isBusy: harness.isBusy) {
                        harness.recordMetric()
                    }
                }
            }

            HarnessSection("Runtime Snapshot", systemImage: "doc.text.magnifyingglass") {
                HarnessMonospacedBlock(harness.debugText)
            }
        }
    }
}
