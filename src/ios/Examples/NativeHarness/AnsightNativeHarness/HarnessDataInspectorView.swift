import SwiftUI

struct HarnessDataInspectorView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("Data") {
            HarnessSection("Database", systemImage: "cylinder.split.1x2") {
                VStack(alignment: .leading, spacing: 12) {
                    VStack(spacing: 0) {
                        HarnessKeyValueRow("Alias", value: "harness")
                        Divider()
                        HarnessKeyValueRow("Rows", value: "\(harness.databaseRowCount)")
                        Divider()
                        HarnessKeyValueRow("Tables", value: "4")
                    }

                    HarnessActionGrid {
                        HarnessActionButton("Re-seed Database", systemImage: "externaldrive.badge.plus", isBusy: harness.isBusy, isProminent: true) {
                            harness.seedDataTapped()
                        }
                        HarnessActionButton("Record Screen", systemImage: "rectangle.on.rectangle", isBusy: harness.isBusy) {
                            harness.recordScreen("Harness Data Inspector")
                        }
                    }

                    HarnessMonospacedBlock("""
                    alias=harness
                    path=\(harness.databasePathText())
                    rowCount=\(harness.databaseRowCount)
                    tables=harness_events,harness_orders,harness_inventory,harness_navigation_events
                    """)
                }
            }

            HarnessSection("Reflection Roots", systemImage: "square.stack.3d.up") {
                VStack(alignment: .leading, spacing: 10) {
                    HarnessMonospacedBlock(harness.reflectionRootsText)

                    HarnessMonospacedBlock("""
                    tools:
                    harness.state.snapshot
                    harness.reflection_roots.list
                    harness.reflection_root.inspect(rootId)
                    """)
                }
            }

            HarnessSection("Seeded Data", systemImage: "externaldrive.connected.to.line.below") {
                HarnessMonospacedBlock(harness.seededDataText)
            }
        }
    }
}
