import AnsightCore
import Foundation

public extension AnsightOptions {
    static var ansightDeveloperDefaults: AnsightOptions {
        AnsightOptions(
            sampleFrequencyMilliseconds: 400,
            retentionPeriodSeconds: 120,
            enableFramesPerSecond: true,
            enableBatteryLevel: false,
            lifecycleCapture: .enabledDefault,
            sessionJpegCapture: AnsightSessionJpegCaptureOptions(
                intervalMilliseconds: 2_000,
                quality: 60,
                maxWidth: 480
            ),
            touchCapture: AnsightTouchCaptureOptions(),
            toolGuard: .fullAccess,
            hostAutoProbe: .enabledDefault
        )
    }
}
