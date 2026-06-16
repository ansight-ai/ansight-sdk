import Foundation

final class AnsightTouchMoveThrottle: @unchecked Sendable {
    private let options: AnsightTouchCaptureOptions
    private var lastRecordedTouchByPointerId: [Int64: AnsightCapturedTouch] = [:]

    init(options: AnsightTouchCaptureOptions) {
        self.options = options
    }

    func shouldRecord(_ touch: AnsightCapturedTouch) -> Bool {
        guard touch.action == .move else {
            return true
        }

        guard let previousTouch = lastRecordedTouchByPointerId[touch.pointerId] else {
            return true
        }

        return hasReachedFrameInterval(previousTouch: previousTouch, touch: touch)
            && hasMovedEnough(previousTouch: previousTouch, touch: touch)
    }

    func observeRecorded(_ touch: AnsightCapturedTouch) {
        switch touch.action {
        case .down, .move:
            lastRecordedTouchByPointerId[touch.pointerId] = touch
        case .up, .cancel, .unknown:
            lastRecordedTouchByPointerId.removeValue(forKey: touch.pointerId)
        }
    }

    private func hasReachedFrameInterval(previousTouch: AnsightCapturedTouch, touch: AnsightCapturedTouch) -> Bool {
        let framesPerSecond = options.moveCaptureFramesPerSecond < 0
            ? AnsightTouchCaptureOptions.defaultMoveCaptureFramesPerSecond
            : options.moveCaptureFramesPerSecond
        guard framesPerSecond > 0 else {
            return true
        }

        let minimumInterval = 1.0 / Double(framesPerSecond)
        return touch.capturedAt.timeIntervalSince(previousTouch.capturedAt) >= minimumInterval
    }

    private func hasMovedEnough(previousTouch: AnsightCapturedTouch, touch: AnsightCapturedTouch) -> Bool {
        let threshold = distanceThreshold(for: touch)
        guard threshold > 0 else {
            return true
        }

        let deltaX = touch.x - previousTouch.x
        let deltaY = touch.y - previousTouch.y
        return (deltaX * deltaX) + (deltaY * deltaY) >= threshold * threshold
    }

    private func distanceThreshold(for touch: AnsightCapturedTouch) -> Double {
        var threshold = options.moveCaptureDistanceThreshold
        if !threshold.isFinite || threshold < 0 {
            threshold = AnsightTouchCaptureOptions.defaultMoveCaptureDistanceThreshold
        }

        guard threshold > 0 else {
            return 0
        }

        if touch.coordinateUnit.caseInsensitiveCompare("pixels") == .orderedSame,
           let surfaceScale = touch.surfaceScale,
           surfaceScale.isFinite,
           surfaceScale > 0 {
            return threshold * surfaceScale
        }

        return threshold
    }
}
