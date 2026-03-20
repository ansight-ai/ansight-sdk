import CryptoKit
import XCTest
@testable import AnsightKit

final class PairingAndRuntimeTests: XCTestCase {
    func testParseDocumentAppliesConnectionHintFromBootstrapDocument() throws {
        let config = TestPairingFactory.signedConfig(configId: "cfg-trust", oneTimeToken: "token-trust")
        let hint = TestPairingFactory.connectionHint(
            configId: "cfg-effective",
            oneTimeToken: "token-effective",
            challengePubKey: "challenge-effective"
        )
        let documentJson = try TestPairingFactory.bootstrapJSON(config: config, connectionHint: hint)

        let document = try PairingConfigDocumentService().parseDocument(documentJson)

        XCTAssertEqual(document.config.configId, "cfg-effective")
        XCTAssertEqual(document.config.oneTimeToken, "token-effective")
        XCTAssertEqual(document.config.challenge.challengePubKey, "challenge-effective")
        XCTAssertEqual(document.trustAnchorConfig?.configId, "cfg-trust")
        XCTAssertEqual(document.discoveryHint?.hostAddress, "127.0.0.1")
    }

    func testOpenSessionUsesDiscoveryHintHostFallback() throws {
        let config = TestPairingFactory.signedConfig()
        let documentJson = try TestPairingFactory.bootstrapJSON(config: config, connectionHint: nil)
        XCTAssertNoThrow(
            try PairingConfigDocumentService().validateDocument(
                ParsedPairingDocument(config: config),
                expectedAppId: config.appId
            )
        )

        let parsedDocument = try PairingConfigDocumentService().parseDocument(documentJson)
        XCTAssertEqual(
            PairingCanonicalJSON.signables(for: config),
            PairingCanonicalJSON.signables(for: parsedDocument.config)
        )
        try AnsightRuntime.shared.initialize()

        let result = try AnsightRuntime.shared.openSession(
            pairingJson: documentJson,
            options: PairingOpenOptions(
                clientName: "Unit Test",
                manualHostAddress: "",
                expectedAppId: config.appId
            )
        )

        XCTAssertTrue(result.success)
        XCTAssertEqual(result.configId, config.configId)
        XCTAssertEqual(result.resolvedHostAddress, "127.0.0.1")
        XCTAssertFalse(result.usedEmbeddedDeveloperPairing)
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

        let signable = PairingCanonicalJSON.signables(for: config).first ?? ""
        let signature = try! privateKey.signature(for: Data(signable.utf8))
        config.signature = signature.derRepresentation.base64EncodedString()
        return config
    }

    static func connectionHint(
        configId: String = "cfg-override",
        oneTimeToken: String = "token-override",
        challengePubKey: String = "challenge-override"
    ) -> PairingConnectionHint {
        PairingConnectionHint(
            configId: configId,
            issuedAt: timestamp(Date().addingTimeInterval(-60)),
            expiresAt: timestamp(Date().addingTimeInterval(600)),
            oneTimeToken: oneTimeToken,
            challenge: PairingChallenge(
                alg: "ECDH-P256",
                challengePubKey: challengePubKey,
                requireProofOnFirstPair: false
            )
        )
    }

    static func bootstrapJSON(
        config: PairingConfig,
        connectionHint: PairingConnectionHint?
    ) throws -> String {
        let bootstrap = PairingBootstrapDocument(
            pairingConfig: config,
            discovery: PairingDiscoveryHint(
                source: "unit-test",
                hostAddress: "127.0.0.1",
                hostName: "test-host",
                wifiName: nil,
                capturedAt: timestamp(Date())
            ),
            connectionHint: connectionHint
        )

        let data = try JSONEncoder().encode(bootstrap)
        return String(decoding: data, as: UTF8.self)
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
