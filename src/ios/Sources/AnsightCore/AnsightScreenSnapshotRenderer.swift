import Foundation

#if canImport(UIKit)
import UIKit
#endif

public enum AnsightScreenSnapshotRenderer {
    @MainActor
    public static func capture(
        format: AnsightScreenSnapshotFormat = .jpeg,
        quality: Int = 80,
        maxWidth: Int? = nil,
        afterScreenUpdates: Bool = false
    ) throws -> AnsightScreenSnapshot {
        #if canImport(UIKit)
        guard let image = renderVisibleWindows(afterScreenUpdates: afterScreenUpdates) else {
            throw AnsightScreenCaptureError.noWindow
        }

        let scaledImage = scaleIfNeeded(image, maxWidth: maxWidth)
        guard let cgImage = scaledImage.cgImage else {
            throw AnsightScreenCaptureError.encodingFailed
        }

        let data: Data?
        switch format {
        case .jpeg:
            data = scaledImage.jpegData(compressionQuality: CGFloat(max(1, min(quality, 100))) / 100.0)
        case .png:
            data = scaledImage.pngData()
        }

        guard let data, !data.isEmpty else {
            throw AnsightScreenCaptureError.encodingFailed
        }

        return AnsightScreenSnapshot(width: cgImage.width, height: cgImage.height, data: data)
        #else
        throw AnsightScreenCaptureError.unavailable
        #endif
    }

    #if canImport(UIKit)
    @MainActor
    private static func renderVisibleWindows(afterScreenUpdates: Bool) -> UIImage? {
        let windows = foregroundWindows()
        guard let referenceWindow = referenceWindow(from: windows) else {
            return nil
        }

        let bounds = referenceWindow.bounds
        guard bounds.width > 0, bounds.height > 0 else {
            return nil
        }

        let format = UIGraphicsImageRendererFormat()
        format.scale = referenceWindow.screen.scale > 0 ? referenceWindow.screen.scale : UIScreen.main.scale
        format.opaque = false

        let windowSnapshot = UIGraphicsImageRenderer(bounds: bounds, format: format).image { context in
            context.cgContext.clear(bounds)
            for window in windows where window.screen === referenceWindow.screen {
                draw(window: window, relativeTo: referenceWindow, afterScreenUpdates: afterScreenUpdates, context: context.cgContext)
            }
        }

        if windowSnapshot.cgImage != nil {
            return windowSnapshot
        }

        return renderScreenSnapshot(screen: referenceWindow.screen, bounds: bounds)
    }

    @MainActor
    private static func renderScreenSnapshot(
        screen: UIScreen,
        bounds: CGRect
    ) -> UIImage? {
        // System input surfaces such as keyboards and date pickers are composited
        // outside app windows and only render reliably after a screen update pass.
        let snapshotView = screen.snapshotView(afterScreenUpdates: true)
        snapshotView.frame = bounds

        let format = UIGraphicsImageRendererFormat()
        format.scale = screen.scale > 0 ? screen.scale : UIScreen.main.scale
        format.opaque = false

        return UIGraphicsImageRenderer(bounds: bounds, format: format).image { context in
            if !snapshotView.drawHierarchy(in: bounds, afterScreenUpdates: true) {
                snapshotView.layer.render(in: context.cgContext)
            }
        }
    }

    @MainActor
    private static func foregroundWindows() -> [UIWindow] {
        let foregroundScenes = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .filter { scene in
                scene.activationState == .foregroundActive || scene.activationState == .foregroundInactive
            }

        var seen = Set<ObjectIdentifier>()
        var windows: [UIWindow] = []

        func append(_ window: UIWindow) {
            guard !window.isHidden,
                  window.alpha > 0.01,
                  window.bounds.width > 0,
                  window.bounds.height > 0
            else {
                return
            }

            let identifier = ObjectIdentifier(window)
            guard seen.insert(identifier).inserted else {
                return
            }

            windows.append(window)
        }

        for window in foregroundScenes.flatMap(\.windows) {
            append(window)
        }

        for window in UIApplication.shared.windows {
            append(window)
        }

        return windows.enumerated().sorted { left, right in
            let leftLevel = left.element.windowLevel.rawValue
            let rightLevel = right.element.windowLevel.rawValue
            if leftLevel == rightLevel {
                return left.offset < right.offset
            }
            return leftLevel < rightLevel
        }.map(\.element)
    }

    @MainActor
    private static func referenceWindow(from windows: [UIWindow]) -> UIWindow? {
        windows
            .filter { $0.screen === UIScreen.main }
            .sorted { left, right in
                let leftArea = left.bounds.width * left.bounds.height
                let rightArea = right.bounds.width * right.bounds.height
                if leftArea == rightArea {
                    return left.windowLevel.rawValue < right.windowLevel.rawValue
                }
                return leftArea > rightArea
            }
            .first
            ?? windows.first { $0.isKeyWindow }
            ?? windows.first
    }

    @MainActor
    private static func draw(
        window: UIWindow,
        relativeTo referenceWindow: UIWindow,
        afterScreenUpdates: Bool,
        context: CGContext
    ) {
        let frame = window === referenceWindow
            ? referenceWindow.bounds
            : window.convert(window.bounds, to: referenceWindow)

        guard frame.width > 0, frame.height > 0 else {
            return
        }

        context.saveGState()
        context.translateBy(x: frame.origin.x, y: frame.origin.y)
        context.setAlpha(window.alpha)

        if !window.drawHierarchy(in: CGRect(origin: .zero, size: frame.size), afterScreenUpdates: afterScreenUpdates) {
            window.layer.render(in: context)
        }

        context.restoreGState()
    }

    private static func scaleIfNeeded(_ image: UIImage, maxWidth: Int?) -> UIImage {
        guard let maxWidth,
              let cgImage = image.cgImage,
              cgImage.width > maxWidth
        else {
            return image
        }

        let targetWidth = maxWidth
        let targetHeight = max(1, Int((Double(cgImage.height) * Double(targetWidth) / Double(cgImage.width)).rounded()))
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = false

        return UIGraphicsImageRenderer(
            size: CGSize(width: targetWidth, height: targetHeight),
            format: format
        ).image { _ in
            image.draw(in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight))
        }
    }
    #endif
}
