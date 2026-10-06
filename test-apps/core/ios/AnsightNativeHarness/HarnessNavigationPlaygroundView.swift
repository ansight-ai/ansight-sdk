import SwiftUI

struct HarnessNavigationPlaygroundView: View {
    @ObservedObject var harness: HarnessViewModel
    @State private var showSheet = false
    @State private var showFullScreen = false
    @State private var showFlyout = false
    @State private var pushActive = false

    var body: some View {
        NavigationView {
            ZStack(alignment: .leading) {
                Color(.systemGroupedBackground)
                    .ignoresSafeArea()

                ScrollView {
                    VStack(alignment: .leading, spacing: 14) {
                        HarnessSection("Current Flow", systemImage: "point.topleft.down.curvedto.point.bottomright.up") {
                            VStack(spacing: 0) {
                                HarnessKeyValueRow("Modal", value: harness.activeModal)
                                Divider()
                                HarnessKeyValueRow("Flyout", value: harness.flyoutSelection)
                                Divider()
                                HarnessKeyValueRow("Push depth", value: "\(harness.pushDepth)")
                            }
                        }

                        HarnessSection("Navigation Modes", systemImage: "arrow.triangle.branch") {
                            VStack(alignment: .leading, spacing: 12) {
                                HarnessActionGrid {
                                    HarnessActionButton("Push Detail", systemImage: "chevron.right.circle", isBusy: harness.isBusy, isProminent: true) {
                                        pushActive = true
                                        harness.pushDepthChanged(1)
                                    }
                                    HarnessActionButton("Sheet Modal", systemImage: "rectangle.bottomthird.inset.filled", isBusy: harness.isBusy) {
                                        showSheet = true
                                        harness.modalStateChanged("sheet")
                                    }
                                    HarnessActionButton("Full Screen", systemImage: "arrow.up.left.and.arrow.down.right", isBusy: harness.isBusy) {
                                        showFullScreen = true
                                        harness.modalStateChanged("fullScreen")
                                    }
                                    HarnessActionButton("Flyout Drawer", systemImage: "sidebar.leading", isBusy: harness.isBusy) {
                                        withAnimation(.easeInOut(duration: 0.2)) {
                                            showFlyout.toggle()
                                        }
                                        harness.flyoutChanged(showFlyout ? "drawer-open" : "drawer-closed")
                                    }
                                }

                                NavigationLink(
                                    destination: HarnessPushLevelView(harness: harness, level: 1),
                                    isActive: $pushActive
                                ) {
                                    EmptyView()
                                }
                                .hidden()
                            }
                        }

                        HarnessSection("Flyout Menu", systemImage: "line.3.horizontal.decrease.circle") {
                            Menu {
                                ForEach(harness.flyoutItems, id: \.self) { item in
                                    Button(item) {
                                        harness.flyoutChanged(item)
                                    }
                                }
                            } label: {
                                Label(harness.flyoutSelection, systemImage: "line.3.horizontal.decrease.circle")
                                    .font(.subheadline.weight(.semibold))
                                    .frame(maxWidth: .infinity, minHeight: 42)
                            }
                            .buttonStyle(.bordered)

                            HarnessKeyValueRow("Selected", value: harness.flyoutSelection)
                        }

                        HarnessSection("Navigation State Root", systemImage: "doc.text.magnifyingglass") {
                            HarnessMonospacedBlock("""
                            rootId=navigation.flow
                            selectedTab=\(harness.selectedTab.rawValue)
                            activeModal=\(harness.activeModal)
                            flyoutSelection=\(harness.flyoutSelection)
                            pushDepth=\(harness.pushDepth)
                            recentEvents=\(harness.navigationEvents.suffix(5).joined(separator: " | "))
                            """)
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.top, 12)
                    .padding(.bottom, 112)
                }

                if showFlyout {
                    Color.black.opacity(0.24)
                        .ignoresSafeArea()
                        .onTapGesture {
                            withAnimation(.easeInOut(duration: 0.2)) {
                                showFlyout = false
                            }
                            harness.flyoutChanged("drawer-closed")
                        }
                        .zIndex(1)

                    HarnessFlyoutDrawerView(harness: harness, isVisible: $showFlyout)
                        .transition(.move(edge: .leading))
                        .zIndex(2)
                    }
            }
            .navigationTitle("Flow")
            .navigationBarTitleDisplayMode(.inline)
            .sheet(isPresented: $showSheet, onDismiss: {
                harness.modalStateChanged("<none>")
            }) {
                HarnessModalContentView(harness: harness, title: "Sheet Modal", systemImage: "rectangle.bottomthird.inset.filled")
            }
            .fullScreenCover(isPresented: $showFullScreen, onDismiss: {
                harness.modalStateChanged("<none>")
            }) {
                HarnessModalContentView(harness: harness, title: "Full Screen Modal", systemImage: "arrow.up.left.and.arrow.down.right")
            }
        }
        .navigationViewStyle(.stack)
    }
}
