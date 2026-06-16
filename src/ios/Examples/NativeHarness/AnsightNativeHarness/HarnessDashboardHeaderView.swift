import Ansight
import SwiftUI

struct HarnessDashboardHeaderView: View {
    @ObservedObject var harness: HarnessViewModel

    private var connectionBadge: HarnessStatusBadge {
        let status = harness.snapshot.hostConnectionStatus
        if harness.snapshot.sessionOpen {
            return HarnessStatusBadge("Live", systemImage: "checkmark.circle.fill", tint: .green)
        }

        if status.connectionState == .connecting {
            return HarnessStatusBadge("Probing", systemImage: "bolt.horizontal.circle", tint: .orange)
        }

        return HarnessStatusBadge("Offline", systemImage: "wifi.slash", tint: .secondary)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: "app.connected.to.app.below.fill")
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(.white)
                    .frame(width: 44, height: 44)
                    .background(
                        LinearGradient(
                            colors: [Color.blue, Color.green],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))

                VStack(alignment: .leading, spacing: 5) {
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text("Native Harness")
                            .font(.headline.weight(.semibold))
                            .lineLimit(1)
                            .minimumScaleFactor(0.75)

                        Spacer(minLength: 6)

                        connectionBadge
                    }

                    Text(harness.statusText)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(3)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }

            LazyVGrid(
                columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)],
                spacing: 8
            ) {
                HarnessMetricTile(
                    "FPS",
                    value: harness.snapshot.lastFrameRate.map(String.init) ?? "--",
                    systemImage: "speedometer",
                    tint: .green
                )
                HarnessMetricTile(
                    "Touches",
                    value: "\(harness.snapshot.touchesSent)/\(harness.snapshot.touchesCaptured)",
                    systemImage: "hand.tap",
                    tint: .orange
                )
                HarnessMetricTile(
                    "Frames",
                    value: "\(harness.snapshot.screenFramesSent)/\(harness.snapshot.screenFramesCaptured)",
                    systemImage: "camera.viewfinder",
                    tint: .blue
                )
                HarnessMetricTile(
                    "Rows",
                    value: "\(harness.databaseRowCount)",
                    systemImage: "cylinder.split.1x2",
                    tint: .purple
                )
            }

            if harness.isBusy {
                ProgressView()
                    .progressViewStyle(.linear)
            }
        }
        .padding(14)
        .background(Color(.secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
    }
}
