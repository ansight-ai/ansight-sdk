import Foundation

#if canImport(UIKit)
import UIKit
#endif

enum AnsightScreenCapture {
    static func capture(options: AnsightSessionJpegCaptureOptions) async throws -> AnsightScreenCaptureResult {
        #if canImport(UIKit)
        let capturedAtUtc = AnsightClock.isoNow()
        let renderStarted = AnsightTiming.now()
        let renderedImage = try await MainActor.run {
            try AnsightScreenSnapshotRenderer.renderTargetImage(
                maxWidth: options.maxWidth,
                afterScreenUpdates: false,
                opaque: true,
                renderMode: options.captureGpuBackedSurfaces ? .hierarchy : .layer
            )
        }
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
