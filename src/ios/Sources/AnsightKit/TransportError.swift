import Foundation
import Network

enum TransportError: LocalizedError {
    case timeout
    case closed
    case sendTimeout

    var errorDescription: String? {
        switch self {
        case .timeout:
            return "Timed out waiting for host acknowledgement."
        case .closed:
            return "WebSocket session is not open."
        case .sendTimeout:
            return "Timed out sending WebSocket payload."
        }
    }
}
