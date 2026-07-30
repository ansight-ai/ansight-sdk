import Foundation
import Network

/// A clear-text WebSocket implemented directly on Network.framework.
///
/// Keeping the socket outside URL Loading System avoids requiring an ATS
/// exception in every consuming app while preserving the SDK's `ws://`
/// development transport.
final class NetworkPairingWebSocket: PairingWebSocket, @unchecked Sendable {
    private let connection: NWConnection
    private let queue = DispatchQueue(label: "ai.ansight.websocket")

    init(url: URL) {
        let webSocketOptions = NWProtocolWebSocket.Options(.version13)
        webSocketOptions.autoReplyPing = true
        webSocketOptions.maximumMessageSize = 64 * 1024 * 1024

        let parameters = NWParameters.tcp
        parameters.defaultProtocolStack.applicationProtocols.insert(webSocketOptions, at: 0)
        connection = NWConnection(to: .url(url), using: parameters)
    }

    func resume() {
        connection.start(queue: queue)
    }

    func cancel(with closeCode: URLSessionWebSocketTask.CloseCode, reason: Data?) {
        let networkCloseCode =
            (try? NWProtocolWebSocket.CloseCode(rawValue: UInt16(closeCode.rawValue)))
            ?? .protocolCode(.normalClosure)
        let metadata = NWProtocolWebSocket.Metadata(opcode: .close)
        metadata.closeCode = networkCloseCode
        let context = NWConnection.ContentContext(
            identifier: "ansight.close",
            metadata: [metadata]
        )
        connection.send(
            content: reason,
            contentContext: context,
            isComplete: true,
            completion: .contentProcessed { [connection] _ in
                connection.cancel()
            }
        )
    }

    func send(_ message: URLSessionWebSocketTask.Message) async throws {
        let content: Data
        let opcode: NWProtocolWebSocket.Opcode
        switch message {
        case .string(let text):
            content = Data(text.utf8)
            opcode = .text
        case .data(let data):
            content = data
            opcode = .binary
        @unknown default:
            throw NetworkPairingWebSocketError.unsupportedMessage
        }

        let metadata = NWProtocolWebSocket.Metadata(opcode: opcode)
        let context = NWConnection.ContentContext(
            identifier: "ansight.message",
            metadata: [metadata]
        )
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<Void, Error>) in
            connection.send(
                content: content,
                contentContext: context,
                isComplete: true,
                completion: .contentProcessed { error in
                    if let error {
                        continuation.resume(throwing: error)
                    } else {
                        continuation.resume(returning: ())
                    }
                }
            )
        }
    }

    func receive() async throws -> URLSessionWebSocketTask.Message {
        try await withCheckedThrowingContinuation { continuation in
            receiveNext { result in
                continuation.resume(with: result)
            }
        }
    }

    private func receiveNext(
        completion: @escaping @Sendable (Result<URLSessionWebSocketTask.Message, Error>) -> Void
    ) {
        connection.receiveMessage { [weak self] content, context, _, error in
            if let error {
                completion(.failure(error))
                return
            }

            guard let metadata = context?
                .protocolMetadata(definition: NWProtocolWebSocket.definition)
                as? NWProtocolWebSocket.Metadata
            else {
                completion(.failure(NetworkPairingWebSocketError.missingMetadata))
                return
            }

            let payload = content ?? Data()
            switch metadata.opcode {
            case .text:
                guard let text = String(data: payload, encoding: .utf8) else {
                    completion(.failure(NetworkPairingWebSocketError.invalidText))
                    return
                }
                completion(.success(.string(text)))
            case .binary:
                completion(.success(.data(payload)))
            case .close:
                completion(.failure(NetworkPairingWebSocketError.closed))
            case .ping, .pong:
                guard let self else {
                    completion(.failure(NetworkPairingWebSocketError.closed))
                    return
                }
                self.receiveNext(completion: completion)
            case .cont:
                completion(.failure(NetworkPairingWebSocketError.unsupportedMessage))
            @unknown default:
                completion(.failure(NetworkPairingWebSocketError.unsupportedMessage))
            }
        }
    }
}

private enum NetworkPairingWebSocketError: LocalizedError {
    case closed
    case invalidText
    case missingMetadata
    case unsupportedMessage

    var errorDescription: String? {
        switch self {
        case .closed:
            return "WebSocket connection closed."
        case .invalidText:
            return "WebSocket text message was not valid UTF-8."
        case .missingMetadata:
            return "WebSocket message metadata was missing."
        case .unsupportedMessage:
            return "WebSocket message type is unsupported."
        }
    }
}
