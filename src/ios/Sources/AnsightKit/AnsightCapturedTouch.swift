import Foundation

struct AnsightCapturedTouch: Sendable, Codable, Equatable, Identifiable {
    let id: String
    let action: AnsightCapturedTouchAction
    let pointerId: Int64
    let pointerIndex: Int
    let pointerCount: Int
    let x: Double
    let y: Double
    let surfaceWidth: Double?
    let surfaceHeight: Double?
    let coordinateUnit: String
    let surfaceScale: Double?
    let capturedAt: Date

    init(
        id: String = UUID().uuidString.lowercased(),
        action: AnsightCapturedTouchAction,
        pointerId: Int64,
        pointerIndex: Int,
        pointerCount: Int,
        x: Double,
        y: Double,
        surfaceWidth: Double?,
        surfaceHeight: Double?,
        coordinateUnit: String,
        surfaceScale: Double?,
        capturedAt: Date = Date()
    ) {
        self.id = id
        self.action = action
        self.pointerId = pointerId
        self.pointerIndex = max(0, pointerIndex)
        self.pointerCount = max(1, pointerCount)
        self.x = x
        self.y = y
        self.surfaceWidth = surfaceWidth
        self.surfaceHeight = surfaceHeight
        self.coordinateUnit = coordinateUnit.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? "points"
            : coordinateUnit.trimmingCharacters(in: .whitespacesAndNewlines)
        self.surfaceScale = surfaceScale
        self.capturedAt = capturedAt
    }

    var coordinateSpace: String {
        "window"
    }

    var capturedAtUtc: String {
        AnsightClock.isoString(from: capturedAt)
    }

    var normalizedX: Double? {
        guard let surfaceWidth, surfaceWidth > 0 else {
            return nil
        }
        return x / surfaceWidth
    }

    var normalizedY: Double? {
        guard let surfaceHeight, surfaceHeight > 0 else {
            return nil
        }
        return y / surfaceHeight
    }
}
