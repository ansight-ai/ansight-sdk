import Foundation

#if canImport(UIKit)
import UIKit
#endif

enum AnsightScreenCapture {
    static func capture(options: AnsightSessionJpegCaptureOptions) async throws -> AnsightScreenCaptureResult {
        #if canImport(UIKit)
        let capturedAtUtc = AnsightClock.isoNow()
        let keyboardPresent = options.captureKeyboardPresence
            ? await AnsightKeyboardPresenceTracker.shared.currentPresence()
            : nil
        let renderStarted = AnsightTiming.now()
        let renderedImage = try await AnsightScreenSnapshotRenderer.renderTargetImageForCapture(
            maxWidth: options.maxWidth,
            // WKWebView content is composited out-of-process. On physical
            // devices, drawing before the pending screen transaction is
            // committed produces a valid but entirely black image.
            afterScreenUpdates: options.captureGpuBackedSurfaces,
            // Flutter's CAMetalLayer is omitted by drawHierarchy when the
            // intermediate UIGraphicsImageRenderer is marked opaque.
            opaque: false,
            renderMode: options.captureGpuBackedSurfaces ? .hierarchy : .layer,
            captureGpuBackedSurfaces: options.captureGpuBackedSurfaces
        )
        let renderMilliseconds = AnsightTiming.elapsedMilliseconds(since: renderStarted)

        let encodeStarted = AnsightTiming.now()
        let jpegData = try AnsightScreenSnapshotRenderer.encode(
            renderedImage,
            format: .jpeg,
            quality: options.quality
        )
        let encodeMilliseconds = AnsightTiming.elapsedMilliseconds(since: encodeStarted)

        let frame = AnsightCapturedScreenFrame(
            capturedAtUtc: capturedAtUtc,
            capturedAtEpochMilliseconds: AnsightClock.epochMilliseconds(fromISO8601: capturedAtUtc),
            width: renderedImage.width,
            height: renderedImage.height,
            quality: options.quality,
            keyboardPresent: keyboardPresent,
            jpegData: jpegData
        )

        return AnsightScreenCaptureResult(
            frame: frame,
            renderMilliseconds: renderMilliseconds,
            encodeMilliseconds: encodeMilliseconds
        )
        #else
        throw AnsightScreenCaptureError.unavailable
        #endif
    }
}

struct AnsightScreenCaptureResult: Sendable, Equatable {
    let frame: AnsightCapturedScreenFrame
    let renderMilliseconds: Int
    let encodeMilliseconds: Int
}
