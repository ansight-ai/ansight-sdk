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

            let typeRegistry = AnsightVisualTreeTypeRegistry()
            let root = selectedRoot.jsonValue(
                includeBounds: includeBounds,
                includeProperties: includeProperties,
                maxDepth: maxDepth,
                typeRegistry: typeRegistry
            )
            return .success(.object([
                "platform": .string(currentPlatform),
                "source": .string(AnsightVisualTreeProviderRegistry.nativeSource),
                "format": .string("ansight.native.visual-tree.compact.v2"),
                "adapter": .string("apple.uikit"),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "types": typeRegistry.jsonValue,
                "root": root,
                "nodeCount": .integer(Int64(selectedRoot.nodeCount)),
                "truncated": .bool(false),
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

            let typeRegistry = AnsightVisualTreeTypeRegistry()
            var payload: [String: JSONValue] = [
                "format": .string("ansight.native.visual-tree.compact.v2"),
                "platform": .string(currentPlatform),
                "source": .string(AnsightVisualTreeProviderRegistry.nativeSource),
                "adapter": .string("apple.uikit"),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "node": node.jsonValue(
                    includeBounds: true,
                    includeProperties: includeProperties,
                    maxDepth: 0,
                    typeRegistry: typeRegistry
                ),
            ]

            if includeAncestors {
                payload["ancestors"] = .array(ancestors.map {
                    $0.jsonValue(
                        includeBounds: true,
                        includeProperties: includeProperties,
                        maxDepth: 0,
                        typeRegistry: typeRegistry
                    )
                })
            }

            if includeDescendants {
                payload["descendants"] = .array(node.descendants().map {
                    $0.jsonValue(
                        includeBounds: true,
                        includeProperties: includeProperties,
                        maxDepth: 0,
                        typeRegistry: typeRegistry
                    )
                })
            }

            payload["types"] = typeRegistry.jsonValue

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
            let quality = try AnsightVisualTreeArgumentReader.integer(
                arguments,
                key: "quality",
                defaultValue: AnsightSessionJpegCaptureOptions.defaultQuality,
                minimum: 1,
                maximum: 100
            )
            let maxWidth = try AnsightVisualTreeArgumentReader.optionalInteger(
                arguments,
                key: "maxWidth",
                minimum: 1,
                maximum: 8192
            ) ?? AnsightSessionJpegCaptureOptions.defaultMaxWidth
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
            let windows = activeWindows()
            guard let activeWindow = windows.first(where: \.isKeyWindow) ?? windows.last else {
                throw AnsightVisualTreeToolError.unavailable("No active UIWindow is available.")
            }

            if windows.count == 1 {
                return buildNode(view: activeWindow, window: activeWindow, includeProperties: includeProperties)
            }

            let children = windows.map { window in
                buildNode(view: window, window: window, includeProperties: includeProperties)
            }
            let frames = windows.map { window in
                window.convert(window.bounds, to: nil)
            }
            let left = frames.map(\.minX).min() ?? 0
            let top = frames.map(\.minY).min() ?? 0
            let right = frames.map(\.maxX).max() ?? 0
            let bottom = frames.map(\.maxY).max() ?? 0
            return AnsightVisualNode(
                id: "windows",
                type: "UIWindowCollection",
                automationId: nil,
                label: "Application windows",
                role: "group",
                supportedActions: [],
                visible: true,
                enabled: true,
                focusable: false,
                bounds: AnsightVisualTreeBounds(
                    x: Double(left),
                    y: Double(top),
                    width: Double(max(0, right - left)),
                    height: Double(max(0, bottom - top))
                ),
                visual: [:],
                z: nil,
                properties: [:],
                children: children
            )
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
            let snapshotFormat: AnsightScreenSnapshotFormat = format == "jpeg" ? .jpeg : .png
            let snapshot: AnsightScreenSnapshot
            if afterScreenUpdates {
                snapshot = try waitForMainActorCapture {
                    try await AnsightScreenSnapshotRenderer.captureIncludingGpuBackedSurfaces(
                        format: snapshotFormat,
                        quality: quality,
                        maxWidth: maxWidth,
                        afterScreenUpdates: true
                    )
                }
            } else {
                snapshot = try runOnMainActor {
                    try AnsightScreenSnapshotRenderer.capture(
                        format: snapshotFormat,
                        quality: quality,
                        maxWidth: maxWidth,
                        afterScreenUpdates: false
                    )
                }
            }

            return AnsightVisualTreeScreenshot(
                format: format,
                width: snapshot.width,
                height: snapshot.height,
                data: snapshot.data,
                annotationApplied: false
            )
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

    private static func waitForMainActorCapture(
        _ action: @escaping @MainActor @Sendable () async throws -> AnsightScreenSnapshot
    ) throws -> AnsightScreenSnapshot {
        let resultBox = AnsightScreenSnapshotResultBox()
        let semaphore = DispatchSemaphore(value: 0)

        Task { @MainActor in
            do {
                resultBox.store(.success(try await action()))
            } catch {
                resultBox.store(.failure(error))
            }
            semaphore.signal()
        }

        if Thread.isMainThread {
            while semaphore.wait(timeout: .now()) != .success {
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.01))
            }
        } else {
            semaphore.wait()
        }

        guard let result = resultBox.take() else {
            throw AnsightVisualTreeToolError.unavailable("Screenshot capture did not produce a result.")
        }
        return try result.get()
    }

    @MainActor
    static func activeWindow() -> UIWindow? {
        let windows = activeWindows()
        return windows.first { $0.isKeyWindow } ?? windows.last
    }

    @MainActor
    static func activeWindows() -> [UIWindow] {
        let scenes = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .filter { scene in
                scene.activationState == .foregroundActive || scene.activationState == .foregroundInactive
            }
        let sceneWindows = scenes.flatMap(\.windows)
        let candidates = sceneWindows.isEmpty ? UIApplication.shared.windows : sceneWindows
        var seen = Set<ObjectIdentifier>()
        return candidates
            .filter { window in
                !window.isHidden
                    && window.alpha > 0
                    && seen.insert(ObjectIdentifier(window)).inserted
            }
            .sorted { left, right in
                if left.windowLevel.rawValue == right.windowLevel.rawValue {
                    return !left.isKeyWindow && right.isKeyWindow
                }
                return left.windowLevel.rawValue < right.windowLevel.rawValue
            }
    }

    @MainActor
    static func buildNode(view: UIView, window: UIWindow, includeProperties: Bool) -> AnsightVisualNode {
        let frame = view.convert(view.bounds, to: window)
        let properties = includeProperties ? propertiesForView(view) : [:]
        return AnsightVisualNode(
            id: nodeId(for: view),
            type: String(describing: type(of: view)),
            automationId: normalized(view.accessibilityIdentifier),
            label: label(for: view),
            role: role(for: view),
            supportedActions: supportedActions(for: view),
            visible: !view.isHidden && view.alpha > 0,
            enabled: isEnabled(view),
            focusable: view.canBecomeFocused || view.isAccessibilityElement,
            bounds: AnsightVisualTreeBounds(
                x: Double(frame.origin.x),
                y: Double(frame.origin.y),
                width: Double(frame.width),
                height: Double(frame.height)
            ),
            visual: visualForView(view),
            z: zIndexForView(view),
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

        if let textField = view as? UITextField {
            if textField.isSecureTextEntry {
                return normalized(textField.placeholder)
                    ?? normalized(view.accessibilityLabel)
                    ?? normalized(view.accessibilityIdentifier)
            }
            if let text = normalized(textField.text) {
                return text
            }
        }

        if let textView = view as? UITextView, let text = normalized(textView.text) {
            return text
        }

        return normalized(view.accessibilityLabel) ?? normalized(view.accessibilityIdentifier)
    }

    @MainActor
    private static func role(for view: UIView) -> String {
        switch view {
        case is UIButton:
            return "button"
        case is UITextField, is UITextView:
            return "textbox"
        case is UISwitch:
            return "switch"
        case is UISlider:
            return "slider"
        case is UILabel:
            return "text"
        case is UIScrollView:
            return "scrollview"
        default:
            return "view"
        }
    }

    @MainActor
    private static func supportedActions(for view: UIView) -> [String] {
        var actions: [String] = []
        if view is UIControl || view.isAccessibilityElement {
            actions.append("tap")
        }
        if view is UITextField || view is UITextView {
            actions.append(contentsOf: ["typeText", "focus"])
        }
        if view is UIScrollView {
            actions.append(contentsOf: ["scroll", "swipe"])
        }
        return actions
    }

    @MainActor
    private static func visualForView(_ view: UIView) -> [String: JSONValue] {
        var visual: [String: JSONValue] = [
            "opacity": .number(Double(max(0, min(1, view.alpha)))),
        ]

        if let backgroundColor = view.backgroundColor {
            visual["background"] = .string(hexColor(backgroundColor))
        }
        if let foregroundColor = foregroundColor(for: view) {
            visual["foreground"] = .string(hexColor(foregroundColor))
        }
        if let text = visualText(for: view) {
            visual["text"] = .string(text)
        }

        switch view {
        case let textField as UITextField where !textField.isSecureTextEntry:
            if let value = normalizedVisualText(textField.text) {
                visual["value"] = .string(value)
            }
        case let textView as UITextView:
            if let value = normalizedVisualText(textView.text) {
                visual["value"] = .string(value)
            }
        case let toggle as UISwitch:
            visual["value"] = .string(toggle.isOn ? "true" : "false")
        case let slider as UISlider:
            visual["value"] = .string(String(slider.value))
        case let stepper as UIStepper:
            visual["value"] = .string(String(stepper.value))
        default:
            break
        }

        return visual
    }

    @MainActor
    private static func zIndexForView(_ view: UIView) -> Double? {
        let zIndex = Double(view.layer.zPosition)
        return zIndex == 0 ? nil : zIndex
    }

    @MainActor
    private static func foregroundColor(for view: UIView) -> UIColor? {
        switch view {
        case let label as UILabel:
            return label.textColor
        case let button as UIButton:
            return button.titleColor(for: button.state) ?? button.titleColor(for: .normal)
        case let textField as UITextField:
            return textField.textColor
        case let textView as UITextView:
            return textView.textColor
        default:
            return nil
        }
    }

    @MainActor
    private static func visualText(for view: UIView) -> String? {
        switch view {
        case let label as UILabel:
            return normalizedVisualText(label.text)
        case let button as UIButton:
            return normalizedVisualText(button.currentTitle)
        case let textField as UITextField:
            return normalizedVisualText(textField.placeholder)
        default:
            return nil
        }
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

    private static func normalizedVisualText(_ value: String?) -> String? {
        guard let value = normalized(value) else {
            return nil
        }

        return value.count <= 240 ? value : String(value.prefix(240)) + "..."
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

#if canImport(UIKit)
private final class AnsightScreenSnapshotResultBox: @unchecked Sendable {
    private let lock = NSLock()
    private var result: Result<AnsightScreenSnapshot, Error>?

    func store(_ result: Result<AnsightScreenSnapshot, Error>) {
        lock.withLock {
            self.result = result
        }
    }

    func take() -> Result<AnsightScreenSnapshot, Error>? {
        lock.withLock {
            defer { result = nil }
            return result
        }
    }
}
#endif
