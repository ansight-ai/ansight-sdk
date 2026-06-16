import SwiftUI

struct HarnessDashboardView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("Harness") {
            HarnessDashboardHeaderView(harness: harness)
            HarnessPairingSectionView(harness: harness)
            HarnessTelemetrySectionView(harness: harness)
            HarnessNativeUISectionView(harness: harness)
            HarnessSeededDataSectionView(harness: harness)
        }
    }
}
