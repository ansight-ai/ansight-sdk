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
    static let defaultMinimumCaptureIntervalNanoseconds: UInt64 = 750_000_000

    private let capture: @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    private let minimumCaptureIntervalNanoseconds: UInt64
    private let lock = NSLock()
    private var activePointerIds: Set<Int64> = []
    private var pendingTrigger: AnsightTouchVisualTreeCaptureTrigger?
    private var gestureId: String?
    private var captureTask: Task<Void, Never>?
    private var closed = false

    init(
        minimumCaptureIntervalNanoseconds: UInt64 = AnsightTouchVisualTreeCaptureCoordinator.defaultMinimumCaptureIntervalNanoseconds,
        capture: @escaping @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    ) {
        precondition(minimumCaptureIntervalNanoseconds > 0)
        self.minimumCaptureIntervalNanoseconds = minimumCaptureIntervalNanoseconds
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
        let task = lock.withLock { () -> Task<Void, Never>? in
            guard !closed else {
                return nil
            }
            closed = true
            activePointerIds.removeAll()
            pendingTrigger = nil
            gestureId = nil
            let task = captureTask
            captureTask = nil
            return task
        }
        task?.cancel()
    }

    func interruptGesture() {
        lock.withLock {
            activePointerIds.removeAll()
            pendingTrigger = nil
            gestureId = nil
        }
    }

    private func enqueue(_ trigger: AnsightTouchVisualTreeCaptureTrigger) {
        lock.withLock {
            guard !closed else {
                return
            }

            pendingTrigger = Self.selectPendingTrigger(pendingTrigger, incoming: trigger)
            if captureTask == nil {
                captureTask = Task { [weak self] in
                    await self?.runCaptureLoop()
                }
            }
        }
    }

    private func runCaptureLoop() async {
        while !Task.isCancelled {
            guard let trigger = takePendingTrigger() else {
                return
            }

            await capture(trigger)
            do {
                try await Task.sleep(nanoseconds: minimumCaptureIntervalNanoseconds)
            } catch {
                return
            }
        }
    }

    private func takePendingTrigger() -> AnsightTouchVisualTreeCaptureTrigger? {
        lock.withLock {
            guard !closed,
                  let trigger = pendingTrigger
            else {
                captureTask = nil
                return nil
            }

            pendingTrigger = nil
            return trigger
        }
    }

    private static func selectPendingTrigger(
        _ pending: AnsightTouchVisualTreeCaptureTrigger?,
        incoming: AnsightTouchVisualTreeCaptureTrigger
    ) -> AnsightTouchVisualTreeCaptureTrigger {
        guard let pending else {
            return incoming
        }
        if incoming.gesturePhase == .started {
            return incoming
        }
        if pending.gesturePhase == .started {
            return pending
        }
        return incoming.gesturePhase == .ended ? incoming : pending
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
