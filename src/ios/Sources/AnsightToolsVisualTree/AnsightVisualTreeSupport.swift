import AnsightCore
import Foundation

#if canImport(UIKit)
import QuartzCore
import UIKit
#endif

internal enum AnsightVisualTreeSupport {
    static var currentPlatform: String {
        #if targetEnvironment(macCatalyst)
        "maccatalyst"
        #elseif os(iOS)
        "ios"
        #else
        "unknown"
        #endif
    }

    static func getVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
        let source = AnsightVisualTreeProviderRegistry.normalizedSourceOrDefault(
            AnsightVisualTreeArgumentReader.string(arguments, key: "source")
        )
        guard let provider = AnsightVisualTreeProviderRegistry.provider(for: source) else {
            return .failure("No visual tree provider is registered for source '\(source)'.", errorCode: "visual_tree_provider_not_found")
        }

        return provider.getVisualTree(arguments: arguments)
    }

    static func getNativeVisualTree(arguments: [String: String]) -> AnsightToolExecutionResult {
        do {
            let includeBounds = try AnsightVisualTreeArgumentReader.bool(arguments, key: "includeBounds", defaultValue: true)
            let includeProperties = try AnsightVisualTreeArgumentReader.bool(arguments, key: "includeComputedStyles", defaultValue: false)
            let maxDepth = try AnsightVisualTreeArgumentReader.integer(arguments, key: "maxDepth", defaultValue: 8, minimum: 1, maximum: 64)
            let rootNodeId = AnsightVisualTreeArgumentReader.string(arguments, key: "rootNodeId")

            let rootNode = try captureTree(includeProperties: includeProperties)
            guard let selectedRoot = rootNodeId == nil ? rootNode : rootNode.find(rootNodeId!) else {
                return .failure("The node '\(rootNodeId ?? "")' was not found.", errorCode: "visual_tree_node_not_found")
            }

            return .success(.object([
                "platform": .string(currentPlatform),
                "source": .string(AnsightVisualTreeProviderRegistry.nativeSource),
                "adapter": .string("apple.uikit"),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "root": selectedRoot.jsonValue(includeBounds: includeBounds, includeProperties: includeProperties, maxDepth: maxDepth),
            ]))
        } catch let error as AnsightVisualTreeToolError {
            return .failure(error.localizedDescription, errorCode: error.errorCode)
        } catch {
            return .failure(error.localizedDescription, errorCode: "visual_tree_execution_failed")
        }
    }

    static func inspectNode(arguments: [String: String]) -> AnsightToolExecutionResult {
        let source = AnsightVisualTreeProviderRegistry.normalizedSourceOrDefault(
            AnsightVisualTreeArgumentReader.string(arguments, key: "source")
        )
        guard let provider = AnsightVisualTreeProviderRegistry.provider(for: source) else {
            return .failure("No visual tree provider is registered for source '\(source)'.", errorCode: "visual_tree_provider_not_found")
        }

        return provider.inspectNode(arguments: arguments)
    }

    static func inspectNativeNode(arguments: [String: String]) -> AnsightToolExecutionResult {
        do {
            let nodeId = try AnsightVisualTreeArgumentReader.requiredString(arguments, key: "nodeId")
            let includeAncestors = try AnsightVisualTreeArgumentReader.bool(arguments, key: "includeAncestors", defaultValue: false)
            let includeDescendants = try AnsightVisualTreeArgumentReader.bool(arguments, key: "includeDescendants", defaultValue: false)
            let includeProperties = try AnsightVisualTreeArgumentReader.bool(arguments, key: "includeProperties", defaultValue: true)

            let rootNode = try captureTree(includeProperties: includeProperties)
            var ancestors: [AnsightVisualNode] = []
            guard let node = rootNode.find(nodeId, ancestors: &ancestors) else {
                return .failure("The node '\(nodeId)' was not found.", errorCode: "visual_tree_node_not_found")
            }

            var payload: [String: JSONValue] = [
                "platform": .string(currentPlatform),
                "source": .string(AnsightVisualTreeProviderRegistry.nativeSource),
                "adapter": .string("apple.uikit"),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "node": node.jsonValue(includeBounds: true, includeProperties: includeProperties, maxDepth: 32),
            ]

            if includeAncestors {
                payload["ancestors"] = .array(ancestors.map {
                    $0.jsonValue(includeBounds: true, includeProperties: includeProperties, maxDepth: 0)
                })
            }

            if includeDescendants {
                payload["descendants"] = .array(node.descendants().map {
                    $0.jsonValue(includeBounds: true, includeProperties: includeProperties, maxDepth: 32)
                })
            }

            return .success(.object(payload))
        } catch let error as AnsightVisualTreeToolError {
            return .failure(error.localizedDescription, errorCode: error.errorCode)
        } catch {
            return .failure(error.localizedDescription, errorCode: "visual_tree_execution_failed")
        }
    }

    static func captureScreenshot(arguments: [String: String]) -> Result<AnsightVisualTreeScreenshot, AnsightVisualTreeToolError> {
        do {
            let rawFormat = (AnsightVisualTreeArgumentReader.string(arguments, key: "format") ?? "png").lowercased()
            let format = rawFormat == "jpeg" || rawFormat == "jpg" ? "jpeg" : "png"
            let quality = try AnsightVisualTreeArgumentReader.integer(arguments, key: "quality", defaultValue: 90, minimum: 1, maximum: 100)
            let maxWidth = try AnsightVisualTreeArgumentReader.optionalInteger(arguments, key: "maxWidth", minimum: 1, maximum: 8192)
            _ = try AnsightVisualTreeArgumentReader.bool(arguments, key: "annotateNodeIds", defaultValue: false)
            let afterScreenUpdates = try AnsightVisualTreeArgumentReader.bool(arguments, key: "afterScreenUpdates", defaultValue: true)
            return .success(try captureScreenshot(format: format, quality: quality, maxWidth: maxWidth, afterScreenUpdates: afterScreenUpdates))
        } catch let error as AnsightVisualTreeToolError {
            return .failure(error)
        } catch {
            return .failure(.unavailable(error.localizedDescription))
        }
    }

    static func captureTree(includeProperties: Bool) throws -> AnsightVisualNode {
        #if canImport(UIKit)
        return try runOnMainActor {
            guard let window = activeWindow() else {
                throw AnsightVisualTreeToolError.unavailable("No active UIWindow is available.")
            }

            return buildNode(view: window, window: window, includeProperties: includeProperties)
        }
        #else
        throw AnsightVisualTreeToolError.platformUnsupported
        #endif
    }

    static func boundsForNode(nodeId: String) throws -> AnsightVisualTreeBounds {
        #if canImport(UIKit)
        return try runOnMainActor {
            guard let window = activeWindow() else {
                throw AnsightVisualTreeToolError.unavailable("No active UIWindow is available.")
            }

            guard let view = findView(in: window, nodeId: nodeId) else {
                throw AnsightVisualTreeToolError.unavailable("The node '\(nodeId)' was not found.")
            }

            let frame = view.convert(view.bounds, to: window)
            return AnsightVisualTreeBounds(
                x: Double(frame.origin.x),
                y: Double(frame.origin.y),
                width: Double(frame.width),
                height: Double(frame.height)
            )
        }
        #else
        throw AnsightVisualTreeToolError.platformUnsupported
        #endif
    }

    private static func captureScreenshot(
        format: String,
        quality: Int,
        maxWidth: Int?,
        afterScreenUpdates: Bool
    ) throws -> AnsightVisualTreeScreenshot {
        #if canImport(UIKit)
        do {
            return try runOnMainActor {
                let snapshotFormat: AnsightScreenSnapshotFormat = format == "jpeg" ? .jpeg : .png
                let snapshot = try AnsightScreenSnapshotRenderer.capture(
                    format: snapshotFormat,
                    quality: quality,
                    maxWidth: maxWidth,
                    afterScreenUpdates: afterScreenUpdates
                )

                return AnsightVisualTreeScreenshot(
                    format: format,
                    width: snapshot.width,
                    height: snapshot.height,
                    data: snapshot.data,
                    annotationApplied: false
                )
            }
        } catch {
            throw AnsightVisualTreeToolError.unavailable(error.localizedDescription)
        }
        #else
        throw AnsightVisualTreeToolError.platformUnsupported
        #endif
    }

    #if canImport(UIKit)
    private static func runOnMainActor<T: Sendable>(_ action: @MainActor () throws -> T) rethrows -> T {
        if Thread.isMainThread {
            return try MainActor.assumeIsolated {
                try action()
            }
        }

        return try DispatchQueue.main.sync {
            try MainActor.assumeIsolated {
                try action()
            }
        }
    }

    @MainActor
    static func activeWindow() -> UIWindow? {
        let scenes = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .filter { scene in
                scene.activationState == .foregroundActive || scene.activationState == .foregroundInactive
            }

        return scenes
            .flatMap(\.windows)
            .first { $0.isKeyWindow }
            ?? scenes.flatMap(\.windows).first { !$0.isHidden && $0.alpha > 0 }
            ?? UIApplication.shared.windows.first { $0.isKeyWindow }
            ?? UIApplication.shared.windows.first { !$0.isHidden && $0.alpha > 0 }
    }

    @MainActor
    static func buildNode(view: UIView, window: UIWindow, includeProperties: Bool) -> AnsightVisualNode {
        let frame = view.convert(view.bounds, to: window)
        let properties = includeProperties ? propertiesForView(view) : [:]
        return AnsightVisualNode(
            id: nodeId(for: view),
            type: String(describing: type(of: view)),
            label: label(for: view),
            visible: !view.isHidden && view.alpha > 0,
            enabled: isEnabled(view),
            focusable: view.canBecomeFocused || view.isAccessibilityElement,
            bounds: AnsightVisualTreeBounds(
                x: Double(frame.origin.x),
                y: Double(frame.origin.y),
                width: Double(frame.width),
                height: Double(frame.height)
            ),
            properties: properties,
            children: view.subviews.map { buildNode(view: $0, window: window, includeProperties: includeProperties) }
        )
    }

    @MainActor
    static func findView(in view: UIView, nodeId: String) -> UIView? {
        if self.nodeId(for: view) == nodeId {
            return view
        }

        for child in view.subviews {
            if let match = findView(in: child, nodeId: nodeId) {
                return match
            }
        }

        return nil
    }

    @MainActor
    static func nodeId(for view: UIView) -> String {
        String(UInt(bitPattern: ObjectIdentifier(view)))
    }

    @MainActor
    private static func isEnabled(_ view: UIView) -> Bool {
        if let control = view as? UIControl {
            return control.isEnabled
        }

        return view.isUserInteractionEnabled
    }

    @MainActor
    private static func label(for view: UIView) -> String? {
        if let label = view as? UILabel, let text = normalized(label.text) {
            return text
        }

        if let button = view as? UIButton, let title = normalized(button.currentTitle) {
            return title
        }

        if let textField = view as? UITextField, let text = normalized(textField.text) {
            return text
        }

        if let textView = view as? UITextView, let text = normalized(textView.text) {
            return text
        }

        return normalized(view.accessibilityLabel) ?? normalized(view.accessibilityIdentifier)
    }

    @MainActor
    private static func propertiesForView(_ view: UIView) -> [String: JSONValue] {
        var properties: [String: JSONValue] = [
            "alpha": .number(Double(view.alpha)),
            "opaque": .bool(view.isOpaque),
            "clipsToBounds": .bool(view.clipsToBounds),
            "userInteractionEnabled": .bool(view.isUserInteractionEnabled),
            "accessibilityIdentifier": view.accessibilityIdentifier.map(JSONValue.string) ?? .null,
        ]

        if let backgroundColor = view.backgroundColor {
            properties["backgroundColor"] = .string(hexColor(backgroundColor))
        }

        if let control = view as? UIControl {
            properties["selected"] = .bool(control.isSelected)
            properties["highlighted"] = .bool(control.isHighlighted)
        }

        return properties
    }

    private static func normalized(_ value: String?) -> String? {
        guard let value = value?.trimmingCharacters(in: .whitespacesAndNewlines),
              !value.isEmpty
        else {
            return nil
        }

        return value
    }

    private static func hexColor(_ color: UIColor) -> String {
        var red: CGFloat = 0
        var green: CGFloat = 0
        var blue: CGFloat = 0
        var alpha: CGFloat = 0
        guard color.getRed(&red, green: &green, blue: &blue, alpha: &alpha) else {
            return "#00000000"
        }

        return String(
            format: "#%02X%02X%02X%02X",
            Int(round(alpha * 255)),
            Int(round(red * 255)),
            Int(round(green * 255)),
            Int(round(blue * 255))
        )
    }
    #endif
}
