import Foundation
import Network

final class PairingLiveSessionTransport: @unchecked Sendable {
    private static let defaultSendTimeoutSeconds: TimeInterval = 10

    private let lock = NSLock()
    private let sendTimeoutSeconds: TimeInterval
    private var webSocket: URLSessionWebSocketTask?
    private var receiveTask: Task<Void, Never>?
    private var pendingResponses: [String: CheckedContinuation<PairingControlEnvelope, Error>] = [:]
    private var toolMessageHandler: (@Sendable (String) async -> String?)?
    private var toolResponseSentHandler: (@Sendable (String, String) async -> Void)?
    private var closeHandler: (@Sendable (String) async -> Void)?

    init(sendTimeoutSeconds: TimeInterval = PairingLiveSessionTransport.defaultSendTimeoutSeconds) {
        self.sendTimeoutSeconds = max(0.2, sendTimeoutSeconds)
    }

    var isOpen: Bool {
        lock.withLock { webSocket != nil }
    }

    func attach(
        url: URL,
        toolMessageHandler: (@Sendable (String) async -> String?)? = nil,
        toolResponseSentHandler: (@Sendable (String, String) async -> Void)? = nil,
        closeHandler: (@Sendable (String) async -> Void)? = nil
    ) async throws {
        await close(notify: false)
        let task = URLSession.shared.webSocketTask(with: url)
        lock.withLock {
            webSocket = task
            self.toolMessageHandler = toolMessageHandler
            self.toolResponseSentHandler = toolResponseSentHandler
            self.closeHandler = closeHandler
        }
        task.resume()
        receiveTask = Task { [weak self] in
            await self?.runReceivePump(task)
        }
    }

    func sendControlRequest(
        action: String,
        payload: JSONValue?,
        acknowledgementTimeoutSeconds: TimeInterval = 15
    ) async -> OperationResult {
        let requestId = "client.\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
        let envelope = PairingControlEnvelope(
            type: PairingControlEnvelope.requestType,
            id: requestId,
            action: action,
            payload: payload
        )

        do {
            let response = try await sendAndAwaitResponse(
                envelope,
                requestId: requestId,
                timeoutSeconds: acknowledgementTimeoutSeconds
            )
            if response.success {
                return .success(response.message ?? "\(action) acknowledged.")
            }

            return .failure(response.message ?? "\(action) failed.")
        } catch {
            return .failure("Failed to send \(action): \(error.localizedDescription)")
        }
    }

    func sendText(_ text: String) async -> OperationResult {
        guard let socket = lock.withLock({ webSocket }) else {
            return .failure("WebSocket session is not open.")
        }

        do {
            try await sendWebSocketMessage(.string(text), using: socket)
            return .success("Payload sent.")
        } catch {
            await close(reason: "Failed to send WebSocket payload: \(error.localizedDescription)", notify: true)
            return .failure("Failed to send WebSocket payload: \(error.localizedDescription)")
        }
    }

    func sendData(_ data: Data) async -> OperationResult {
        guard let socket = lock.withLock({ webSocket }) else {
            return .failure("WebSocket session is not open.")
        }

        do {
            try await sendWebSocketMessage(.data(data), using: socket)
            return .success("Binary payload sent.")
        } catch {
            await close(reason: "Failed to send WebSocket binary payload: \(error.localizedDescription)", notify: true)
            return .failure("Failed to send WebSocket binary payload: \(error.localizedDescription)")
        }
    }

    func close(reason: String = "WebSocket session closed.", notify: Bool = false) async {
        let state = lock.withLock { () -> (
            URLSessionWebSocketTask?,
            Task<Void, Never>?,
            (@Sendable (String) async -> Void)?
        ) in
            let state = (webSocket, receiveTask)
            webSocket = nil
            receiveTask = nil
            toolMessageHandler = nil
            toolResponseSentHandler = nil
            let handler = closeHandler
            closeHandler = nil
            let pending = pendingResponses
            pendingResponses.removeAll()
            for continuation in pending.values {
                continuation.resume(throwing: TransportError.closed)
            }
            return (state.0, state.1, handler)
        }

        state.0?.cancel(with: .normalClosure, reason: nil)
        state.1?.cancel()
        if notify, let handler = state.2 {
            await handler(reason)
        }
    }

    private func sendAndAwaitResponse(
        _ envelope: PairingControlEnvelope,
        requestId: String,
        timeoutSeconds: TimeInterval
    ) async throws -> PairingControlEnvelope {
        guard let socket = lock.withLock({ webSocket }) else {
            throw TransportError.closed
        }

        let data = try JSONEncoder.ansightEncoder.encode(envelope)
        let json = String(decoding: data, as: UTF8.self)
        return try await withCheckedThrowingContinuation { continuation in
            lock.withLock {
                pendingResponses[requestId] = continuation
            }

            Task { [weak self] in
                guard let self else {
                    return
                }

                do {
                    try await self.sendWebSocketMessage(.string(json), using: socket)
                } catch {
                    self.failPendingResponse(requestId, error: error)
                    await self.close(reason: "Failed to send \(envelope.action): \(error.localizedDescription)", notify: true)
                }
            }

            Task { [weak self] in
                let nanoseconds = UInt64(max(0.2, timeoutSeconds) * 1_000_000_000)
                try? await Task.sleep(nanoseconds: nanoseconds)
                self?.failPendingResponse(requestId, error: TransportError.timeout)
            }
        }
    }

    private func failPendingResponse(_ requestId: String, error: Error) {
        let continuation = lock.withLock {
            pendingResponses.removeValue(forKey: requestId)
        }
        continuation?.resume(throwing: error)
    }

    private func sendWebSocketMessage(
        _ message: URLSessionWebSocketTask.Message,
        using socket: URLSessionWebSocketTask
    ) async throws {
        let timeoutSeconds = sendTimeoutSeconds
        try await withCheckedThrowingContinuation { continuation in
            let gate = ContinuationGate<Void>(continuation: continuation)
            let sendTask = Task {
                do {
                    try await socket.send(message)
                    gate.resume(.success(()))
                } catch {
                    gate.resume(.failure(error))
                }
            }

            Task {
                let nanoseconds = UInt64(timeoutSeconds * 1_000_000_000)
                try? await Task.sleep(nanoseconds: nanoseconds)
                sendTask.cancel()
                gate.resume(.failure(TransportError.sendTimeout))
            }
        }
    }

    private func runReceivePump(_ socket: URLSessionWebSocketTask) async {
        var closeReason = "WebSocket session closed."
        while !Task.isCancelled {
            do {
                let message = try await socket.receive()
                switch message {
                case .string(let text):
                    await handleIncomingText(text)
                case .data:
                    continue
                @unknown default:
                    continue
                }
            } catch {
                closeReason = "WebSocket receive failed: \(error.localizedDescription)"
                break
            }
        }

        await close(reason: closeReason, notify: true)
    }

    private func handleIncomingText(_ text: String) async {
        if let envelope = try? JSONDecoder.ansightDecoder.decode(PairingControlEnvelope.self, from: Data(text.utf8)),
           envelope.type == PairingControlEnvelope.responseType,
           let replyTo = envelope.replyTo {
            let continuation = lock.withLock {
                pendingResponses.removeValue(forKey: replyTo)
            }
            continuation?.resume(returning: envelope)
            return
        }

        if let response = await toolMessageHandler?(text) {
            let result = await sendText(response)
            if result.success {
                let handler = lock.withLock { toolResponseSentHandler }
                await handler?(text, response)
            }
        }
    }
}
