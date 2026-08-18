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
        record(touches: touches, action: .down)
        let beginsGesture = activeTouches.isEmpty
        activeTouches.formUnion(touches.map(ObjectIdentifier.init))
        state = beginsGesture ? .began : .changed
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent) {
        if options.captureMoveEvents {
            record(touches: touches, action: .move)
        }
        state = .changed
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent) {
        record(touches: touches, action: .up)
        activeTouches.subtract(touches.map(ObjectIdentifier.init))
        state = activeTouches.isEmpty ? .ended : .changed
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent) {
        if options.captureCancelEvents {
            record(touches: touches, action: .cancel)
        }
        activeTouches.removeAll()
        state = .cancelled
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

    private func record(touches: Set<UITouch>, action: AnsightCapturedTouchAction) {
        guard let window = view as? UIWindow else {
            return
        }

        let pointerCount = touches.count
        for (pointerIndex, touch) in touches.enumerated() {
            let point = touch.location(in: window)
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
                capturedAt: Date()
            )

            guard moveThrottle.shouldRecord(capturedTouch) else {
                continue
            }

            recordTouch(capturedTouch)
            moveThrottle.observeRecorded(capturedTouch)
        }
    }

    private func pointerId(for touch: UITouch) -> Int64 {
        Int64(Int(bitPattern: Unmanaged.passUnretained(touch).toOpaque()))
    }
}
#endif
