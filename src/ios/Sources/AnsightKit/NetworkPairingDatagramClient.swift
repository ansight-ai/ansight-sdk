import Foundation
import Network

struct NetworkPairingDatagramClient: PairingDatagramClient {
    func sendConnectRequest(_ data: Data, host: String, port: Int, timeoutSeconds: TimeInterval) async throws -> Data? {
        guard let nwPort = NWEndpoint.Port(rawValue: UInt16(port)) else {
            throw TransportError.closed
        }

        return try await withCheckedThrowingContinuation { continuation in
            let connection = NWConnection(host: NWEndpoint.Host(host), port: nwPort, using: .udp)
            let queue = DispatchQueue(label: "ai.ansight.ios.udp-bootstrap.\(UUID().uuidString)")
            let gate = ContinuationGate<Data?>(continuation: continuation)

            @Sendable func finish(_ result: Result<Data?, Error>) {
                connection.cancel()
                gate.resume(result)
            }

            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    connection.send(content: data, completion: .contentProcessed { error in
                        if let error {
                            finish(.failure(error))
                            return
                        }

                        connection.receiveMessage { responseData, _, _, receiveError in
                            if let receiveError {
                                finish(.failure(receiveError))
                                return
                            }

                            finish(.success(responseData))
                        }
                    })
                case .failed(let error):
                    finish(.failure(error))
                case .cancelled:
                    finish(.success(nil))
                default:
                    break
                }
            }

            queue.asyncAfter(deadline: .now() + timeoutSeconds) {
                finish(.success(nil))
            }
            connection.start(queue: queue)
        }
    }
}
