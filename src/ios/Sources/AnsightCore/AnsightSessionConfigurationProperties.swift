import Foundation

enum AnsightSessionConfigurationProperties {
    static let captureGroup = "ansight.capture"
    static let telemetryGroup = "ansight.telemetry"

    static func create(
        options: AnsightOptions,
        capturePolicy: HostSessionJpegCapturePolicy
    ) -> [String: [String: String]] {
        var properties = AnsightRuntime.normalizedCustomProperties(options.customProperties)
        properties[captureGroup] = captureProperties(
            options: options.sessionJpegCapture,
            capturePolicy: capturePolicy
        )
        properties[telemetryGroup] = [
            "schemaVersion": "1",
            "sampleFrequencyMilliseconds": String(options.sampleFrequencyMilliseconds),
            "retentionPeriodSeconds": String(options.retentionPeriodSeconds),
            "framesPerSecond": String(options.enableFramesPerSecond),
            "batteryLevel": String(options.enableBatteryLevel),
            "openFileHandles": String(options.enableOpenFileHandleTracking),
        ]
        return properties
    }

    private static func captureProperties(
        options: AnsightSessionJpegCaptureOptions?,
        capturePolicy: HostSessionJpegCapturePolicy
    ) -> [String: String] {
        var properties = [
            "schemaVersion": "1",
            "enabled": String(options != nil || capturePolicy.useHostCapture),
            "owner": capturePolicy.useHostCapture ? "host" : (options == nil ? "none" : "app"),
        ]

        if let options {
            properties["intervalMilliseconds"] = String(options.intervalMilliseconds)
            properties["quality"] = String(options.quality)
            properties["maxWidth"] = options.maxWidth.map(String.init) ?? "full"
            properties["captureGpuBackedSurfaces"] = String(options.captureGpuBackedSurfaces)
            properties["captureKeyboardPresence"] = String(options.captureKeyboardPresence)
            properties["mode"] = options.mode.rawValue
        }
        if let source = capturePolicy.source {
            properties["ownerSource"] = source
        }

        return properties
    }
}
