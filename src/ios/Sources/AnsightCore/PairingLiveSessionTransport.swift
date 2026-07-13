import Foundation
import Network

final class PairingLiveSessionTransport: @unchecked Sendable {
    private static let defaultSendTimeoutSeconds: TimeInterval = 10
    private static let authenticationTimeoutSeconds: TimeInterval = 10
    private static let maximumAuthenticationMessageBytes = 64 * 1_024
    private static let maximumInboundTextMessageBytes = 1_024 * 1_024
    private static let maximumInboundBinaryMessageBytes = 8 * 1_024 * 1_024

    private let lock = NSLock()
    private let sendTimeoutSeconds: TimeInterval
    private let webSocketFactory: @Sendable (URL, String?) -> any PairingWebSocket
    private let identityStore: any PairingClientIdentityStoring
    private var webSocket: (any PairingWebSocket)?
    private var receiveTask: Task<Void, Never>?
    private var pendingResponses: [String: CheckedContinuation<PairingControlEnvelope, Error>] = [:]
    private var toolMessageHandler: (@Sendable (String) async -> String?)?
    private var toolResponseSentHandler: (@Sendable (String, String) async -> Void)?
    private var closeHandler: (@Sendable (String) async -> Void)?
    private var authenticatedSessionId: String?
    private var authenticatedGrant: PairingGrantV2?

    init(
        sendTimeoutSeconds: TimeInterval = PairingLiveSessionTransport.defaultSendTimeoutSeconds,
        identityStore: any PairingClientIdentityStoring = KeychainPairingClientIdentityStore(),
        webSocketFactory: @escaping @Sendable (URL, String?) -> any PairingWebSocket = {
            URLSessionPairingWebSocket(url: $0, tlsSpkiSha256: $1)
        }
    ) {
        self.sendTimeoutSeconds = max(0.2, sendTimeoutSeconds)
        self.identityStore = identityStore
        self.webSocketFactory = webSocketFactory
    }

    var isOpen: Bool {
        lock.withLock { webSocket != nil }
    }

    @discardableResult
    func attach(
        url: URL,
        tlsSpkiSha256: String? = nil,
        secureContext: SecurePairingContext? = nil,
        toolMessageHandler: (@Sendable (String) async -> String?)? = nil,
        toolResponseSentHandler: (@Sendable (String, String) async -> Void)? = nil,
        closeHandler: (@Sendable (String) async -> Void)? = nil
    ) async throws -> SecureAuthenticationResult? {
        await close(notify: false)
        let task = webSocketFactory(url, tlsSpkiSha256)
        lock.withLock {
            webSocket = task
            self.toolMessageHandler = toolMessageHandler
            self.toolResponseSentHandler = toolResponseSentHandler
            self.closeHandler = closeHandler
        }
        task.resume()
        let authenticationResult: SecureAuthenticationResult?
        do {
            if let secureContext {
                authenticationResult = try await authenticate(task, context: secureContext)
                lock.withLock {
                    authenticatedSessionId = authenticationResult?.sessionId
                    authenticatedGrant = authenticationResult?.grant
                }
            } else {
                authenticationResult = nil
            }
        } catch {
            await close(notify: false)
            throw error
        }
        receiveTask = Task { [weak self] in
            await self?.runReceivePump(task)
        }
        return authenticationResult
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
            await closeIfCurrent(socket, reason: "Failed to send WebSocket payload: \(error.localizedDescription)", notify: true)
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
            await closeIfCurrent(socket, reason: "Failed to send WebSocket binary payload: \(error.localizedDescription)", notify: true)
            return .failure("Failed to send WebSocket binary payload: \(error.localizedDescription)")
        }
    }

    func close(reason: String = "WebSocket session closed.", notify: Bool = false) async {
        let state = lock.withLock { () -> (
            (any PairingWebSocket)?,
            Task<Void, Never>?,
            (@Sendable (String) async -> Void)?
        ) in
            let state = (webSocket, receiveTask)
            webSocket = nil
            receiveTask = nil
            toolMessageHandler = nil
            toolResponseSentHandler = nil
            authenticatedSessionId = nil
            authenticatedGrant = nil
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
                    await self.closeIfCurrent(socket, reason: "Failed to send \(envelope.action): \(error.localizedDescription)", notify: true)
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
        using socket: any PairingWebSocket
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

    private func runReceivePump(_ socket: any PairingWebSocket) async {
        var closeReason = "WebSocket session closed."
        while !Task.isCancelled {
            do {
                let message = try await socket.receive()
                switch message {
                case .string(let text):
                    guard text.utf8.count <= Self.maximumInboundTextMessageBytes else {
                        throw TransportError.messageTooLarge
                    }
                    await handleIncomingText(text)
                case .data(let data):
                    guard data.count <= Self.maximumInboundBinaryMessageBytes else {
                        throw TransportError.messageTooLarge
                    }
                    continue
                @unknown default:
                    continue
                }
            } catch {
                closeReason = "WebSocket receive failed: \(error.localizedDescription)"
                break
            }
        }

        await closeIfCurrent(socket, reason: closeReason, notify: true)
    }

    private func closeIfCurrent(_ socket: any PairingWebSocket, reason: String, notify: Bool) async {
        let state = lock.withLock { () -> (
            Task<Void, Never>?,
            (@Sendable (String) async -> Void)?
        )? in
            guard let currentSocket = webSocket,
                  currentSocket === socket
            else {
                return nil
            }

            let receiveTask = receiveTask
            webSocket = nil
            self.receiveTask = nil
            toolMessageHandler = nil
            toolResponseSentHandler = nil
            authenticatedSessionId = nil
            authenticatedGrant = nil
            let handler = closeHandler
            closeHandler = nil
            let pending = pendingResponses
            pendingResponses.removeAll()
            for continuation in pending.values {
                continuation.resume(throwing: TransportError.closed)
            }
            return (receiveTask, handler)
        }

        guard let state else {
            return
        }

        socket.cancel(with: .normalClosure, reason: nil)
        state.0?.cancel()
        if notify, let handler = state.1 {
            await handler(reason)
        }
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

    private func authenticate(
        _ socket: any PairingWebSocket,
        context: SecurePairingContext
    ) async throws -> SecureAuthenticationResult {
        let challengeText = try await receiveText(from: socket)
        if let error = decodeAuthError(challengeText) {
            throw error
        }
        let challenge = try JSONDecoder.ansightDecoder.decode(AuthChallengeV2.self, from: Data(challengeText.utf8))
        guard challenge.type == "AUTH_CHALLENGE_V2",
              challenge.ver == SecurePairingProtocol.version,
              challenge.requestId == context.request.requestId,
              challenge.configId == context.config.configId,
              challenge.appId == context.config.appId,
              challenge.clientNonce == context.request.clientNonce,
              challenge.hostNonce == context.offer.hostNonce,
              SecurePairingProtocol.decodedBase64URL(challenge.authSessionId)?.count == 16,
              SecurePairingProtocol.decodedBase64URL(challenge.serverChallenge)?.count == 32,
              SecurePairingProtocol.parseTimestamp(challenge.expiresAt).map({
                  let now = Date()
                  return now <= $0 && $0 <= now.addingTimeInterval(60)
              }) == true
        else {
            throw PairingDocumentError.invalidDocument("Secure authentication challenge did not match the signed offer.")
        }

        guard let hostId = context.config.host.hostId else {
            throw PairingDocumentError.invalidDocument("Secure pairing host identity is missing.")
        }
        var identity = try identityStore.loadOrCreate(hostId: hostId, appId: context.config.appId)
        if let grant = identity.grant,
           grant.hostId == hostId,
           grant.appId == context.config.appId,
           grant.clientKeyId == identity.keyId,
           SecurePairingProtocol.verifyGrant(grant, hostPublicKey: context.config.host.hostPubKey) {
            let proofInput = SecurePairingProtocol.reconnectProofInput(
                request: context.request,
                offer: context.offer,
                challenge: challenge,
                grantId: grant.grantId,
                clientKeyId: identity.keyId
            )
            let proof = AuthProveV2(
                type: "AUTH_PROVE_V2",
                ver: SecurePairingProtocol.version,
                authSessionId: challenge.authSessionId,
                grantId: grant.grantId,
                clientKeyId: identity.keyId,
                signatureAlgorithm: SecurePairingProtocol.signatureAlgorithm,
                signature: try identity.signatureBase64(for: proofInput)
            )
            try await sendCodable(proof, using: socket)
        } else {
            guard let enrollment = context.config.enrollment else {
                throw PairingDocumentError.invalidDocument("Secure enrollment is unavailable and no valid grant is stored.")
            }
            let maximumScopes = Set(enrollment.maxScopes)
            let requestedScopes = SecurePairingProtocol.canonicalScopes(context.requestedScopes)
                .filter { maximumScopes.contains($0) }
            let requestCritical = context.requestCritical && enrollment.allowCritical
            let proofInput = SecurePairingProtocol.enrollmentProofInput(
                config: context.config,
                request: context.request,
                offer: context.offer,
                challenge: challenge,
                clientKeyId: identity.keyId,
                clientPublicKey: identity.publicKeyBase64,
                requestedScopes: requestedScopes,
                requestCritical: requestCritical
            )
            let enroll = AuthEnrollV2(
                type: "AUTH_ENROLL_V2",
                ver: SecurePairingProtocol.version,
                authSessionId: challenge.authSessionId,
                ticketId: enrollment.ticketId,
                clientKeyId: identity.keyId,
                clientPublicKey: identity.publicKeyBase64,
                requestedScopes: requestedScopes,
                requestCritical: requestCritical,
                proofAlgorithm: SecurePairingProtocol.proofAlgorithm,
                proof: try SecurePairingProtocol.enrollmentProof(secret: enrollment.secret, input: proofInput)
            )
            try await sendCodable(enroll, using: socket)
        }

        let resultText = try await receiveText(from: socket)
        if let error = decodeAuthError(resultText) {
            throw error
        }
        let result = try JSONDecoder.ansightDecoder.decode(AuthOkV2.self, from: Data(resultText.utf8))
        guard result.type == "AUTH_OK_V2",
              result.ver == SecurePairingProtocol.version,
              !result.sessionId.isEmpty,
              result.grant.hostId == hostId,
              result.grant.configId == context.config.configId,
              result.grant.appId == context.config.appId,
              result.grant.clientKeyId == identity.keyId,
              SecurePairingProtocol.verifyGrant(result.grant, hostPublicKey: context.config.host.hostPubKey)
        else {
            throw PairingDocumentError.invalidDocument("Secure authentication result or grant signature is invalid.")
        }

        identity.grant = result.grant
        try identityStore.save(identity, hostId: hostId, appId: context.config.appId)
        return SecureAuthenticationResult(sessionId: result.sessionId, grant: result.grant)
    }

    private func sendCodable<T: Encodable>(_ value: T, using socket: any PairingWebSocket) async throws {
        let data = try JSONEncoder.ansightEncoder.encode(value)
        try await sendWebSocketMessage(.string(String(decoding: data, as: UTF8.self)), using: socket)
    }

    private func receiveText(from socket: any PairingWebSocket) async throws -> String {
        let message = try await withThrowingTaskGroup(of: URLSessionWebSocketTask.Message.self) { group in
            group.addTask {
                try await socket.receive()
            }
            group.addTask {
                let nanoseconds = UInt64(Self.authenticationTimeoutSeconds * 1_000_000_000)
                try await Task.sleep(nanoseconds: nanoseconds)
                throw TransportError.authenticationTimeout
            }
            guard let first = try await group.next() else {
                throw TransportError.closed
            }
            group.cancelAll()
            return first
        }
        guard case .string(let text) = message else {
            throw PairingDocumentError.invalidDocument("Secure authentication expected a JSON text message.")
        }
        guard text.utf8.count <= Self.maximumAuthenticationMessageBytes else {
            throw TransportError.messageTooLarge
        }
        return text
    }

    private func decodeAuthError(_ text: String) -> AuthErrorV2? {
        guard let probe = try? JSONDecoder.ansightDecoder.decode(AuthMessageTypeProbe.self, from: Data(text.utf8)),
              probe.type == "AUTH_ERROR_V2"
        else {
            return nil
        }
        return try? JSONDecoder.ansightDecoder.decode(AuthErrorV2.self, from: Data(text.utf8))
    }
}

struct SecureAuthenticationResult: Sendable {
    let sessionId: String
    let grant: PairingGrantV2
}

private struct AuthMessageTypeProbe: Decodable {
    let type: String
}
