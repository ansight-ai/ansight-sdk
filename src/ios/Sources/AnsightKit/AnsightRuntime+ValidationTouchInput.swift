import Foundation

@_spi(AnsightValidation)
public extension AnsightRuntime {
    func recordValidationTouchInput(
        action: String,
        pointerId: Int64 = 1,
        pointerIndex: Int = 0,
        pointerCount: Int = 1,
        x: Double,
        y: Double,
        surfaceWidth: Double? = nil,
        surfaceHeight: Double? = nil,
        coordinateUnit: String = "points",
        surfaceScale: Double? = nil,
        capturedAt: Date = Date()
    ) {
        recordCapturedTouch(
            AnsightCapturedTouch(
                action: ansightValidationTouchAction(from: action),
                pointerId: pointerId,
                pointerIndex: pointerIndex,
                pointerCount: pointerCount,
                x: x,
                y: y,
                surfaceWidth: surfaceWidth,
                surfaceHeight: surfaceHeight,
                coordinateUnit: coordinateUnit,
                surfaceScale: surfaceScale,
                capturedAt: capturedAt
            )
        )
    }
}

private func ansightValidationTouchAction(from action: String) -> AnsightCapturedTouchAction {
    switch action.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
    case "down", "began", "begin", "start", "started":
        return .down
    case "move", "moved", "drag":
        return .move
    case "up", "ended", "end", "complete", "completed":
        return .up
    case "cancel", "cancelled", "canceled":
        return .cancel
    default:
        return .unknown
    }
}
