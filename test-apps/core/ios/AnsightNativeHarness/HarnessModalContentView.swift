import SwiftUI

struct HarnessModalContentView: View {
    @ObservedObject var harness: HarnessViewModel
    let title: String
    let systemImage: String
    @Environment(\.dismiss) private var dismiss

    private var eventLabel: String {
        title
            .replacingOccurrences(of: " ", with: "_")
            .lowercased()
    }

    var body: some View {
        NavigationView {
            VStack(alignment: .leading, spacing: 14) {
                HarnessSection(title, systemImage: systemImage) {
                    VStack(spacing: 0) {
                        HarnessKeyValueRow("Presentation", value: eventLabel)
                        Divider()
                        HarnessKeyValueRow("Active modal", value: harness.activeModal)
                    }
                }

                HarnessActionGrid {
                    HarnessActionButton("Record Screen", systemImage: "rectangle.on.rectangle", isBusy: harness.isBusy, isProminent: true) {
                        harness.recordScreen(title)
                    }
                    HarnessActionButton("Record Event", systemImage: "flag", isBusy: harness.isBusy) {
                        harness.recordEvent(label: "ios_harness_modal_\(eventLabel)")
                    }
                    HarnessActionButton("Dismiss", systemImage: "xmark.circle", isBusy: harness.isBusy) {
                        dismiss()
                    }
                }

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.top, 12)
            .padding(.bottom, 24)
            .background(Color(.systemGroupedBackground).ignoresSafeArea())
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") {
                        dismiss()
                    }
                }
            }
        }
        .navigationViewStyle(.stack)
    }
}
