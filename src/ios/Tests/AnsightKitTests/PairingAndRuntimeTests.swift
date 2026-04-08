import CryptoKit
import XCTest
@testable import AnsightKit

final class PairingAndRuntimeTests: XCTestCase {
    func testParseDocumentParsesPairingTicket() throws {
        let config = TestPairingFactory.signedConfig(
            configId: "cfg-ticket",
            oneTimeToken: "token-ticket",
            challengePubKey: "challenge-ticket"
        )
        let documentJson = try TestPairingFactory.ticketJSON(config: config)

        let document = try PairingConfigDocumentService().parseDocument(documentJson)

        XCTAssertEqual(document.config.configId, "cfg-ticket")
        XCTAssertEqual(document.config.oneTimeToken, "token-ticket")
        XCTAssertEqual(document.config.challenge.challengePubKey, "challenge-ticket")
        XCTAssertEqual(document.discoveryHint?.hostAddress, "127.0.0.1")
    }

    func testOpenSessionUsesTicketDiscoveryHint() throws {
        let config = TestPairingFactory.signedConfig()
        let documentJson = try TestPairingFactory.ticketJSON(config: config)
        XCTAssertNoThrow(
            try PairingConfigDocumentService().validateDocument(
                ParsedPairingDocument(config: config),
                expectedAppId: config.appId
            )
        )

        let parsedDocument = try PairingConfigDocumentService().parseDocument(documentJson)
        XCTAssertEqual(parsedDocument.config.configId, config.configId)
        try AnsightRuntime.shared.initialize()

        let result = try AnsightRuntime.shared.openSession(
            pairingJson: documentJson,
            options: PairingOpenOptions(
                clientName: "Unit Test",
                expectedAppId: config.appId
            )
        )

        XCTAssertTrue(result.success)
        XCTAssertEqual(result.configId, config.configId)
        XCTAssertEqual(result.resolvedHostAddress, "127.0.0.1")
        XCTAssertFalse(result.usedEmbeddedDeveloperPairing)
    }

    func testParseDocumentRejectsLegacyBootstrapPayload() {
        let bootstrapJson = """
        {
          "schema": "ansight.pairing-bootstrap.v1",
          "pairingConfig": {}
        }
        """

        XCTAssertThrowsError(try PairingConfigDocumentService().parseDocument(bootstrapJson)) { error in
            XCTAssertTrue((error as NSError).localizedDescription.contains("no longer supported"))
        }
    }

    func testParseDocumentRejectsBarePairingConfigPayload() throws {
        let config = TestPairingFactory.signedConfig()
        let configJson = String(decoding: try JSONEncoder().encode(config), as: UTF8.self)

        XCTAssertThrowsError(try PairingConfigDocumentService().parseDocument(configJson)) { error in
            XCTAssertTrue((error as NSError).localizedDescription.contains("pairing ticket"))
        }
    }

    func testBundledToolScanReportDefaultsToNoDetectedTools() {
        let report = AnsightDeveloperMode.bundledToolScanReport

        XCTAssertTrue(report.detectedToolTypes.isEmpty)
        XCTAssertFalse(report.allowBundledTools)
    }

    func testEmbeddedDeveloperPairingIsAvailableWhenExpected() throws {
        try XCTSkipUnless(
            ProcessInfo.processInfo.environment["EXPECT_EMBEDDED_PAIRING"] == "true",
            "Set EXPECT_EMBEDDED_PAIRING=true when building with embedded developer pairing."
        )

        let embeddedJson = try XCTUnwrap(AnsightDeveloperMode.embeddedPairingJson)
        let parsed = try PairingConfigDocumentService().parseAndValidateDocument(embeddedJson)

        XCTAssertEqual(parsed.discoveryHint?.source, "developer-pairing-swiftpm")
        XCTAssertFalse(parsed.config.configId.isEmpty)
    }
}

private enum TestPairingFactory {
    static func signedConfig(
        configId: String = "cfg-1",
        appId: String = "com.ansight.test",
        oneTimeToken: String = "token-1",
        challengePubKey: String = "challenge-key"
    ) -> PairingConfig {
        let privateKey = P256.Signing.PrivateKey()
        let issuedAt = timestamp(Date().addingTimeInterval(-60))
        let expiresAt = timestamp(Date().addingTimeInterval(600))

        var config = PairingConfig(
            configId: configId,
            appId: appId,
            appName: "Ansight Tests",
            issuedAt: issuedAt,
            expiresAt: expiresAt,
            oneTimeToken: oneTimeToken,
            host: PairingHost(
                hostId: "host-1",
                hostName: "test-host",
                discoveryPort: 45123,
                hostPubKey: privateKey.publicKey.derRepresentation.base64EncodedString(),
                hostPubKeyFingerprint: "fingerprint-1"
            ),
            challenge: PairingChallenge(
                alg: "ECDH-P256",
                challengePubKey: challengePubKey,
                requireProofOnFirstPair: true
            ),
            trust: PairingTrust(
                mode: "developer",
                requireTokenOnFirstPair: true,
                allowLanDiscovery: true
            ),
            signature: ""
        )

        let signable = PairingCanonicalJSON.serializePairingConfigForSignature(config)
        let signature = try! privateKey.signature(for: Data(signable.utf8))
        config.signature = signature.derRepresentation.base64EncodedString()
        return config
    }

    static func ticketJSON(
        config: PairingConfig
    ) throws -> String {
        let ticket = PairingTicket(
            config: config,
            discovery: PairingDiscoveryHint(
                source: "unit-test",
                hostAddress: "127.0.0.1",
                hostName: "test-host",
                wifiName: nil,
                capturedAt: timestamp(Date())
            )
        )

        let data = try JSONEncoder().encode(ticket)
        return String(decoding: data, as: UTF8.self)
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
