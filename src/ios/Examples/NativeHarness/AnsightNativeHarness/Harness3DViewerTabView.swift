import SwiftUI

struct Harness3DViewerTabView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessScreen("3D Viewer") {
            HarnessInline3DViewer(
                materialName: $harness.sceneMaterial,
                rotationEnabled: $harness.sceneRotationEnabled,
                spinSpeed: $harness.sceneSpinSpeed,
                selectedNodeName: $harness.selectedSceneNode
            )
            .frame(height: 300)
            .background(Color(.secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))

            HarnessSection("Scene Controls", systemImage: "slider.horizontal.3") {
                VStack(alignment: .leading, spacing: 12) {
                    VStack(spacing: 0) {
                        HarnessKeyValueRow("Selected node", value: harness.selectedSceneNode)
                        Divider()
                        HarnessKeyValueRow("Material", value: harness.sceneMaterial)
                    }

                    Picker("Material", selection: $harness.sceneMaterial) {
                        ForEach(harness.sceneMaterials, id: \.self) { material in
                            Text(material).tag(material)
                        }
                    }
                    .pickerStyle(.menu)
                    .onChange(of: harness.sceneMaterial) { value in
                        harness.sceneStateChanged("material:\(value)")
                    }

                    Toggle(isOn: $harness.sceneRotationEnabled) {
                        Label("Rotate scene", systemImage: "rotate.3d")
                            .font(.subheadline.weight(.medium))
                    }
                    .onChange(of: harness.sceneRotationEnabled) { value in
                        harness.sceneStateChanged("rotation:\(value)")
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Label("Spin speed", systemImage: "speedometer")
                                .font(.subheadline.weight(.medium))
                            Spacer()
                            Text("\(String(format: "%.1f", harness.sceneSpinSpeed))x")
                                .font(.headline.monospacedDigit())
                        }

                        Slider(value: $harness.sceneSpinSpeed, in: 0.25...2.5, step: 0.25)
                            .onChange(of: harness.sceneSpinSpeed) { value in
                                harness.sceneStateChanged("spin:\(value)")
                            }
                    }
                }
            }

            HarnessSection("3D State Root", systemImage: "point.3.connected.trianglepath.dotted") {
                HarnessMonospacedBlock("""
                rootId=scene.inline3d
                material=\(harness.sceneMaterial)
                rotationEnabled=\(String(harness.sceneRotationEnabled))
                spinSpeed=\(String(format: "%.2f", harness.sceneSpinSpeed))
                selectedNode=\(harness.selectedSceneNode)
                """)
            }
        }
    }
}
