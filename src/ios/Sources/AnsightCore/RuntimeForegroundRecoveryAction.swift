import Foundation

enum RuntimeForegroundRecoveryAction: Sendable, Equatable {
    case none
    case refreshOpenSession
    case reconnect
    case closeStaleTransport
    case closeStaleTransportAndReconnect
}
