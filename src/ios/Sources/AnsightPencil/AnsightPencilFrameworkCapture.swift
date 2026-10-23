import Foundation
import AnsightCore
import AnsightPencilRuntime

public extension AnsightPencil {
    /// Validates the playback environment independently of recording preferences.
    static func requireFrameworkPlayback() throws {
        let runtime = AnsightRuntime.shared
        guard isSupported, runtime.snapshot().initialized,
              runtime.hostConnectionStatus().isConnected else {
            throw NSError(domain: "AnsightPencil", code: 1, userInfo: [NSLocalizedDescriptionKey:
                "Framework Pencil playback requires iOS Simulator and a connected Ansight session."])
        }
    }

    /// Records framework-delivered input; it does not install hooks or manufacture UIKit touches.
    static func captureFrameworkSample(_ json: String) throws {
        let runtime = AnsightRuntime.shared
        // Recording is optional. Never enable capture as a side effect of playback.
        guard isSupported, runtime.snapshot().initialized,
              runtime.snapshot().touchCaptureActive, runtime.hostConnectionStatus().isConnected else { return }
        guard let data = json.data(using: .utf8),
              let payload = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let sample = payload["sample"] as? [String: Any],
              let geometry = payload["geometry"] as? [String: Any],
              let pointer = payload["pointerId"] as? NSNumber,
              let phase = sample["phase"] as? String,
              let x = sample["x"] as? Double, let y = sample["y"] as? Double,
              let width = geometry["viewportWidth"] as? Double,
              let height = geometry["viewportHeight"] as? Double,
              let scale = geometry["scale"] as? Double else {
            throw NSError(domain: "AnsightPencil", code: 2, userInfo: [NSLocalizedDescriptionKey: "Invalid framework Pencil capture."])
        }
        var details: [String: Any] = ["tool": "stylus", "sampleKind": phase.hasPrefix("hover") ? "hover" : "primary",
            "inputSource": "simulator-sdk-framework", "maximumPossibleForce": 1]
        for key in ["altitudeRadians", "azimuthRadians", "rollRadians", "distance"] { details[key] = sample[key] }
        details["force"] = sample["pressure"]
        let encoded = try JSONSerialization.data(withJSONObject: details)
        let result = AnsightRuntime.shared.submitTouchInput(action: phase, pointerId: pointer.int64Value,
            x: x * width, y: y * height, surfaceWidth: width, surfaceHeight: height, surfaceScale: scale,
            detailsJson: String(decoding: encoded, as: UTF8.self))
        guard result.success else {
            throw NSError(domain: "AnsightPencil", code: 3, userInfo: [NSLocalizedDescriptionKey: result.message])
        }
    }
}
