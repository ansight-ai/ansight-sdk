import Foundation
import Network

struct PairingConnectionAttempt: Sendable {
    let success: Bool
    let accepted: Bool
    let message: String
    let hostAddress: String?
    let connectResponse: ConnectResponse?
    let webSocketURL: URL?
    let secureContext: SecurePairingContext?
    let failureCode: String?

    static func failure(_ message: String, code: String? = nil) -> PairingConnectionAttempt {
        PairingConnectionAttempt(
            success: false,
            accepted: false,
            message: message,
            hostAddress: nil,
            connectResponse: nil,
            webSocketURL: nil,
            secureContext: nil,
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
            secureContext: nil,
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
            secureContext: nil,
            failureCode: nil
        )
    }

    static func secureSuccess(
        hostAddress: String,
        config: PairingConfig,
        request: ConnectInitV2,
        offer: ConnectOfferV2,
        requestedScopes: [String],
        requestCritical: Bool,
        webSocketURL: URL
    ) -> PairingConnectionAttempt {
        let response = ConnectResponse(
            type: "CONNECT_OFFER_V2",
            ver: 2,
            accepted: true,
            reason: "ok",
            reasonMessage: nil,
            hostId: offer.hostId,
            hostName: config.host.hostName ?? offer.hostId,
            hostWifiName: nil,
            message: "Secure connection offer accepted.",
            webSocketPort: offer.webSocketPort,
            webSocketPath: offer.webSocketPath,
            webSocketToken: nil
        )
        return PairingConnectionAttempt(
            success: true,
            accepted: true,
            message: "Authenticated secure host offer is ready.",
            hostAddress: hostAddress,
            connectResponse: response,
            webSocketURL: webSocketURL,
            secureContext: SecurePairingContext(
                config: config,
                request: request,
                offer: offer,
                requestedScopes: requestedScopes,
                requestCritical: requestCritical
            ),
            failureCode: nil
        )
    }
}
