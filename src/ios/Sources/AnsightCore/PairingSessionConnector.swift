import Foundation
import Network

public final class PairingSessionConnector: PairingSessionConnecting, @unchecked Sendable {
    private let datagramClient: any PairingDatagramClient
    private let wifiStatusProvider: @Sendable () -> PairingWifiPreflightStatus
    private let simulatorLocalHostAddressProvider: @Sendable () -> String?

    public convenience init() {
        self.init(datagramClient: NetworkPairingDatagramClient())
    }

    init(
        datagramClient: any PairingDatagramClient,
        wifiStatusProvider: @escaping @Sendable () -> PairingWifiPreflightStatus = PairingWifiPreflight.getStatus,
        simulatorLocalHostAddressProvider: @escaping @Sendable () -> String? = PairingSimulatorLocalHostAddress.resolve
    ) {
        self.datagramClient = datagramClient
        self.wifiStatusProvider = wifiStatusProvider
        self.simulatorLocalHostAddressProvider = simulatorLocalHostAddressProvider
    }

    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt {
        let simulatorLocalHostAddress = normalizedHostAddress(simulatorLocalHostAddressProvider())
        let hostAddressCandidates = PairingHostAddressCandidates.resolve(
            discoveryHint: document.discoveryHint,
            hostAddressOverride: options?.hostAddressOverride,
            simulatorLocalHostAddress: simulatorLocalHostAddress
        )
        guard !hostAddressCandidates.isEmpty else {
            return .failure(
                "A current host address is required. Import a fresh pairing config or compact pairing config code.",
                code: PairingFailureCodes.hostAddressRequired
            )
        }

        let discoveryPort = options?.discoveryPort
            ?? document.discoveryHint?.discoveryPort
            ?? document.config.host.discoveryPort

        guard (1...65_535).contains(discoveryPort) else {
            return .failure("Pairing discovery port must be between 1 and 65535.", code: PairingFailureCodes.hostAddressRequired)
        }

        let hostNetworkCheckMessage = Self.hostNetworkCheckMessage(discoveryHint: document.discoveryHint)
        if !Self.hasSimulatorLocalHostCandidate(hostAddressCandidates, simulatorLocalHostAddress: simulatorLocalHostAddress),
           wifiStatusProvider() == .notConnected {
            return .failure(
                "Ansight is unavailable because this device is not connected to Wi-Fi. \(hostNetworkCheckMessage)",
                code: PairingFailureCodes.wifiRequired
            )
        }

        if document.config.isSecureV2 {
            return await connectSecure(
                document: document,
                hostAddressCandidates: hostAddressCandidates,
                discoveryPort: discoveryPort,
                hostNetworkCheckMessage: hostNetworkCheckMessage,
                requestedScopes: options?.requestedScopes ?? [],
                requestCritical: options?.requestCritical ?? false
            )
        }

        guard options?.allowInsecureV1 == true else {
            return .failure(
                "This pairing document uses insecure protocol v1. Re-pair with Studio for WSS, or explicitly enable legacy v1 for a controlled local development environment.",
                code: PairingFailureCodes.transportNegotiationFailed
            )
        }

        let oneTimeToken = document.config.oneTimeToken
        guard !oneTimeToken.isEmpty else {
            return .failure("Legacy pairing config did not contain a token.", code: PairingFailureCodes.pairingTokenInvalid)
        }

        let request = ConnectRequest(
            configId: document.config.configId,
            oneTimeToken: oneTimeToken,
            appId: document.config.appId,
            clientName: clientName
        )

        let requestData: Data
        do {
            requestData = try JSONEncoder.ansightEncoder.encode(request)
        } catch {
            return .failure("Failed to encode connect request: \(error.localizedDescription)", code: PairingFailureCodes.udpBootstrapFailed)
        }

        var lastFailure: PairingConnectionAttempt?
        for hostAddress in hostAddressCandidates {
            let responseData: Data?
            do {
                responseData = try await datagramClient.sendConnectRequest(
                    requestData,
                    host: hostAddress,
                    port: discoveryPort,
                    timeoutSeconds: 5
                )
            } catch {
                lastFailure = .failure("UDP connect failed for \(hostAddress): \(error.localizedDescription)", code: PairingFailureCodes.udpBootstrapFailed)
                continue
            }

            guard let responseData else {
                lastFailure = .failure(
                    "No connect response from host at \(hostAddress). \(hostNetworkCheckMessage) The remembered host address may be stale. Import a fresh pairing QR code or enter the host IP manually.",
                    code: PairingFailureCodes.udpBootstrapTimeout
                )
                continue
            }

            let response: ConnectResponse
            do {
                response = try JSONDecoder.ansightDecoder.decode(ConnectResponse.self, from: responseData)
            } catch {
                return .failure("Host connect response was malformed: \(error.localizedDescription)", code: PairingFailureCodes.udpBootstrapFailed)
            }

            guard response.type == "CONNECT_RESP" else {
                return .failure("Host connect response had unexpected type '\(response.type)'.", code: PairingFailureCodes.udpBootstrapFailed)
            }

            guard response.accepted else {
                return .rejected(hostAddress: hostAddress, response: response)
            }

            guard let webSocketPort = response.webSocketPort,
                  let webSocketPath = response.webSocketPath,
                  let webSocketToken = response.webSocketToken,
                  !webSocketPath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  !webSocketToken.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            else {
                return .failure("Host did not provide a WebSocket handoff.", code: PairingFailureCodes.webSocketHandoffUnavailable)
            }

            var components = URLComponents()
            components.scheme = "ws"
            components.host = hostAddress
            components.port = webSocketPort
            components.path = webSocketPath.hasPrefix("/") ? webSocketPath : "/\(webSocketPath)"
            components.queryItems = [URLQueryItem(name: "token", value: webSocketToken)]
            guard let url = components.url else {
                return .failure("Host WebSocket handoff was not a valid URL.", code: PairingFailureCodes.webSocketHandoffUnavailable)
            }

            return .success(hostAddress: hostAddress, response: response, webSocketURL: url)
        }

        return lastFailure ?? .failure(
            "A current host address is required. Import a fresh pairing config or compact pairing config code.",
            code: PairingFailureCodes.hostAddressRequired
        )
    }

    private func connectSecure(
        document: ParsedPairingDocument,
        hostAddressCandidates: [String],
        discoveryPort: Int,
        hostNetworkCheckMessage: String,
        requestedScopes: [String],
        requestCritical: Bool
    ) async -> PairingConnectionAttempt {
        let request: ConnectInitV2
        let requestData: Data
        do {
            request = try SecurePairingProtocol.makeConnectInit(config: document.config)
            requestData = try JSONEncoder.ansightEncoder.encode(request)
        } catch {
            return .failure("Failed to create secure connect request: \(error.localizedDescription)", code: PairingFailureCodes.udpBootstrapFailed)
        }

        var lastFailure: PairingConnectionAttempt?
        for hostAddress in hostAddressCandidates {
            let responseData: Data?
            do {
                responseData = try await datagramClient.sendConnectRequest(
                    requestData,
                    host: hostAddress,
                    port: discoveryPort,
                    timeoutSeconds: 5
                )
            } catch {
                lastFailure = .failure("Secure UDP connect failed for \(hostAddress): \(error.localizedDescription)", code: PairingFailureCodes.udpBootstrapFailed)
                continue
            }

            guard let responseData else {
                lastFailure = .failure(
                    "No secure connect offer from host at \(hostAddress). \(hostNetworkCheckMessage)",
                    code: PairingFailureCodes.udpBootstrapTimeout
                )
                continue
            }

            do {
                let offer = try JSONDecoder.ansightDecoder.decode(ConnectOfferV2.self, from: responseData)
                try SecurePairingProtocol.validateOffer(offer, request: request, config: document.config)

                var components = URLComponents()
                components.scheme = "wss"
                components.host = hostAddress
                components.port = offer.webSocketPort
                components.path = offer.webSocketPath
                guard let url = components.url else {
                    throw PairingDocumentError.invalidDocument("Secure WebSocket offer URL is invalid.")
                }
                return .secureSuccess(
                    hostAddress: hostAddress,
                    config: document.config,
                    request: request,
                    offer: offer,
                    requestedScopes: SecurePairingProtocol.canonicalScopes(requestedScopes),
                    requestCritical: requestCritical,
                    webSocketURL: url
                )
            } catch {
                lastFailure = .failure("Secure host offer was rejected: \(error.localizedDescription)", code: PairingFailureCodes.pairingProofInvalid)
            }
        }

        return lastFailure ?? .failure(
            "A current secure host address is required.",
            code: PairingFailureCodes.hostAddressRequired
        )
    }

    private func normalizedHostAddress(_ address: String?) -> String? {
        guard let candidate = address?.trimmingCharacters(in: .whitespacesAndNewlines),
              !candidate.isEmpty
        else {
            return nil
        }

        return candidate
    }

    private static func hasSimulatorLocalHostCandidate(
        _ candidates: [String],
        simulatorLocalHostAddress: String?
    ) -> Bool {
        guard let simulatorLocalHostAddress else {
            return false
        }

        return candidates.contains { $0.caseInsensitiveCompare(simulatorLocalHostAddress) == .orderedSame }
    }

    private static func hostNetworkCheckMessage(discoveryHint: PairingDiscoveryHint?) -> String {
        if let wifiName = discoveryHint?.wifiName?.trimmingCharacters(in: .whitespacesAndNewlines),
           !wifiName.isEmpty {
            return "Check that this device is on the same Wi-Fi network as the Ansight host. Last known host Wi-Fi: \(wifiName)."
        }

        return "Check that this device is on the same Wi-Fi network as the Ansight host."
    }
}
