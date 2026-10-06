import SwiftUI

struct HarnessPushLevelView: View {
    @ObservedObject var harness: HarnessViewModel
    let level: Int
    @State private var pushNext = false
    @Environment(\.presentationMode) private var presentationMode

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HarnessSection("Push State", systemImage: "chevron.right.circle") {
                VStack(spacing: 0) {
                    HarnessKeyValueRow("Level", value: "\(level)")
                    Divider()
                    HarnessKeyValueRow("Depth", value: "\(harness.pushDepth)")
                }
            }

            HarnessActionGrid {
                if level < 3 {
                    HarnessActionButton("Push Level \(level + 1)", systemImage: "chevron.right.2", isBusy: harness.isBusy, isProminent: true) {
                        pushNext = true
                        harness.pushDepthChanged(level + 1)
                    }
                }
                HarnessActionButton("Pop", systemImage: "chevron.left.circle", isBusy: harness.isBusy) {
                    harness.pushDepthChanged(max(0, level - 1))
                    presentationMode.wrappedValue.dismiss()
                }
                HarnessActionButton("Event", systemImage: "flag", isBusy: harness.isBusy) {
                    harness.recordEvent(label: "ios_harness_push_level_\(level)")
                }
            }

            NavigationLink(
                destination: HarnessPushLevelView(harness: harness, level: level + 1),
                isActive: $pushNext
            ) {
                EmptyView()
            }
            .hidden()

            Spacer()
        }
        .padding(.horizontal, 16)
        .padding(.top, 12)
        .padding(.bottom, 112)
        .background(Color(.systemGroupedBackground).ignoresSafeArea())
        .navigationTitle("Level \(level)")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            harness.pushDepthChanged(level)
        }
        .onDisappear {
            if harness.pushDepth >= level {
                harness.pushDepthChanged(max(0, level - 1))
            }
        }
    }
}
