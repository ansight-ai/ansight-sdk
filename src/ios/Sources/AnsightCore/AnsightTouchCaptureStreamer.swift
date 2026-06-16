import Foundation

final class AnsightTouchCaptureStreamer: @unchecked Sendable {
    private let transport: PairingLiveSessionTransport
    private let resultHandler: @Sendable (Int, OperationResult) -> Void
    private let lock = NSLock()

    private var pendingTouches: [AnsightCapturedTouch] = []
    private var pumpTask: Task<Void, Never>?
    private var streaming = false

    init(
        transport: PairingLiveSessionTransport,
        resultHandler: @escaping @Sendable (Int, OperationResult) -> Void
    ) {
        self.transport = transport
        self.resultHandler = resultHandler
    }

    var isStreaming: Bool {
        lock.withLock { streaming }
    }

    func start() -> OperationResult {
        guard transport.isOpen else {
            return .failure("WebSocket session is not open.")
        }

        lock.withLock {
            pendingTouches.removeAll()
            streaming = true
        }
        return .success("Touch capture streaming started.")
    }

    func stop() -> OperationResult {
        let task = lock.withLock { () -> Task<Void, Never>? in
            streaming = false
            pendingTouches.removeAll()
            let task = pumpTask
            pumpTask = nil
            return task
        }
        task?.cancel()
        return .success("Touch capture streaming stopped.")
    }

    func record(_ touch: AnsightCapturedTouch) {
        lock.withLock {
            guard streaming else {
                return
            }

            pendingTouches.append(touch)
            if pendingTouches.count > AnsightTouchInputWireProtocol.maxPendingTouches {
                pendingTouches.removeFirst(pendingTouches.count - AnsightTouchInputWireProtocol.maxPendingTouches)
            }

            if pumpTask == nil {
                pumpTask = Task { [weak self] in
                    await self?.runPump()
                }
            }
        }
    }

    private func runPump() async {
        while !Task.isCancelled {
            let batch = lock.withLock { () -> [AnsightCapturedTouch] in
                guard streaming, !pendingTouches.isEmpty else {
                    pumpTask = nil
                    return []
                }

                let batchSize = min(pendingTouches.count, AnsightTouchInputWireProtocol.maxBatchSize)
                let batch = Array(pendingTouches.prefix(batchSize))
                pendingTouches.removeFirst(batchSize)
                return batch
            }

            guard !batch.isEmpty else {
                return
            }

            let result = await send(batch)
            resultHandler(batch.count, result)
            if !result.success {
                lock.withLock {
                    streaming = false
                    pendingTouches.removeAll()
                    pumpTask = nil
                }
                return
            }
        }

        lock.withLock {
            pumpTask = nil
        }
    }

    private func send(_ touches: [AnsightCapturedTouch]) async -> OperationResult {
        do {
            for payload in AnsightTouchInputWireProtocol.payloads(for: touches) {
                let result = try await transport.sendText(payload.jsonString())
                guard result.success else {
                    return result
                }
            }

            return .success("Streamed \(touches.count) touch input records.")
        } catch {
            return .failure("Failed to encode touch input: \(error.localizedDescription)")
        }
    }
}
