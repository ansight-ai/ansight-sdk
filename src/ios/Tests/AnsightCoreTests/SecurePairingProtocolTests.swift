import CryptoKit
import Foundation
import XCTest
@testable import AnsightCore

final class SecurePairingProtocolTests: XCTestCase {
    func testSecureConfigValidatesAndConnectorUsesSignedSecretFreeOffer() async throws {
        let fixture = try SecurePairingFixture()
        let datagram = FakePairingDatagramClient { requestData, _, _ in
            guard let request = try? JSONDecoder.ansightDecoder.decode(ConnectInitV2.self, from: requestData),
                  let offer = try? fixture.signedOffer(for: request),
                  let data = try? JSONEncoder.ansightEncoder.encode(offer)
            else { return nil }
            return data
        }
        let connector = PairingSessionConnector(
            datagramClient: datagram,
            wifiStatusProvider: { .connected },
            simulatorLocalHostAddressProvider: { nil }
        )
        let document = ParsedPairingDocument(
            config: fixture.config,
            discoveryHint: PairingDiscoveryHint(hostAddress: "192.168.1.20")
        )

        try PairingConfigDocumentService().validateDocument(document, expectedAppId: fixture.config.appId)
        let attempt = await connector.connect(
            document: document,
            clientName: "Secure test",
            options: PairingConnectionOptions(requestedScopes: ["Read"], requestCritical: false)
        )

        XCTAssertTrue(attempt.success)
        XCTAssertEqual(attempt.webSocketURL?.scheme, "wss")
        XCTAssertNil(attempt.webSocketURL?.query)
        XCTAssertEqual(attempt.secureContext?.requestedScopes, ["Read"])
        XCTAssertEqual(datagram.requestCount, 1)
    }

    func testSecureTransportEnrollsAndPersistsHostSignedGrant() async throws {
        let fixture = try SecurePairingFixture()
        let request = try SecurePairingProtocol.makeConnectInit(config: fixture.config)
        let offer = try fixture.signedOffer(for: request)
        let context = SecurePairingContext(
            config: fixture.config,
            request: request,
            offer: offer,
            requestedScopes: ["Read"],
            requestCritical: false
        )
        let clientKey = P256.Signing.PrivateKey()
        let identity = PairingClientIdentity(signingKey: .software(clientKey), grant: nil)
        let store = MemoryPairingClientIdentityStore(identity: identity)
        let challenge = AuthChallengeV2(
            type: "AUTH_CHALLENGE_V2",
            ver: 2,
            authSessionId: try SecurePairingProtocol.randomBase64URL(byteCount: 16),
            requestId: request.requestId,
            configId: fixture.config.configId,
            appId: fixture.config.appId,
            clientNonce: request.clientNonce,
            hostNonce: offer.hostNonce,
            serverChallenge: try SecurePairingProtocol.randomBase64URL(byteCount: 32),
            expiresAt: fixture.timestamp(secondsFromNow: 30)
        )
        let grant = try fixture.signedGrant(clientKeyId: identity.keyId)
        let authOk = AuthOkV2(type: "AUTH_OK_V2", ver: 2, sessionId: "secure-session", grant: grant)
        let challengeJSON = String(decoding: try JSONEncoder.ansightEncoder.encode(challenge), as: UTF8.self)
        let authOkJSON = String(decoding: try JSONEncoder.ansightEncoder.encode(authOk), as: UTF8.self)
        let socket = TestPairingWebSocket(
            sendBehavior: .complete,
            incomingMessages: [.string(challengeJSON), .string(authOkJSON)]
        )
        let transport = PairingLiveSessionTransport(
            identityStore: store,
            webSocketFactory: { _, _ in socket }
        )

        let result = try await transport.attach(
            url: try XCTUnwrap(URL(string: "wss://192.168.1.20:45124/ws/v2/test")),
            tlsSpkiSha256: fixture.tlsPin,
            secureContext: context
        )
        await transport.close()

        XCTAssertEqual(result?.sessionId, "secure-session")
        XCTAssertEqual(store.savedIdentity?.grant?.grantId, grant.grantId)
        let sent = try XCTUnwrap(socket.sentTextMessages().first)
        let enroll = try JSONDecoder.ansightDecoder.decode(AuthEnrollV2.self, from: Data(sent.utf8))
        XCTAssertEqual(enroll.type, "AUTH_ENROLL_V2")
        XCTAssertEqual(enroll.clientKeyId, identity.keyId)
        XCTAssertFalse(enroll.proof.isEmpty)
    }

    func testCanonicalJSONMatchesSystemTextJsonPlusEscaping() {
        let challenge = AuthChallengeV2(
            type: "AUTH_CHALLENGE_V2",
            ver: 2,
            authSessionId: "auth",
            requestId: "request",
            configId: "config",
            appId: "app",
            clientNonce: "client",
            hostNonce: "host",
            serverChallenge: "challenge",
            expiresAt: "2026-07-13T10:00:00.0000000+10:00"
        )

        XCTAssertEqual(
            SecurePairingProtocol.canonicalChallenge(challenge),
            #"{"type":"AUTH_CHALLENGE_V2","ver":2,"authSessionId":"auth","requestId":"request","configId":"config","appId":"app","clientNonce":"client","hostNonce":"host","serverChallenge":"challenge","expiresAt":"2026-07-13T10:00:00.0000000\u002B10:00"}"#
        )
    }

    func testRememberedProfileDeletesEnrollmentSecretAndCannotBeImported() throws {
        let fixture = try SecurePairingFixture()
        let clientKeyId = SecurePairingProtocol.base64URL(Data(repeating: 0xEF, count: 32))
        let grant = try fixture.signedGrant(clientKeyId: clientKeyId)
        let source = ParsedPairingDocument(config: fixture.config)
        let remembered = PairingRememberedProfile.replacingEnrollment(in: source, with: grant)
        let encoded = try JSONEncoder.ansightEncoder.encode(
            PairingConfigDocument(config: remembered.config)
        )
        let json = String(decoding: encoded, as: UTF8.self)

        XCTAssertTrue(remembered.config.isSecureRememberedProfile)
        XCTAssertNil(remembered.config.enrollment)
        XCTAssertFalse(json.contains(fixture.config.enrollment?.secret ?? ""))
        try SecurePairingProtocol.validateRememberedProfile(
            remembered.config,
            expectedAppId: fixture.config.appId
        )
        XCTAssertThrowsError(
            try PairingConfigDocumentService().parseAndValidateDocument(
                json,
                expectedAppId: fixture.config.appId
            )
        )
    }
}

private final class MemoryPairingClientIdentityStore: PairingClientIdentityStoring, @unchecked Sendable {
    private let lock = NSLock()
    private var identity: PairingClientIdentity
    private(set) var savedIdentity: PairingClientIdentity?

    init(identity: PairingClientIdentity) {
        self.identity = identity
    }

    func loadOrCreate(hostId: String, appId: String) throws -> PairingClientIdentity {
        lock.withLock { identity }
    }

    func save(_ identity: PairingClientIdentity, hostId: String, appId: String) throws {
        lock.withLock {
            self.identity = identity
            savedIdentity = identity
        }
    }

    func clear(hostId: String, appId: String) {
        lock.withLock { savedIdentity = nil }
    }
}

private final class SecurePairingFixture: @unchecked Sendable {
    let hostKey = P256.Signing.PrivateKey()
    let tlsPin: String
    private(set) var config: PairingConfig

    init() throws {
        tlsPin = SecurePairingProtocol.base64URL(Data(repeating: 0xAB, count: 32))
        let publicKey = hostKey.publicKey.derRepresentation.base64EncodedString()
        let hostId = try XCTUnwrap(SecurePairingProtocol.fingerprint(publicKeyBase64: publicKey))
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let now = Date()
        config = PairingConfig(
            configId: "secure-config",
            appId: "ai.ansight.secure-test",
            appName: "Secure Test",
            issuedAt: formatter.string(from: now.addingTimeInterval(-1)),
            expiresAt: formatter.string(from: now.addingTimeInterval(600)),
            host: PairingHost(
                hostId: hostId,
                hostName: "Test host",
                hostPubKey: publicKey,
                hostPubKeyFingerprint: hostId,
                tlsPins: [PairingTlsPin(
                    tlsSpkiSha256: tlsPin,
                    notBefore: formatter.string(from: now.addingTimeInterval(-60)),
                    notAfter: formatter.string(from: now.addingTimeInterval(3_600))
                )]
            ),
            enrollment: PairingEnrollment(
                ticketId: "ticket-id",
                secret: SecurePairingProtocol.base64URL(Data(repeating: 0xCD, count: 32)),
                expiresAt: formatter.string(from: now.addingTimeInterval(600)),
                grantExpiresAt: formatter.string(from: now.addingTimeInterval(3_600))
            ),
            signature: ""
        )
        config.signature = try hostKey.signature(for: Data(SecurePairingProtocol.canonicalConfig(config).utf8))
            .rawRepresentation.base64EncodedString()
    }

    func signedOffer(for request: ConnectInitV2) throws -> ConnectOfferV2 {
        var offer = ConnectOfferV2(
            type: "CONNECT_OFFER_V2",
            ver: 2,
            requestId: request.requestId,
            configId: request.configId,
            appId: request.appId,
            clientNonce: request.clientNonce,
            hostNonce: try SecurePairingProtocol.randomBase64URL(byteCount: 32),
            hostId: try XCTUnwrap(config.host.hostId),
            selectedVersion: 2,
            selectedTransport: "wss",
            webSocketPort: 45124,
            webSocketPath: "/ws/v2/test",
            tlsSpkiSha256: tlsPin,
            expiresAt: timestamp(secondsFromNow: 10),
            signatureAlgorithm: "ES256-P1363",
            signature: ""
        )
        let signature = try hostKey.signature(
            for: Data(SecurePairingProtocol.offerSignatureInput(request: request, offer: offer).utf8)
        )
        offer = ConnectOfferV2(
            type: offer.type,
            ver: offer.ver,
            requestId: offer.requestId,
            configId: offer.configId,
            appId: offer.appId,
            clientNonce: offer.clientNonce,
            hostNonce: offer.hostNonce,
            hostId: offer.hostId,
            selectedVersion: offer.selectedVersion,
            selectedTransport: offer.selectedTransport,
            webSocketPort: offer.webSocketPort,
            webSocketPath: offer.webSocketPath,
            tlsSpkiSha256: offer.tlsSpkiSha256,
            expiresAt: offer.expiresAt,
            signatureAlgorithm: offer.signatureAlgorithm,
            signature: signature.rawRepresentation.base64EncodedString()
        )
        return offer
    }

    func signedGrant(clientKeyId: String) throws -> PairingGrantV2 {
        var grant = PairingGrantV2(
            grantId: "grant-id",
            hostId: try XCTUnwrap(config.host.hostId),
            configId: config.configId,
            appId: config.appId,
            clientKeyId: clientKeyId,
            allowedScopes: ["Read"],
            allowCritical: false,
            issuedAt: timestamp(secondsFromNow: -1),
            expiresAt: timestamp(secondsFromNow: 3_600),
            signatureAlgorithm: "ES256-P1363",
            signature: ""
        )
        let signature = try hostKey.signature(for: Data(SecurePairingProtocol.canonicalGrant(grant).utf8))
        grant = PairingGrantV2(
            grantId: grant.grantId,
            hostId: grant.hostId,
            configId: grant.configId,
            appId: grant.appId,
            clientKeyId: grant.clientKeyId,
            allowedScopes: grant.allowedScopes,
            allowCritical: grant.allowCritical,
            issuedAt: grant.issuedAt,
            expiresAt: grant.expiresAt,
            signatureAlgorithm: grant.signatureAlgorithm,
            signature: signature.rawRepresentation.base64EncodedString()
        )
        return grant
    }

    func timestamp(secondsFromNow: TimeInterval) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date().addingTimeInterval(secondsFromNow))
    }
}
