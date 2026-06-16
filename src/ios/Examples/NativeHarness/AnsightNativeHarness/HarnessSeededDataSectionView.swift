import SwiftUI

struct HarnessSeededDataSectionView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessSection("Seeded Data", systemImage: "externaldrive.connected.to.line.below") {
            VStack(alignment: .leading, spacing: 12) {
                VStack(spacing: 0) {
                    HarnessKeyValueRow("Database rows", value: "\(harness.databaseRowCount)")
                    Divider()
                    HarnessKeyValueRow("Seeded at", value: harness.seededAtUtc)
                }

                HarnessActionButton("Re-seed Data", systemImage: "externaldrive.badge.plus", isBusy: harness.isBusy) {
                    harness.seedDataTapped()
                }

                HarnessMonospacedBlock(harness.seededDataText)
            }
        }
    }
}
