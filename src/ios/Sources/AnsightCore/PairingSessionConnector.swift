import Foundation
import Network

public final class PairingSessionConnector: PairingSessionConnecting, @unchecked Sendable {
    private let datagramClient: any PairingDatagramClient
    private let wifiStatusProvider: @Sendable () -> PairingWifiPreflightStatus
    private let simulatorLocalHostAddressProvider: @Sendable () -> String?

    public convenience init() {
        self.init(
            datagramClient: NetworkPairingDatagramClient(),
            simulatorLocalHostAddressProvider: PairingSimulatorLocalHostAddress.resolve
        )
    }

    init(
        datagramClient: any PairingDatagramClient,
        wifiStatusProvider: @escaping @Sendable () -> PairingWifiPreflightStatus = PairingWifiPreflight.getStatus,
        simulatorLocalHostAddressProvider: @escaping @Sendable () -> String? = { nil }
    ) {
        self.datagramClient = datagramClient
        self.wifiStatusProvider = wifiStatusProvider
        self.simulatorLocalHostAddressProvider = simulatorLocalHostAddressProvider
    }

    var localHostAddress: String? {
        normalizedHostAddress(simulatorLocalHostAddressProvider())
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
                "The scanned Ansight QR code does not contain a reachable Studio address.",
                code: PairingFailureCodes.hostAddressRequired
            )
        }

        let discoveryPort = options?.discoveryPort
            ?? document.discoveryHint?.discoveryPort
            ?? document.config.host.discoveryPort
        guard (1...65_535).contains(discoveryPort) else {
            return .failure(
                "Studio discovery port must be between 1 and 65535.",
                code: PairingFailureCodes.hostAddressRequired
            )
        }

        let hostNetworkCheckMessage = Self.hostNetworkCheckMessage(discoveryHint: document.discoveryHint)
        let hasSimulatorLocalHostCandidate = Self.hasSimulatorLocalHostCandidate(
            hostAddressCandidates,
            simulatorLocalHostAddress: simulatorLocalHostAddress
        )
        let wifiStatus = hasSimulatorLocalHostCandidate ? PairingWifiPreflightStatus.connected : wifiStatusProvider()
        if wifiStatus == .notConnected {
            return .failure(
                "This device must be on the same Wi-Fi network as Ansight Studio. \(hostNetworkCheckMessage)",
                code: PairingFailureCodes.wifiRequired
            )
        }
        if wifiStatus == .cellular, options?.allowCellularConnections != true {
            return .failure(
                "Cellular Studio connections are disabled.",
                code: PairingFailureCodes.wifiRequired
            )
        }

        let deviceId = PairingDeviceIdentity.resolve()
        let enrollmentAppId: String
        if document.config.appId.trimmingCharacters(in: .whitespacesAndNewlines) == PairingConfig.anyAppId {
            enrollmentAppId = Bundle.main.bundleIdentifier?
                .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        } else {
            enrollmentAppId = document.config.appId
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }
        guard !enrollmentAppId.isEmpty, enrollmentAppId != PairingConfig.anyAppId else {
            return .failure(
                "Ansight could not resolve this app's bundle id for generic enrollment.",
                code: PairingFailureCodes.enrollmentRequired
            )
        }

        let isLocalEnrollment = document.config.configId.hasPrefix(
            PairingEnrollmentModes.localConfigPrefix
        )
        let discoveryTimeoutSeconds: TimeInterval = isLocalEnrollment ? 1 : 5
        var lastFailure: PairingConnectionAttempt?
        for hostAddress in hostAddressCandidates {
            let requestId = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
            let request = ConnectRequest(
                requestId: requestId,
                enrollmentMode: isLocalEnrollment
                    ? PairingEnrollmentModes.local
                    : PairingEnrollmentModes.invite,
                inviteId: document.config.configId,
                appId: enrollmentAppId,
                deviceId: deviceId,
                deviceName: clientName,
                accessToken: document.config.enrollment.accessToken
            )
            let requestData: Data
            do {
                requestData = try JSONEncoder.ansightEncoder.encode(request)
            } catch {
                return .failure(
                    "Failed to encode enrollment request: \(error.localizedDescription)",
                    code: PairingFailureCodes.udpBootstrapFailed
                )
            }

            let responseData: Data?
            do {
                responseData = try await datagramClient.sendConnectRequest(
                    requestData,
                    host: hostAddress,
                    port: discoveryPort,
                    timeoutSeconds: discoveryTimeoutSeconds
                )
            } catch {
                lastFailure = .failure(
                    "UDP enrollment failed for \(hostAddress): \(error.localizedDescription)",
                    code: PairingFailureCodes.udpBootstrapFailed
                )
                continue
            }
            guard let responseData else {
                lastFailure = .failure(
                    "No response from Studio at \(hostAddress). \(hostNetworkCheckMessage) Scan a fresh QR code if Studio's address changed.",
                    code: PairingFailureCodes.udpBootstrapTimeout
                )
                continue
            }

            let response: ConnectResponse
            do {
                response = try JSONDecoder.ansightDecoder.decode(ConnectResponse.self, from: responseData)
            } catch {
                return .failure(
                    "Studio enrollment response was malformed: \(error.localizedDescription)",
                    code: PairingFailureCodes.udpBootstrapFailed
                )
            }
            guard response.type == "ENROLLMENT_RESULT",
                  response.ver == 2,
                  response.requestId == requestId
            else {
                return .failure(
                    "Studio returned an unexpected enrollment response.",
                    code: PairingFailureCodes.udpBootstrapFailed
                )
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
                return .failure(
                    "Studio did not provide a WebSocket handoff.",
                    code: PairingFailureCodes.webSocketHandoffUnavailable
                )
            }

            var components = URLComponents()
            components.scheme = "ws"
            components.host = hostAddress
            components.port = webSocketPort
            components.path = webSocketPath.hasPrefix("/") ? webSocketPath : "/\(webSocketPath)"
            components.queryItems = [URLQueryItem(name: "token", value: webSocketToken)]
            guard let url = components.url else {
                return .failure(
                    "Studio WebSocket handoff was not a valid URL.",
                    code: PairingFailureCodes.webSocketHandoffUnavailable
                )
            }

            return .success(hostAddress: hostAddress, response: response, webSocketURL: url)
        }

        return lastFailure ?? .failure(
            "The scanned Ansight QR code does not contain a reachable Studio address.",
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
        return candidates.contains {
            $0.caseInsensitiveCompare(simulatorLocalHostAddress) == .orderedSame
        }
    }

    private static func hostNetworkCheckMessage(discoveryHint: PairingDiscoveryHint?) -> String {
        if let wifiName = discoveryHint?.wifiName?.trimmingCharacters(in: .whitespacesAndNewlines),
           !wifiName.isEmpty {
            return "Last known Studio Wi-Fi: \(wifiName)."
        }
        return "Check that both devices are on the same Wi-Fi network."
    }
}

enum PairingDeviceIdentity {
    private static let key = "ai.ansight.enrollment.device-id"
    private static let accessTokenKey = "ai.ansight.enrollment.local-access-token"
    private static let lock = NSLock()

    static func resolve() -> String {
        lock.lock()
        defer { lock.unlock() }

        if let existing = UserDefaults.standard.string(forKey: key)?
            .trimmingCharacters(in: .whitespacesAndNewlines),
           !existing.isEmpty {
            return existing
        }

        let deviceId = "apple.\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
        UserDefaults.standard.set(deviceId, forKey: key)
        return deviceId
    }

    static func resolveAccessToken() -> String {
        lock.lock()
        defer { lock.unlock() }

        if let existing = UserDefaults.standard.string(forKey: accessTokenKey)?
            .trimmingCharacters(in: .whitespacesAndNewlines),
           !existing.isEmpty {
            return existing
        }

        let bytes = Data((0..<32).map { _ in UInt8.random(in: .min ... .max) })
        let accessToken = bytes.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
        UserDefaults.standard.set(accessToken, forKey: accessTokenKey)
        return accessToken
    }
}
