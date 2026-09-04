import Foundation
import Network

struct PairingConnectionAttempt: Sendable {
    let success: Bool
    let accepted: Bool
    let message: String
    let hostAddress: String?
    let connectResponse: ConnectResponse?
    let webSocketURL: URL?
    let authenticatedWebSocket: (any PairingWebSocket)?
    let failureCode: String?

    static func failure(_ message: String, code: String? = nil) -> PairingConnectionAttempt {
        PairingConnectionAttempt(
            success: false,
            accepted: false,
            message: message,
            hostAddress: nil,
            connectResponse: nil,
            webSocketURL: nil,
            authenticatedWebSocket: nil,
            failureCode: code
        )
    }

    static func rejected(hostAddress: String, response: ConnectResponse) -> PairingConnectionAttempt {
        PairingConnectionAttempt(
            success: false,
            accepted: false,
            message: response.reasonMessage ?? response.message,
            hostAddress: hostAddress,
            connectResponse: response,
            webSocketURL: nil,
            authenticatedWebSocket: nil,
            failureCode: nil
        )
    }

    static func success(hostAddress: String, response: ConnectResponse, webSocketURL: URL) -> PairingConnectionAttempt {
        PairingConnectionAttempt(
            success: true,
            accepted: true,
            message: "Connected to host and WebSocket session is ready.",
            hostAddress: hostAddress,
            connectResponse: response,
            webSocketURL: webSocketURL,
            authenticatedWebSocket: nil,
            failureCode: nil
        )
    }

    static func secureSuccess(
        hostAddress: String,
        response: ConnectResponse,
        webSocket: any PairingWebSocket
    ) -> PairingConnectionAttempt {
        PairingConnectionAttempt(
            success: true,
            accepted: true,
            message: "Securely enrolled and connected to Ansight host.",
            hostAddress: hostAddress,
            connectResponse: response,
            webSocketURL: nil,
            authenticatedWebSocket: webSocket,
            failureCode: nil
        )
    }
}
