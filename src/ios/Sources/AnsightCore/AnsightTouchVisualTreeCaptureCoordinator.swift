import Foundation

enum AnsightTouchVisualTreeGesturePhase: String, Sendable {
    case started
    case checkpoint
    case ended
}

struct AnsightTouchVisualTreeCaptureTrigger: Sendable {
    let gestureId: String
    let touchAction: String
    let gesturePhase: AnsightTouchVisualTreeGesturePhase
    let touchCapturedAtUtc: String
}

final class AnsightTouchVisualTreeCaptureCoordinator: @unchecked Sendable {
    private let capture: @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    private let lock = NSLock()
    private var activePointerIds: Set<Int64> = []
    private var gestureId: String?
    private var captureTask: Task<Void, Never>?
    private var closed = false

    init(
        capture: @escaping @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    ) {
        self.capture = capture
    }

    func observe(_ touch: AnsightCapturedTouch) {
        var trigger: AnsightTouchVisualTreeCaptureTrigger?

        lock.withLock {
            guard !closed else {
                return
            }

            switch touch.action {
            case .down:
                let beginsGesture = activePointerIds.isEmpty
                activePointerIds.insert(touch.pointerId)
                if beginsGesture {
                    gestureId = "gesture-\(UUID().uuidString.lowercased())"
                }
                trigger = createTrigger(touch, phase: beginsGesture ? .started : .checkpoint)
            case .move:
                activePointerIds.insert(touch.pointerId)
            case .up:
                activePointerIds.remove(touch.pointerId)
                if activePointerIds.isEmpty {
                    trigger = createTrigger(touch, phase: .ended)
                } else {
                    trigger = createTrigger(touch, phase: .checkpoint)
                }
            case .cancel:
                activePointerIds.removeAll()
                gestureId = nil
            case .unknown:
                break
            }
        }

        if let trigger {
            enqueue(trigger)
        }
    }

    func close() {
        let tasks = lock.withLock { () -> [Task<Void, Never>] in
            guard !closed else {
                return []
            }
            closed = true
            activePointerIds.removeAll()
            gestureId = nil
            let tasks = [captureTask].compactMap { $0 }
            captureTask = nil
            return tasks
        }
        tasks.forEach { $0.cancel() }
    }

    func interruptGesture() {
        lock.withLock {
            activePointerIds.removeAll()
            gestureId = nil
        }
    }

    private func enqueue(_ trigger: AnsightTouchVisualTreeCaptureTrigger) {
        lock.withLock {
            guard !closed else {
                return
            }

            let previousTask = captureTask
            captureTask = Task { [weak self] in
                await previousTask?.value
                guard let self else {
                    return
                }
                let isClosed = self.lock.withLock { self.closed }
                guard !Task.isCancelled,
                      !isClosed
                else {
                    return
                }
                await self.capture(trigger)
            }
        }
    }

    private func createTrigger(
        _ touch: AnsightCapturedTouch,
        phase: AnsightTouchVisualTreeGesturePhase
    ) -> AnsightTouchVisualTreeCaptureTrigger {
        AnsightTouchVisualTreeCaptureTrigger(
            gestureId: gestureId ?? "gesture-\(UUID().uuidString.lowercased())",
            touchAction: touchActionName(touch.action),
            gesturePhase: phase,
            touchCapturedAtUtc: touch.capturedAtUtc
        )
    }

    private func touchActionName(_ action: AnsightCapturedTouchAction) -> String {
        switch action {
        case .down:
            return "down"
        case .move:
            return "move"
        case .up:
            return "up"
        case .cancel:
            return "cancel"
        case .unknown:
            return "unknown"
        }
    }
}
