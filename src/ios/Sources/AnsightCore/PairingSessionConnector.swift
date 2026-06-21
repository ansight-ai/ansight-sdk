import Foundation
import Network

public final class PairingSessionConnector: PairingSessionConnecting, @unchecked Sendable {
    private let datagramClient: any PairingDatagramClient
    private let wifiStatusProvider: @Sendable () -> PairingWifiPreflightStatus

    public convenience init() {
        self.init(datagramClient: NetworkPairingDatagramClient())
    }

    init(
        datagramClient: any PairingDatagramClient,
        wifiStatusProvider: @escaping @Sendable () -> PairingWifiPreflightStatus = PairingWifiPreflight.getStatus
    ) {
        self.datagramClient = datagramClient
        self.wifiStatusProvider = wifiStatusProvider
    }

    func connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions?
    ) async -> PairingConnectionAttempt {
        let hostAddressCandidates = Self.hostAddressCandidates(document: document, options: options)
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
        if wifiStatusProvider() == .notConnected {
            return .failure(
                "Ansight is unavailable because this device is not connected to Wi-Fi. \(hostNetworkCheckMessage)",
                code: PairingFailureCodes.wifiRequired
            )
        }

        let request = ConnectRequest(
            configId: document.config.configId,
            oneTimeToken: document.config.oneTimeToken,
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

    private static func hostAddressCandidates(document: ParsedPairingDocument, options: PairingConnectionOptions?) -> [String] {
        if let hostAddressOverride = options?.hostAddressOverride?.trimmingCharacters(in: .whitespacesAndNewlines),
           !hostAddressOverride.isEmpty {
            return [hostAddressOverride]
        }

        var seen = Set<String>()
        var candidates: [String] = []
        for address in document.discoveryHint?.hostAddresses ?? [] {
            let candidate = address.trimmingCharacters(in: .whitespacesAndNewlines)
            if !candidate.isEmpty && seen.insert(candidate).inserted {
                candidates.append(candidate)
            }
        }

        return candidates
    }

    private static func hostNetworkCheckMessage(discoveryHint: PairingDiscoveryHint?) -> String {
        if let wifiName = discoveryHint?.wifiName?.trimmingCharacters(in: .whitespacesAndNewlines),
           !wifiName.isEmpty {
            return "Check that this device is on the same Wi-Fi network as the Ansight host. Last known host Wi-Fi: \(wifiName)."
        }

        return "Check that this device is on the same Wi-Fi network as the Ansight host."
    }
}
