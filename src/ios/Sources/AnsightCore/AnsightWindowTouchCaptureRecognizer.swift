import Foundation

#if canImport(UIKit)
import UIKit

final class AnsightWindowTouchCaptureRecognizer: UIGestureRecognizer {
    private let options: AnsightTouchCaptureOptions
    private let moveThrottle: AnsightTouchMoveThrottle
    private let recordTouch: @Sendable (AnsightCapturedTouch) -> Void
    private var activeTouches: Set<ObjectIdentifier> = []

    init(
        options: AnsightTouchCaptureOptions,
        recordTouch: @escaping @Sendable (AnsightCapturedTouch) -> Void
    ) {
        self.options = options
        self.moveThrottle = AnsightTouchMoveThrottle(options: options)
        self.recordTouch = recordTouch
        super.init(target: nil, action: nil)
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        record(touches: touches, action: .down, event: event)
        let beginsGesture = activeTouches.isEmpty
        activeTouches.formUnion(touches.map(ObjectIdentifier.init))
        state = beginsGesture ? .began : .changed
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent) {
        if options.captureMoveEvents {
            record(touches: touches, action: .move, event: event)
        }
        state = .changed
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent) {
        record(touches: touches, action: .up, event: event)
        activeTouches.subtract(touches.map(ObjectIdentifier.init))
        state = activeTouches.isEmpty ? .ended : .changed
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent) {
        if options.captureCancelEvents {
            record(touches: touches, action: .cancel, event: event)
        }
        activeTouches.removeAll()
        state = .cancelled
    }

    override func touchesEstimatedPropertiesUpdated(_ touches: Set<UITouch>) {
        record(touches: touches, action: .move, event: nil, sampleKindOverride: "estimatedUpdate")
    }

    override func reset() {
        activeTouches.removeAll()
        super.reset()
    }

    override func canPrevent(_ preventedGestureRecognizer: UIGestureRecognizer) -> Bool {
        false
    }

    override func canBePrevented(by preventingGestureRecognizer: UIGestureRecognizer) -> Bool {
        false
    }

    private func record(
        touches: Set<UITouch>,
        action: AnsightCapturedTouchAction,
        event: UIEvent?,
        sampleKindOverride: String? = nil
    ) {
        guard let window = view as? UIWindow else {
            return
        }

        let pointerCount = touches.count
        for (pointerIndex, touch) in touches.enumerated() {
            let samples = action == .move && touch.type == .pencil && sampleKindOverride == nil
                ? (event?.coalescedTouches(for: touch) ?? [touch])
                : [touch]
            for sample in samples {
                let point = sample.location(in: window)
                let isPencil = sample.type == .pencil
                let details = AnsightTouchDetails(
                    tool: toolName(for: sample),
                    sampleKind: sampleKindOverride ?? (sample.timestamp == touch.timestamp ? "current" : "coalesced"),
                    force: isPencil ? Double(sample.force) : nil,
                    maximumPossibleForce: isPencil ? Double(sample.maximumPossibleForce) : nil,
                    altitudeRadians: isPencil ? Double(sample.altitudeAngle) : nil,
                    azimuthRadians: isPencil ? Double(sample.azimuthAngle(in: window)) : nil,
                    rollRadians: isPencil ? rollAngle(for: sample) : nil,
                    distance: nil,
                    estimatedProperties: isPencil ? Int64(sample.estimatedProperties.rawValue) : nil,
                    estimatedPropertiesExpectingUpdates: isPencil ? Int64(sample.estimatedPropertiesExpectingUpdates.rawValue) : nil,
                    estimationUpdateIndex: isPencil ? sample.estimationUpdateIndex?.int64Value : nil
                )
                let capturedTouch = AnsightCapturedTouch(
                    action: action,
                    pointerId: pointerId(for: touch),
                    pointerIndex: pointerIndex,
                    pointerCount: pointerCount,
                    x: point.x,
                    y: point.y,
                    surfaceWidth: window.bounds.width,
                    surfaceHeight: window.bounds.height,
                    coordinateUnit: "points",
                    surfaceScale: window.screen.scale,
                    capturedAt: Date(timeIntervalSinceNow: sample.timestamp - ProcessInfo.processInfo.systemUptime),
                    details: details
                )

                guard isPencil || moveThrottle.shouldRecord(capturedTouch) else {
                    continue
                }

                recordTouch(capturedTouch)
                moveThrottle.observeRecorded(capturedTouch)
            }
        }
    }

    private func pointerId(for touch: UITouch) -> Int64 {
        Int64(Int(bitPattern: Unmanaged.passUnretained(touch).toOpaque()))
    }

    private func rollAngle(for touch: UITouch) -> Double? {
        if #available(iOS 17.5, macCatalyst 17.5, *) {
            return Double(touch.rollAngle)
        }
        return nil
    }

    private func toolName(for touch: UITouch) -> String {
        switch touch.type {
        case .pencil: return "stylus"
        case .direct: return "finger"
        case .indirectPointer: return "mouse"
        default: return "unknown"
        }
    }
}

@available(iOS 16.4, macCatalyst 16.4, *)
final class AnsightWindowHoverCaptureRecognizer: UIHoverGestureRecognizer {
    private let recordTouch: @Sendable (AnsightCapturedTouch) -> Void

    init(recordTouch: @escaping @Sendable (AnsightCapturedTouch) -> Void) {
        self.recordTouch = recordTouch
        super.init(target: nil, action: nil)
        addTarget(self, action: #selector(captureHover))
        allowedTouchTypes = [NSNumber(value: UITouch.TouchType.pencil.rawValue)]
        cancelsTouchesInView = false
    }

    @objc private func captureHover() {
        guard let window = view as? UIWindow else {
            return
        }

        let action: AnsightCapturedTouchAction
        switch state {
        case .began: action = .hoverEnter
        case .changed: action = .hoverMove
        case .ended, .cancelled: action = .hoverExit
        default: return
        }

        let point = location(in: window)
        let roll: Double?
        if #available(iOS 17.5, macCatalyst 17.5, *) {
            roll = Double(rollAngle)
        } else {
            roll = nil
        }
        recordTouch(AnsightCapturedTouch(
            action: action,
            pointerId: Int64(Int(bitPattern: Unmanaged.passUnretained(self).toOpaque())),
            pointerIndex: 0,
            pointerCount: 1,
            x: point.x,
            y: point.y,
            surfaceWidth: window.bounds.width,
            surfaceHeight: window.bounds.height,
            coordinateUnit: "points",
            surfaceScale: window.screen.scale,
            details: AnsightTouchDetails(
                tool: "stylus",
                sampleKind: "hover",
                force: nil,
                maximumPossibleForce: nil,
                altitudeRadians: Double(altitudeAngle),
                azimuthRadians: Double(azimuthAngle(in: window)),
                rollRadians: roll,
                distance: Double(zOffset),
                estimatedProperties: nil,
                estimatedPropertiesExpectingUpdates: nil,
                estimationUpdateIndex: nil
            )
        ))
    }
}
#endif
