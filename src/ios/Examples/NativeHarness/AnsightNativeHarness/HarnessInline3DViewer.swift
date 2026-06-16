import SceneKit
import SwiftUI
import UIKit

struct HarnessInline3DViewer: UIViewRepresentable {
    @Binding var materialName: String
    @Binding var rotationEnabled: Bool
    @Binding var spinSpeed: Double
    @Binding var selectedNodeName: String

    func makeUIView(context: Context) -> SCNView {
        let view = SCNView(frame: .zero)
        view.scene = context.coordinator.scene
        view.backgroundColor = UIColor.systemBackground
        view.allowsCameraControl = true
        view.autoenablesDefaultLighting = false
        view.antialiasingMode = .multisampling4X

        let tap = UITapGestureRecognizer(target: context.coordinator, action: #selector(HarnessInline3DViewerCoordinator.handleTap(_:)))
        view.addGestureRecognizer(tap)
        context.coordinator.sceneView = view
        context.coordinator.apply(materialName: materialName, rotationEnabled: rotationEnabled, spinSpeed: spinSpeed)
        return view
    }

    func updateUIView(_ uiView: SCNView, context: Context) {
        context.coordinator.parent = self
        context.coordinator.apply(materialName: materialName, rotationEnabled: rotationEnabled, spinSpeed: spinSpeed)
    }

    func makeCoordinator() -> HarnessInline3DViewerCoordinator {
        HarnessInline3DViewerCoordinator(parent: self)
    }
}
