import Foundation

#if canImport(UIKit)
import ImageIO
import UIKit
#if canImport(WebKit)
import WebKit
#endif
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
            // Keep the intermediate bitmap transparent even for JPEG output.
            // On physical devices, an opaque bitmap can make drawHierarchy
            // omit CAMetalLayer content such as Flutter/Impeller.
            // ImageIO removes the alpha channel when the bitmap is encoded.
            opaque: false,
            renderMode: .hierarchy
        )
        let data = try encode(renderedImage, format: format, quality: quality)
        return AnsightScreenSnapshot(width: renderedImage.width, height: renderedImage.height, data: data)
        #else
        throw AnsightScreenCaptureError.unavailable
        #endif
    }

    @MainActor
    public static func captureIncludingGpuBackedSurfaces(
        format: AnsightScreenSnapshotFormat = .jpeg,
        quality: Int = AnsightSessionJpegCaptureOptions.defaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.defaultMaxWidth,
        afterScreenUpdates: Bool = true
    ) async throws -> AnsightScreenSnapshot {
        #if canImport(UIKit)
        let renderedImage = try await renderTargetImageForCapture(
            maxWidth: maxWidth,
            afterScreenUpdates: afterScreenUpdates,
            // See capture(format:quality:maxWidth:afterScreenUpdates:).
            opaque: false,
            renderMode: .hierarchy,
            captureGpuBackedSurfaces: true
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

    @MainActor
    static func renderTargetImageForCapture(
        maxWidth: Int?,
        afterScreenUpdates: Bool,
        opaque: Bool,
        renderMode: AnsightScreenSnapshotRenderMode = .hierarchy,
        captureGpuBackedSurfaces: Bool
    ) async throws -> AnsightRenderedScreenImage {
        let renderedImage = try renderTargetImage(
            maxWidth: maxWidth,
            afterScreenUpdates: afterScreenUpdates,
            opaque: opaque,
            renderMode: renderMode
        )

        guard captureGpuBackedSurfaces else {
            return renderedImage
        }

        #if canImport(WebKit)
        return try await compositeWebViewSnapshots(
            over: renderedImage,
            maxWidth: maxWidth,
            opaque: opaque
        )
        #else
        return renderedImage
        #endif
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
        let windowSnapshot = bitmapImage(size: geometry.targetSize, opaque: opaque) { context in
            prepareOutput(in: context, bounds: outputBounds, opaque: opaque)
            context.scaleBy(x: geometry.scaleX, y: geometry.scaleY)
            for window in windows where window.screen === referenceWindow.screen {
                draw(
                    window: window,
                    relativeTo: referenceWindow,
                    afterScreenUpdates: afterScreenUpdates,
                    renderMode: renderMode,
                    context: context
                )
            }
        }

        if let windowSnapshot, windowSnapshot.cgImage != nil {
            return windowSnapshot
        }

        return renderScreenSnapshot(
            screen: referenceWindow.screen,
            bounds: bounds,
            targetSize: geometry.targetSize,
            opaque: opaque
        )
    }

    #if canImport(WebKit)
    @MainActor
    private static func compositeWebViewSnapshots(
        over renderedImage: AnsightRenderedScreenImage,
        maxWidth: Int?,
        opaque: Bool
    ) async throws -> AnsightRenderedScreenImage {
        let windows = foregroundWindows()
        guard let referenceWindow = referenceWindow(from: windows) else {
            return renderedImage
        }

        let geometry = renderGeometry(for: referenceWindow, maxWidth: maxWidth)
        var snapshots: [WebViewSnapshot] = []

        for window in windows where window.screen === referenceWindow.screen {
            for webView in visibleWebViews(in: window) {
                let frame = webView.convert(webView.bounds, to: referenceWindow)
                guard frame.width > 0,
                      frame.height > 0,
                      frame.intersects(referenceWindow.bounds),
                      let image = await snapshot(webView: webView)
                else {
                    continue
                }

                snapshots.append(WebViewSnapshot(frame: frame, image: image))
            }
        }

        guard !snapshots.isEmpty else {
            return renderedImage
        }

        let outputBounds = CGRect(origin: .zero, size: geometry.targetSize)
        let image = bitmapImage(size: geometry.targetSize, opaque: opaque) { _ in
            UIImage(cgImage: renderedImage.cgImage).draw(in: outputBounds)
            for snapshot in snapshots {
                snapshot.image.draw(
                    in: CGRect(
                        x: snapshot.frame.origin.x * geometry.scaleX,
                        y: snapshot.frame.origin.y * geometry.scaleY,
                        width: snapshot.frame.width * geometry.scaleX,
                        height: snapshot.frame.height * geometry.scaleY
                    )
                )
            }
        }

        guard let cgImage = image?.cgImage else {
            return renderedImage
        }

        return AnsightRenderedScreenImage(cgImage: cgImage, width: cgImage.width, height: cgImage.height)
    }

    @MainActor
    private static func visibleWebViews(in rootView: UIView) -> [WKWebView] {
        var webViews: [WKWebView] = []

        func visit(_ view: UIView, ancestorsVisible: Bool) {
            let isVisible = ancestorsVisible
                && !view.isHidden
                && view.alpha > 0.01
                && view.bounds.width > 0
                && view.bounds.height > 0

            guard isVisible else {
                return
            }

            if let webView = view as? WKWebView {
                webViews.append(webView)
                return
            }

            for subview in view.subviews {
                visit(subview, ancestorsVisible: isVisible)
            }
        }

        visit(rootView, ancestorsVisible: true)
        return webViews
    }

    @MainActor
    private static func snapshot(webView: WKWebView) async -> UIImage? {
        let configuration = WKSnapshotConfiguration()
        configuration.rect = webView.bounds
        configuration.afterScreenUpdates = true

        return await withCheckedContinuation { continuation in
            webView.takeSnapshot(with: configuration) { image, _ in
                continuation.resume(returning: image)
            }
        }
    }
    #endif

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
        return bitmapImage(size: targetSize, opaque: opaque) { context in
            prepareOutput(in: context, bounds: outputBounds, opaque: opaque)
            context.scaleBy(x: targetSize.width / bounds.width, y: targetSize.height / bounds.height)
            if !snapshotView.drawHierarchy(in: bounds, afterScreenUpdates: true) {
                snapshotView.layer.render(in: context)
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

    private static func bitmapImage(
        size: CGSize,
        opaque: Bool,
        draw: (CGContext) -> Void
    ) -> UIImage? {
        let width = max(1, Int(size.width.rounded()))
        let height = max(1, Int(size.height.rounded()))
        let bytesPerPixel = 4
        let bytesPerRow = bytesPerPixel * width
        let byteCount = height * bytesPerRow

        guard let rawData = calloc(byteCount, MemoryLayout<UInt8>.size) else {
            return nil
        }
        defer {
            free(rawData)
        }

        var bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue
        if opaque {
            bitmapInfo = CGImageAlphaInfo.noneSkipLast.rawValue
        }

        guard let context = CGContext(
            data: rawData,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: bytesPerRow,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: bitmapInfo
        ) else {
            return nil
        }

        // UIKit draws in a top-left coordinate system. A fresh bitmap context
        // is bottom-left, so align it before making it the current context.
        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: 1, y: -1)
        UIGraphicsPushContext(context)
        draw(context)
        UIGraphicsPopContext()

        guard let image = context.makeImage() else {
            return nil
        }

        return UIImage(cgImage: image, scale: 1, orientation: .up)
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

    #if canImport(WebKit)
    private struct WebViewSnapshot {
        let frame: CGRect
        let image: UIImage
    }
    #endif

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
