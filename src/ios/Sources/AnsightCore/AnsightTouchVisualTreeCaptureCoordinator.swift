import Foundation

enum AnsightTouchVisualTreeGesturePhase: String, Sendable {
    case started
    case checkpoint
    case ended
    case cancelled
}

struct AnsightTouchVisualTreeCaptureTrigger: Sendable {
    let gestureId: String
    let touchAction: String
    let gesturePhase: AnsightTouchVisualTreeGesturePhase
    let touchCapturedAtUtc: String
}

final class AnsightTouchVisualTreeCaptureCoordinator: @unchecked Sendable {
    static let defaultCheckpointIntervalNanoseconds: UInt64 = 250_000_000

    private let capture: @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    private let configuredCheckpointIntervalNanoseconds: UInt64
    private let lock = NSLock()
    private var activePointerIds: Set<Int64> = []
    private var latestTouch: AnsightCapturedTouch?
    private var gestureId: String?
    private var checkpointTask: Task<Void, Never>?
    private var captureTask: Task<Void, Never>?
    private var checkpointQueued = false
    private var closed = false

    init(
        checkpointIntervalNanoseconds: UInt64 = defaultCheckpointIntervalNanoseconds,
        capture: @escaping @Sendable (AnsightTouchVisualTreeCaptureTrigger) async -> Void
    ) {
        precondition(checkpointIntervalNanoseconds > 0)
        configuredCheckpointIntervalNanoseconds = checkpointIntervalNanoseconds
        self.capture = capture
    }

    func observe(_ touch: AnsightCapturedTouch) {
        var trigger: AnsightTouchVisualTreeCaptureTrigger?
        var shouldStartCheckpoints = false
        var checkpointTaskToCancel: Task<Void, Never>?

        lock.withLock {
            guard !closed else {
                return
            }

            latestTouch = touch
            switch touch.action {
            case .down:
                let beginsGesture = activePointerIds.isEmpty
                activePointerIds.insert(touch.pointerId)
                if beginsGesture {
                    gestureId = "gesture-\(UUID().uuidString.lowercased())"
                    shouldStartCheckpoints = true
                }
                trigger = createTrigger(touch, phase: beginsGesture ? .started : .checkpoint)
            case .move:
                activePointerIds.insert(touch.pointerId)
            case .up:
                activePointerIds.remove(touch.pointerId)
                if activePointerIds.isEmpty {
                    checkpointTaskToCancel = checkpointTask
                    checkpointTask = nil
                    trigger = createTrigger(touch, phase: .ended)
                } else {
                    trigger = createTrigger(touch, phase: .checkpoint)
                }
            case .cancel:
                activePointerIds.removeAll()
                checkpointTaskToCancel = checkpointTask
                checkpointTask = nil
                trigger = createTrigger(touch, phase: .cancelled)
            case .unknown:
                break
            }
        }

        checkpointTaskToCancel?.cancel()
        if shouldStartCheckpoints {
            startCheckpointLoop()
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
            latestTouch = nil
            gestureId = nil
            let tasks = [checkpointTask, captureTask].compactMap { $0 }
            checkpointTask = nil
            captureTask = nil
            checkpointQueued = false
            return tasks
        }
        tasks.forEach { $0.cancel() }
    }

    func interruptGesture() {
        let task = lock.withLock { () -> Task<Void, Never>? in
            activePointerIds.removeAll()
            latestTouch = nil
            gestureId = nil
            let task = checkpointTask
            checkpointTask = nil
            return task
        }
        task?.cancel()
    }

    private func startCheckpointLoop() {
        let checkpointIntervalNanoseconds = configuredCheckpointIntervalNanoseconds
        let task = Task { [weak self] in
            while !Task.isCancelled {
                do {
                    try await Task.sleep(nanoseconds: checkpointIntervalNanoseconds)
                } catch {
                    return
                }

                guard let self,
                      let trigger = self.checkpointTrigger()
                else {
                    return
                }
                self.enqueue(trigger)
            }
        }

        let shouldCancel = lock.withLock { () -> Bool in
            guard !closed, !activePointerIds.isEmpty else {
                return true
            }
            checkpointTask?.cancel()
            checkpointTask = task
            return false
        }
        if shouldCancel {
            task.cancel()
        }
    }

    private func checkpointTrigger() -> AnsightTouchVisualTreeCaptureTrigger? {
        lock.withLock {
            guard !closed,
                  !activePointerIds.isEmpty,
                  let latestTouch
            else {
                return nil
            }
            return createTrigger(latestTouch, phase: .checkpoint)
        }
    }

    private func enqueue(_ trigger: AnsightTouchVisualTreeCaptureTrigger) {
        lock.withLock {
            guard !closed else {
                return
            }

            if trigger.gesturePhase == .checkpoint {
                guard !checkpointQueued else {
                    return
                }
                checkpointQueued = true
            }

            let previousTask = captureTask
            captureTask = Task { [weak self] in
                await previousTask?.value
                guard let self else {
                    return
                }
                let isClosed = self.lock.withLock { () -> Bool in
                    if trigger.gesturePhase == .checkpoint {
                        self.checkpointQueued = false
                    }
                    return self.closed
                }
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
