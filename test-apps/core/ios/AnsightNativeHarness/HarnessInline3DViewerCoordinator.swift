import SceneKit
import SwiftUI
import UIKit

@MainActor
final class HarnessInline3DViewerCoordinator: NSObject {
    var parent: HarnessInline3DViewer
    weak var sceneView: SCNView?

    let scene = SCNScene()
    private let rootNode = SCNNode()
    private var currentMaterialName = ""
    private var currentRotationEnabled = false
    private var currentSpinSpeed = 0.0

    init(parent: HarnessInline3DViewer) {
        self.parent = parent
        super.init()
        buildScene()
    }

    func apply(materialName: String, rotationEnabled: Bool, spinSpeed: Double) {
        if currentMaterialName != materialName {
            currentMaterialName = materialName
            applyMaterial(named: materialName)
        }

        if currentRotationEnabled != rotationEnabled || currentSpinSpeed != spinSpeed {
            currentRotationEnabled = rotationEnabled
            currentSpinSpeed = spinSpeed
            applyRotation(enabled: rotationEnabled, spinSpeed: spinSpeed)
        }
    }

    @objc
    func handleTap(_ recognizer: UITapGestureRecognizer) {
        guard let view = sceneView else {
            return
        }

        let location = recognizer.location(in: view)
        let hit = view.hitTest(location, options: nil).first?.node
        let selected = hit?.name ?? "<none>"
        parent.selectedNodeName = selected
        highlight(nodeName: selected)
    }

    private func buildScene() {
        scene.rootNode.addChildNode(rootNode)

        let camera = SCNCamera()
        camera.fieldOfView = 55
        let cameraNode = SCNNode()
        cameraNode.camera = camera
        cameraNode.position = SCNVector3(0, 1.4, 6.2)
        cameraNode.eulerAngles = SCNVector3(-0.18, 0, 0)
        scene.rootNode.addChildNode(cameraNode)

        let keyLight = SCNLight()
        keyLight.type = .omni
        keyLight.intensity = 900
        let keyLightNode = SCNNode()
        keyLightNode.light = keyLight
        keyLightNode.position = SCNVector3(2.4, 3.0, 3.2)
        scene.rootNode.addChildNode(keyLightNode)

        let ambient = SCNLight()
        ambient.type = .ambient
        ambient.intensity = 250
        ambient.color = UIColor.systemGray2
        let ambientNode = SCNNode()
        ambientNode.light = ambient
        scene.rootNode.addChildNode(ambientNode)

        let cube = SCNNode(geometry: SCNBox(width: 1.35, height: 1.35, length: 1.35, chamferRadius: 0.08))
        cube.name = "cube"
        cube.position = SCNVector3(-1.35, 0, 0)
        rootNode.addChildNode(cube)

        let sphere = SCNNode(geometry: SCNSphere(radius: 0.76))
        sphere.name = "sphere"
        sphere.position = SCNVector3(1.25, 0.05, 0)
        rootNode.addChildNode(sphere)

        let torus = SCNNode(geometry: SCNTorus(ringRadius: 0.88, pipeRadius: 0.12))
        torus.name = "torus"
        torus.position = SCNVector3(0, -1.28, -0.25)
        torus.eulerAngles = SCNVector3(Float.pi / 2, 0, 0)
        rootNode.addChildNode(torus)

        let floor = SCNFloor()
        floor.reflectivity = 0.18
        let floorNode = SCNNode(geometry: floor)
        floorNode.name = "floor"
        floorNode.position = SCNVector3(0, -1.9, 0)
        rootNode.addChildNode(floorNode)

        applyMaterial(named: parent.materialName)
        applyRotation(enabled: parent.rotationEnabled, spinSpeed: parent.spinSpeed)
    }

    private func applyMaterial(named name: String) {
        let color: UIColor
        switch name {
        case "Graphite":
            color = UIColor.systemGray
        case "Plum":
            color = UIColor.systemPurple
        case "Safety Orange":
            color = UIColor.systemOrange
        default:
            color = UIColor.systemTeal
        }

        for node in rootNode.childNodes where node.name != "floor" {
            let material = SCNMaterial()
            material.diffuse.contents = color
            material.metalness.contents = 0.35
            material.roughness.contents = 0.42
            node.geometry?.materials = [material]
        }

        let floorMaterial = SCNMaterial()
        floorMaterial.diffuse.contents = UIColor.secondarySystemBackground
        rootNode.childNode(withName: "floor", recursively: false)?.geometry?.materials = [floorMaterial]
    }

    private func applyRotation(enabled: Bool, spinSpeed: Double) {
        rootNode.removeAction(forKey: "spin")
        guard enabled else {
            return
        }

        let duration = max(2.0, 7.0 / max(0.25, spinSpeed))
        let action = SCNAction.repeatForever(.rotateBy(x: 0, y: CGFloat(Double.pi * 2), z: 0, duration: duration))
        rootNode.runAction(action, forKey: "spin")
    }

    private func highlight(nodeName: String) {
        for node in rootNode.childNodes {
            node.scale = SCNVector3(1, 1, 1)
        }

        guard let node = rootNode.childNode(withName: nodeName, recursively: false) else {
            return
        }

        node.scale = SCNVector3(1.15, 1.15, 1.15)
        let pulse = SCNAction.sequence([
            .scale(to: 1.24, duration: 0.12),
            .scale(to: 1.15, duration: 0.16),
        ])
        node.runAction(pulse)
    }
}
