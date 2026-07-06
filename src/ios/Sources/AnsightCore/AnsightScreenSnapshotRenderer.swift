import Foundation

#if canImport(UIKit)
import ImageIO
import UIKit
#endif

public enum AnsightScreenSnapshotRenderer {
    @MainActor
    public static func capture(
        format: AnsightScreenSnapshotFormat = .jpeg,
        quality: Int = AnsightSessionJpegCaptureOptions.defaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.defaultMaxWidth,
        afterScreenUpdates: Bool = false
    ) throws -> AnsightScreenSnapshot {
        #if canImport(UIKit)
        let renderedImage = try renderTargetImage(
            maxWidth: maxWidth,
            afterScreenUpdates: afterScreenUpdates,
            opaque: format == .jpeg,
            renderMode: .hierarchy
        )
        let data = try encode(renderedImage, format: format, quality: quality)
        return AnsightScreenSnapshot(width: renderedImage.width, height: renderedImage.height, data: data)
        #else
        throw AnsightScreenCaptureError.unavailable
        #endif
    }

    #if canImport(UIKit)
    @MainActor
    static func renderTargetImage(
        maxWidth: Int?,
        afterScreenUpdates: Bool,
        opaque: Bool,
        renderMode: AnsightScreenSnapshotRenderMode = .hierarchy
    ) throws -> AnsightRenderedScreenImage {
        guard let image = renderVisibleWindows(
            maxWidth: maxWidth,
            afterScreenUpdates: afterScreenUpdates,
            opaque: opaque,
            renderMode: renderMode
        ),
              let cgImage = image.cgImage
        else {
            throw AnsightScreenCaptureError.noWindow
        }

        return AnsightRenderedScreenImage(cgImage: cgImage, width: cgImage.width, height: cgImage.height)
    }

    static func encode(
        _ image: AnsightRenderedScreenImage,
        format: AnsightScreenSnapshotFormat,
        quality: Int
    ) throws -> Data {
        switch format {
        case .jpeg:
            return try encodeImage(
                image.cgImage,
                typeIdentifier: "public.jpeg" as CFString,
                properties: [
                    kCGImageDestinationLossyCompressionQuality as String: CGFloat(max(1, min(quality, 100))) / 100.0,
                ] as CFDictionary
            )
        case .png:
            return try encodeImage(image.cgImage, typeIdentifier: "public.png" as CFString, properties: nil)
        }
    }

    @MainActor
    private static func renderVisibleWindows(
        maxWidth: Int?,
        afterScreenUpdates: Bool,
        opaque: Bool,
        renderMode: AnsightScreenSnapshotRenderMode
    ) -> UIImage? {
        let windows = foregroundWindows()
        guard let referenceWindow = referenceWindow(from: windows) else {
            return nil
        }

        let bounds = referenceWindow.bounds
        guard bounds.width > 0, bounds.height > 0 else {
            return nil
        }

        let geometry = renderGeometry(for: referenceWindow, maxWidth: maxWidth)
        let outputBounds = CGRect(origin: .zero, size: geometry.targetSize)
        let format = rendererFormat(opaque: opaque)

        let windowSnapshot = imageRenderer(size: geometry.targetSize, format: format).image { context in
            prepareOutput(in: context.cgContext, bounds: outputBounds, opaque: opaque)
            context.cgContext.scaleBy(x: geometry.scaleX, y: geometry.scaleY)
            for window in windows where window.screen === referenceWindow.screen {
                draw(
                    window: window,
                    relativeTo: referenceWindow,
                    afterScreenUpdates: afterScreenUpdates,
                    renderMode: renderMode,
                    context: context.cgContext
                )
            }
        }

        if windowSnapshot.cgImage != nil {
            return windowSnapshot
        }

        return renderScreenSnapshot(
            screen: referenceWindow.screen,
            bounds: bounds,
            targetSize: geometry.targetSize,
            opaque: opaque
        )
    }

    @MainActor
    private static func renderScreenSnapshot(
        screen: UIScreen,
        bounds: CGRect,
        targetSize: CGSize,
        opaque: Bool
    ) -> UIImage? {
        // System input surfaces such as keyboards and date pickers are composited
        // outside app windows and only render reliably after a screen update pass.
        let snapshotView = screen.snapshotView(afterScreenUpdates: true)
        snapshotView.frame = bounds

        let outputBounds = CGRect(origin: .zero, size: targetSize)
        let format = rendererFormat(opaque: opaque)

        return imageRenderer(size: targetSize, format: format).image { context in
            prepareOutput(in: context.cgContext, bounds: outputBounds, opaque: opaque)
            context.cgContext.scaleBy(x: targetSize.width / bounds.width, y: targetSize.height / bounds.height)
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
        renderMode: AnsightScreenSnapshotRenderMode,
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

        switch renderMode {
        case .layer:
            window.layer.render(in: context)
        case .hierarchy:
            if !window.drawHierarchy(in: CGRect(origin: .zero, size: frame.size), afterScreenUpdates: afterScreenUpdates) {
                window.layer.render(in: context)
            }
        }

        context.restoreGState()
    }

    @MainActor
    private static var cachedRenderer: CachedImageRenderer?

    @MainActor
    private static func imageRenderer(size: CGSize, format: UIGraphicsImageRendererFormat) -> UIGraphicsImageRenderer {
        let width = max(1, Int(size.width.rounded()))
        let height = max(1, Int(size.height.rounded()))
        let opaque = format.opaque
        if let cachedRenderer, cachedRenderer.matches(width: width, height: height, opaque: opaque) {
            return cachedRenderer.renderer
        }

        let renderer = UIGraphicsImageRenderer(size: size, format: format)
        cachedRenderer = CachedImageRenderer(width: width, height: height, opaque: opaque, renderer: renderer)
        return renderer
    }

    private static func rendererFormat(opaque: Bool) -> UIGraphicsImageRendererFormat {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = opaque
        return format
    }

    private static func renderGeometry(for window: UIWindow, maxWidth: Int?) -> RenderGeometry {
        let bounds = window.bounds
        let scale = window.screen.scale > 0 ? window.screen.scale : UIScreen.main.scale
        let sourcePixelWidth = max(1, Int((bounds.width * scale).rounded()))
        let sourcePixelHeight = max(1, Int((bounds.height * scale).rounded()))
        let targetWidth = targetWidth(sourcePixelWidth: sourcePixelWidth, maxWidth: maxWidth)
        let targetHeight = max(1, Int((Double(sourcePixelHeight) * Double(targetWidth) / Double(sourcePixelWidth)).rounded()))
        let targetSize = CGSize(width: targetWidth, height: targetHeight)
        return RenderGeometry(
            targetSize: targetSize,
            scaleX: targetSize.width / bounds.width,
            scaleY: targetSize.height / bounds.height
        )
    }

    private static func targetWidth(sourcePixelWidth: Int, maxWidth: Int?) -> Int {
        guard let maxWidth,
              maxWidth > 0,
              sourcePixelWidth > maxWidth
        else {
            return sourcePixelWidth
        }
        return maxWidth
    }

    private static func prepareOutput(in context: CGContext, bounds: CGRect, opaque: Bool) {
        if opaque {
            context.setFillColor(CGColor(gray: 0, alpha: 1))
            context.fill(bounds)
        } else {
            context.clear(bounds)
        }
    }

    private static func encodeImage(
        _ image: CGImage,
        typeIdentifier: CFString,
        properties: CFDictionary?
    ) throws -> Data {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, typeIdentifier, 1, nil) else {
            throw AnsightScreenCaptureError.encodingFailed
        }

        CGImageDestinationAddImage(destination, image, properties)
        guard CGImageDestinationFinalize(destination), data.length > 0 else {
            throw AnsightScreenCaptureError.encodingFailed
        }

        return data as Data
    }

    private struct RenderGeometry {
        let targetSize: CGSize
        let scaleX: CGFloat
        let scaleY: CGFloat
    }

    private struct CachedImageRenderer {
        let width: Int
        let height: Int
        let opaque: Bool
        let renderer: UIGraphicsImageRenderer

        func matches(width: Int, height: Int, opaque: Bool) -> Bool {
            self.width == width && self.height == height && self.opaque == opaque
        }
    }
    #endif
}

#if canImport(UIKit)
struct AnsightRenderedScreenImage: @unchecked Sendable {
    let cgImage: CGImage
    let width: Int
    let height: Int
}

enum AnsightScreenSnapshotRenderMode: Sendable {
    case hierarchy
    case layer
}
#endif
