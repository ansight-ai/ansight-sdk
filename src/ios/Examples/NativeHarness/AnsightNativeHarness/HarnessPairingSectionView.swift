import SwiftUI

struct HarnessPairingSectionView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessSection("Enrollment", systemImage: "link") {
            VStack(alignment: .leading, spacing: 12) {
                HarnessActionButton("Auto Connect", systemImage: "bolt.horizontal.fill", isBusy: harness.isBusy, isProminent: true) {
                    harness.runAsync {
                        await harness.connect(.auto(clientName: HarnessConstants.clientName))
                    }
                }

                VStack(spacing: 0) {
                    HarnessKeyValueRow("Host", value: harness.snapshot.hostConnectionStatus.hostName ?? "No host")
                    Divider()
                    HarnessKeyValueRow("Registration", value: harness.snapshot.lastPairingConfigId ?? "Not enrolled")
                    Divider()
                    HarnessKeyValueRow("State", value: harness.snapshot.hostConnectionStatus.summaryMessage)
                }

                HarnessActionGrid {
                    HarnessActionButton("Initialize", systemImage: "power", isBusy: harness.isBusy) {
                        harness.initializeTapped()
                    }
                    HarnessActionButton("Scan Enrollment QR", systemImage: "qrcode.viewfinder", isBusy: harness.isBusy) {
                        harness.runAsync {
                            await harness.connect(.qrCode(
                                title: "Scan Ansight Enrollment QR",
                                clientName: HarnessConstants.clientName
                            ))
                        }
                    }
                    HarnessActionButton("Disconnect", systemImage: "xmark.circle", isBusy: harness.isBusy) {
                        harness.runAsync {
                            await harness.disconnect()
                        }
                    }
                    HarnessActionButton("Clear Enrollment", systemImage: "trash", role: .destructive, isBusy: harness.isBusy) {
                        harness.clearPairingState()
                    }
                }
            }
        }
    }
}
