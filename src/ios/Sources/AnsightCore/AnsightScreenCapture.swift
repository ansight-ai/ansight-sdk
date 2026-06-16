import Foundation

#if canImport(UIKit)
import UIKit
#endif

enum AnsightScreenCapture {
    @MainActor
    static func capture(options: AnsightSessionJpegCaptureOptions) throws -> AnsightCapturedScreenFrame {
        #if canImport(UIKit)
        let capturedAtUtc = AnsightClock.isoNow()
        let snapshot = try AnsightScreenSnapshotRenderer.capture(
            format: .jpeg,
            quality: options.quality,
            maxWidth: options.maxWidth,
            afterScreenUpdates: false
        )

        return AnsightCapturedScreenFrame(
            capturedAtUtc: capturedAtUtc,
            capturedAtEpochMilliseconds: AnsightClock.epochMilliseconds(fromISO8601: capturedAtUtc),
            width: snapshot.width,
            height: snapshot.height,
            quality: options.quality,
            jpegData: snapshot.data
        )
        #else
        throw AnsightScreenCaptureError.unavailable
        #endif
    }
}
