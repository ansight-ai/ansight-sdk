import SwiftUI

struct ContentView: View {
    @StateObject private var harness = HarnessViewModel()

    var body: some View {
        TabView(selection: $harness.selectedTab) {
            HarnessDashboardView(harness: harness)
                .tabItem {
                    Label("Home", systemImage: "gauge.with.dots.needle.bottom.50percent")
                }
                .tag(HarnessTab.dashboard)

            Harness3DViewerTabView(harness: harness)
                .tabItem {
                    Label("3D", systemImage: "cube.transparent")
                }
                .tag(HarnessTab.viewer3D)

            HarnessNavigationPlaygroundView(harness: harness)
                .tabItem {
                    Label("Flow", systemImage: "point.topleft.down.curvedto.point.bottomright.up")
                }
                .tag(HarnessTab.navigation)

            HarnessDataInspectorView(harness: harness)
                .tabItem {
                    Label("Data", systemImage: "externaldrive.connected.to.line.below")
                }
                .tag(HarnessTab.data)

            HarnessRuntimeSnapshotView(harness: harness)
                .tabItem {
                    Label("Debug", systemImage: "doc.text.magnifyingglass")
                }
                .tag(HarnessTab.snapshot)
        }
        .tint(.blue)
        .task {
            await harness.bootstrap()
        }
        .onChange(of: harness.selectedTab) { tab in
            harness.selectedTabChanged(tab)
        }
    }
}
