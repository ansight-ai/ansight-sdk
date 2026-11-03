import SwiftUI

struct HarnessDashboardView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("Harness") {
            HarnessDashboardHeaderView(harness: harness)
            if harness.offlineUploadEnabled {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Offline capture upload").font(.headline)
                    Text(harness.offlineUploadMessage).accessibilityIdentifier("offline.upload.status")
                    ForEach(harness.offlineCloudSessionIds, id: \.self) { sessionId in
                        Text(sessionId).font(.caption.monospaced()).textSelection(.enabled)
                    }
                    Button("Stop recording and finish upload") { harness.stopOfflineCapture() }
                        .accessibilityIdentifier("offline.upload.stop")
                    Button("Retry pending uploads") { harness.runAsync { await harness.uploadOfflineChunks() } }
                        .accessibilityIdentifier("offline.upload.retry")
                }
            }
            HarnessActionButton("Annotate", systemImage: "pencil.tip", isBusy: harness.isBusy) {
                harness.runAsync {
                    await harness.presentAnnotation()
                }
            }
            .accessibilityIdentifier("annotation.present")
            if harness.connectionMessage.hasPrefix("Annotation ") {
                Text(harness.connectionMessage)
                    .accessibilityIdentifier("annotation.result")
            }
            HarnessPairingSectionView(harness: harness)
            HarnessTelemetrySectionView(harness: harness)
            HarnessNativeUISectionView(harness: harness)
            HarnessSeededDataSectionView(harness: harness)
        }
    }
}
