import SwiftUI

struct HarnessDashboardView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("Harness") {
            HarnessDashboardHeaderView(harness: harness)
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
