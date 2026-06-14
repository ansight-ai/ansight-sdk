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

    func testParseDocumentAcceptsBarePairingConfigPayload() throws {
        let config = TestPairingFactory.signedConfig()
        let configJson = String(decoding: try JSONEncoder().encode(config), as: UTF8.self)

        let document = try PairingConfigDocumentService().parseDocument(configJson)

        XCTAssertEqual(document.config.configId, config.configId)
        XCTAssertNil(document.discoveryHint)
    }

    func testParseDocumentAcceptsStudioPublicConfigWithoutDiscoveryPort() throws {
        let config = TestPairingFactory.signedConfig()
        let configJson = try TestPairingFactory.studioPublicConfigJSON(config: config)

        let document = try PairingConfigDocumentService().parseAndValidateDocument(configJson, expectedAppId: config.appId)

        XCTAssertEqual(document.config.configId, config.configId)
        XCTAssertEqual(document.config.host.discoveryPort, PairingProtocolDefaults.discoveryPort)
    }

    func testPairingCanonicalJsonMatchesStudioEscaping() {
        var config = TestPairingFactory.signedConfig(
            oneTimeToken: "token+value",
            challengePubKey: "challenge+key"
        )
        config.issuedAt = "2026-06-14T00:31:47.473808+00:00"
        config.expiresAt = "2026-06-15T00:31:47.473808+00:00"
        config.trust.mode = "pinned-key+token+challenge"

        let signable = PairingCanonicalJSON.serializePairingConfigForSignature(config)

        XCTAssertTrue(signable.contains(#""issuedAt":"2026-06-14T00:31:47.473808+00:00""#))
        XCTAssertTrue(signable.contains(#""expiresAt":"2026-06-15T00:31:47.473808+00:00""#))
        XCTAssertTrue(signable.contains(#""oneTimeToken":"token\u002Bvalue""#))
        XCTAssertTrue(signable.contains(#""challengePubKey":"challenge\u002Bkey""#))
        XCTAssertTrue(signable.contains(#""mode":"pinned-key\u002Btoken\u002Bchallenge""#))
        XCTAssertFalse(signable.contains(#""issuedAt":"2026-06-14T00:31:47.473808\u002B00:00""#))
    }

    func testSessionJpegWireProtocolEncodesHostHeader() {
        let jpegData = Data([0xFF, 0xD8, 0xFF, 0xD9])
        let frame = AnsightCapturedScreenFrame(
            capturedAtUtc: "1970-01-01T00:00:01.234Z",
            capturedAtEpochMilliseconds: 1_234,
            width: 320,
            height: 568,
            quality: 70,
            jpegData: jpegData
        )

        let payload = SessionJpegWireProtocol.encode(frame)

        XCTAssertEqual(payload.count, SessionJpegWireProtocol.headerSize + jpegData.count)
        XCTAssertEqual(Array(payload[0..<7]), [0x41, 0x53, 0x4A, 0x50, 1, 1, 70])
        XCTAssertEqual(readInt64(payload, at: 8), 1_234)
        XCTAssertEqual(readInt32(payload, at: 16), 320)
        XCTAssertEqual(readInt32(payload, at: 20), 568)
        XCTAssertEqual(readInt32(payload, at: 24), Int32(jpegData.count))
        XCTAssertEqual(payload[SessionJpegWireProtocol.headerSize..<payload.count], jpegData[0..<jpegData.count])
    }

    func testParseDocumentAcceptsPairingConfigDocumentPayload() throws {
        let config = TestPairingFactory.signedConfig(configId: "cfg-document")
        let configDocument = PairingConfigDocument(
            config: config,
            discovery: PairingDiscoveryHint(source: "document-test", hostAddress: "127.0.0.2", discoveryPort: 45123)
        )
        let documentJson = String(decoding: try JSONEncoder().encode(configDocument), as: UTF8.self)

        let document = try PairingConfigDocumentService().parseAndValidateDocument(documentJson, expectedAppId: config.appId)

        XCTAssertEqual(document.config.configId, "cfg-document")
        XCTAssertEqual(document.discoveryHint?.hostAddress, "127.0.0.2")
        XCTAssertEqual(document.discoveryHint?.discoveryPort, 45123)
    }

    func testOptionsClampToSharedTelemetryBoundsAndRejectReservedCustomChannels() throws {
        let options = try AnsightOptions(
            sampleFrequencyMilliseconds: 50,
            retentionPeriodSeconds: 10
        ).validated()

        XCTAssertEqual(options.sampleFrequencyMilliseconds, 200)
        XCTAssertEqual(options.retentionPeriodSeconds, 60)
        XCTAssertTrue(options.lifecycleCapture.enabled)

        let lifecycleOptions = try AnsightOptions(
            lifecycleCapture: AnsightLifecycleCaptureOptions(
                minimumScreenViewIntervalMilliseconds: -1
            )
        ).validated()
        XCTAssertEqual(lifecycleOptions.lifecycleCapture.minimumScreenViewIntervalMilliseconds, 0)

        let disabledLifecycleOptions = try AnsightOptions(
            lifecycleCapture: .disabled
        ).validated()
        XCTAssertFalse(disabledLifecycleOptions.lifecycleCapture.enabled)

        XCTAssertThrowsError(
            try AnsightOptions(additionalChannels: [
                AnsightChannel(id: AnsightChannels.framesPerSecond, name: "Nope"),
            ]).validated()
        )
    }

    func testRuntimeRecordsLifecycleScreenAndRetainedTelemetry() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(
                additionalChannels: [
                    AnsightChannel(id: 42, name: "Custom"),
                ],
                hostAutoProbe: .disabledDefault
            )
        )
        try AnsightRuntime.shared.activate()
        try AnsightRuntime.shared.metric(12, channel: 42)
        try AnsightRuntime.shared.screenViewed("Home", details: ["route": "/"])
        AnsightRuntime.shared.setAppLifecycleState(.foreground)

        let snapshot = AnsightRuntime.shared.snapshot()

        XCTAssertTrue(snapshot.initialized)
        XCTAssertTrue(snapshot.active)
        XCTAssertEqual(snapshot.metricsRecorded, 1)
        XCTAssertEqual(snapshot.lifecycleState, .foreground)
        XCTAssertEqual(snapshot.currentScreen?.name, "Home")
        XCTAssertTrue(snapshot.channels.contains { $0.id == 42 && $0.name == "Custom" })
        XCTAssertTrue(AnsightRuntime.shared.recordedEvents().contains { $0.type == .screenViewed })
        XCTAssertTrue(AnsightRuntime.shared.recordedEvents().contains { $0.type == .lifecycle })
    }

    func testScreenRouteResolverOverridesDefaultDescriptor() {
        let defaultDescriptor = AnsightScreenDescriptor(
            name: "GameView",
            key: "UIHostingController:GameView",
            details: [
                "source": "uikit",
                "viewController": "UIHostingController",
            ]
        )
        let context = AnsightScreenRouteContext(
            source: "uikit",
            defaultName: "GameView",
            defaultKey: defaultDescriptor.key,
            title: nil,
            viewControllerName: "UIHostingController",
            viewControllerTypeName: "SwiftUI.UIHostingController<GameView>",
            swiftUIRootTypeName: "GameView",
            details: defaultDescriptor.details
        )
        let resolver = AnsightScreenRouteResolver { context in
            XCTAssertEqual(context.swiftUIRootTypeName, "GameView")
            return AnsightScreenRoute(
                name: "Checkout",
                key: "route:/checkout",
                details: ["route": "/checkout"]
            )
        }

        let screen = AnsightScreenRouteResolution.resolve(
            defaultDescriptor: defaultDescriptor,
            context: context,
            resolver: resolver
        )

        XCTAssertEqual(screen.name, "Checkout")
        XCTAssertEqual(screen.key, "route:/checkout")
        XCTAssertEqual(screen.details["source"], "uikit")
        XCTAssertEqual(screen.details["route"], "/checkout")
    }

    func testScreenRouteResolverFallsBackWhenRouteNameIsBlank() {
        let defaultDescriptor = AnsightScreenDescriptor(
            name: "Home",
            key: "HomeViewController:Home",
            details: ["source": "uikit"]
        )
        let context = AnsightScreenRouteContext(
            source: "uikit",
            defaultName: "Home",
            defaultKey: defaultDescriptor.key,
            title: "Home",
            viewControllerName: "HomeViewController",
            viewControllerTypeName: "TestApp.HomeViewController",
            details: defaultDescriptor.details
        )
        let resolver = AnsightScreenRouteResolver { _ in
            AnsightScreenRoute(name: "  ", key: "route:/ignored")
        }

        let screen = AnsightScreenRouteResolution.resolve(
            defaultDescriptor: defaultDescriptor,
            context: context,
            resolver: resolver
        )

        XCTAssertEqual(screen, defaultDescriptor)
    }

    func testRuntimeRecordsFrameRateSampleOnFpsChannel() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(hostAutoProbe: .disabledDefault)
        )
        try AnsightRuntime.shared.activate()

        AnsightRuntime.shared.recordFrameRateSample(59)

        let metrics = AnsightRuntime.shared.recordedMetrics()
        XCTAssertTrue(metrics.contains { $0.channel == AnsightChannels.framesPerSecond && $0.value == 59 })
        XCTAssertEqual(AnsightRuntime.shared.snapshot().lastFrameRate, 59)
    }

    func testTelemetrySequencesContinueAfterRetentionTrim() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(
                sampleFrequencyMilliseconds: 2_000,
                retentionPeriodSeconds: 60,
                hostAutoProbe: .disabledDefault
            )
        )
        try AnsightRuntime.shared.activate()

        for value in 1...65 {
            try AnsightRuntime.shared.metric(Int64(value))
            try AnsightRuntime.shared.event("event-\(value)")
        }

        let metrics = AnsightRuntime.shared.recordedMetrics()
        let events = AnsightRuntime.shared.recordedEvents()

        XCTAssertEqual(metrics.count, 60)
        XCTAssertEqual(metrics.first?.sequence, 6)
        XCTAssertEqual(metrics.last?.sequence, 65)
        XCTAssertEqual(events.count, 60)
        XCTAssertEqual(events.first?.sequence, 6)
        XCTAssertEqual(events.last?.sequence, 65)
    }

    func testRuntimeIgnoresUnknownMetricChannels() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(hostAutoProbe: .disabledDefault)
        )
        try AnsightRuntime.shared.activate()

        try AnsightRuntime.shared.metric(42, channel: 77)

        XCTAssertFalse(AnsightRuntime.shared.recordedMetrics().contains { $0.channel == 77 })
        XCTAssertFalse(AnsightRuntime.shared.snapshot().channels.contains { $0.id == 77 })
    }

    func testSessionControlPayloadsMatchProtocolShape() throws {
        let clientLogPayload = AnsightRuntime.makeClientLogPayload(" hello ")
        guard case .object(let clientLogObject) = clientLogPayload else {
            return XCTFail("Expected client log object payload.")
        }
        XCTAssertEqual(clientLogObject["data"], .string("hello"))

        let propertiesPayload = AnsightRuntime.makeSessionPropertiesPayload([
            " app ": [
                " tenant ": "acme",
                "": "ignored",
            ],
            "": [
                "region": "ignored",
            ],
        ])
        guard case .object(let propertiesObject) = propertiesPayload,
              case .object(let customProperties)? = propertiesObject["customProperties"],
              case .object(let appProperties)? = customProperties["app"] else {
            return XCTFail("Expected normalized custom properties payload.")
        }
        XCTAssertEqual(appProperties["tenant"], .string("acme"))
        XCTAssertNotNil(propertiesObject["updatedAtUtc"])

        let completePayload = AnsightRuntime.makeSessionCompletePayload()
        guard case .object(let completeObject) = completePayload else {
            return XCTFail("Expected session complete object payload.")
        }
        XCTAssertEqual(completeObject["reason"], .string("client log stream complete"))
    }

    func testTelemetryPayloadBuildersMatchProtocolShape() throws {
        let metricsPayload = AnsightRuntime.makeMetricsPayload([
            RecordedMetric(value: 123, channel: 42, capturedAtUtc: "2026-06-14T00:00:00Z"),
        ])
        let metricsRoot = try JSONSerialization.jsonObject(with: metricsPayload.jsonData()) as? [String: Any]
        XCTAssertEqual(metricsRoot?["source"] as? String, "client")
        XCTAssertEqual(metricsRoot?["type"] as? String, "CLIENT_METRICS")
        let metrics = try XCTUnwrap(metricsRoot?["metrics"] as? [[String: Any]])
        XCTAssertEqual(metrics.first?["channel"] as? Int, 42)
        XCTAssertEqual(metrics.first?["value"] as? Int, 123)

        let eventsPayload = AnsightRuntime.makeEventsPayload([
            RecordedEvent(
                id: "event-1",
                label: "Opened",
                type: .info,
                details: "details",
                channel: AnsightChannels.unspecified,
                capturedAtUtc: "2026-06-14T00:00:01Z"
            ),
        ])
        let eventsRoot = try JSONSerialization.jsonObject(with: eventsPayload.jsonData()) as? [String: Any]
        XCTAssertEqual(eventsRoot?["source"] as? String, "client")
        XCTAssertEqual(eventsRoot?["type"] as? String, "CLIENT_EVENTS")
        let events = try XCTUnwrap(eventsRoot?["events"] as? [[String: Any]])
        XCTAssertEqual(events.first?["id"] as? String, "event-1")
        XCTAssertEqual(events.first?["eventType"] as? String, "Info")
    }

    func testDeviceApplicationIconProfileSerializesInlineIconPayload() throws {
        let profile = DeviceAppProfile(
            app: DeviceApplicationProfile(
                appId: "com.example.icon",
                appName: "Icon",
                icon: DeviceApplicationIconProfile(
                    format: "png",
                    mimeType: "image/png",
                    width: 2,
                    height: 2,
                    byteCount: 3,
                    dataBase64: "AQID"
                ),
                processId: nil,
                versionName: nil,
                versionCode: nil,
                buildNumber: nil,
                environmentCode: nil,
                installSource: nil,
                firstInstallTimeMs: nil,
                lastUpdateTimeMs: nil,
                debuggable: nil
            )
        )

        let data = try JSONEncoder.ansightEncoder.encode(profile)
        let json = String(decoding: data, as: UTF8.self)
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        let app = try XCTUnwrap(root["app"] as? [String: Any])
        let icon = try XCTUnwrap(app["icon"] as? [String: Any])

        XCTAssertEqual(icon["format"] as? String, "png")
        XCTAssertEqual(icon["mimeType"] as? String, "image/png")
        XCTAssertEqual(icon["width"] as? Int, 2)
        XCTAssertEqual(icon["height"] as? Int, 2)
        XCTAssertEqual(icon["byteCount"] as? Int, 3)
        XCTAssertEqual(icon["dataBase64"] as? String, "AQID")
        XCTAssertFalse(json.contains("widthPx"))
        XCTAssertFalse(json.contains("heightPx"))
    }

    func testTouchInputWireProtocolPacksStudioCompatibleBatch() throws {
        let start = Date(timeIntervalSince1970: 1_000)
        let touches = [
            AnsightCapturedTouch(
                action: .down,
                pointerId: 7,
                pointerIndex: 0,
                pointerCount: 1,
                x: 12,
                y: 34,
                surfaceWidth: 200,
                surfaceHeight: 400,
                coordinateUnit: "points",
                surfaceScale: 2,
                capturedAt: start
            ),
            AnsightCapturedTouch(
                action: .up,
                pointerId: 7,
                pointerIndex: 0,
                pointerCount: 1,
                x: 13,
                y: 35,
                surfaceWidth: 200,
                surfaceHeight: 400,
                coordinateUnit: "points",
                surfaceScale: 2,
                capturedAt: start.addingTimeInterval(0.125)
            ),
        ]

        let payloads = AnsightTouchInputWireProtocol.payloads(for: touches)

        XCTAssertEqual(payloads.count, 1)
        let data = try payloads[0].jsonData()
        let root = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        XCTAssertEqual(root?["type"] as? String, "CLIENT_TOUCH_INPUT")
        XCTAssertEqual(root?["schema"] as? String, "ansight.touches.v1")
        XCTAssertEqual(root?["space"] as? String, "w")
        XCTAssertEqual(root?["unit"] as? String, "pt")
        XCTAssertEqual(root?["surface"] as? [Double], [200, 400, 2])

        let rows = try XCTUnwrap(root?["rows"] as? [[Any]])
        XCTAssertEqual(rows.count, 2)
        XCTAssertEqual(rows[0][0] as? Int, 0)
        XCTAssertEqual(rows[0][1] as? Int, 0)
        XCTAssertEqual(rows[0][2] as? Int, 7)
        XCTAssertEqual(rows[1][0] as? Int, 125)
        XCTAssertEqual(rows[1][1] as? Int, 2)
    }

    func testRuntimeRecordsCapturedTouchInDebugSnapshot() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(hostAutoProbe: .disabledDefault)
        )
        try AnsightRuntime.shared.activate()

        AnsightRuntime.shared.recordCapturedTouch(
            AnsightCapturedTouch(
                action: .down,
                pointerId: 1,
                pointerIndex: 0,
                pointerCount: 1,
                x: 10,
                y: 20,
                surfaceWidth: 100,
                surfaceHeight: 200,
                coordinateUnit: "points",
                surfaceScale: 2
            )
        )

        let snapshot = AnsightRuntime.shared.snapshot()
        XCTAssertTrue(snapshot.touchCaptureEnabled)
        XCTAssertEqual(snapshot.touchesCaptured, 1)
        XCTAssertEqual(snapshot.touchesSent, 0)
        XCTAssertEqual(snapshot.lastTouchCaptureMessage, "Captured touch input.")
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

    private func readInt32(_ data: Data, at index: Int) -> Int32 {
        var value: Int32 = 0
        _ = withUnsafeMutableBytes(of: &value) { buffer in
            data.copyBytes(to: buffer, from: index..<index + MemoryLayout<Int32>.size)
        }
        return Int32(littleEndian: value)
    }

    private func readInt64(_ data: Data, at index: Int) -> Int64 {
        var value: Int64 = 0
        _ = withUnsafeMutableBytes(of: &value) { buffer in
            data.copyBytes(to: buffer, from: index..<index + MemoryLayout<Int64>.size)
        }
        return Int64(littleEndian: value)
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
                discoveryPort: 45123,
                hostName: "test-host",
                wifiName: nil,
                capturedAt: timestamp(Date())
            )
        )

        let data = try JSONEncoder().encode(ticket)
        return String(decoding: data, as: UTF8.self)
    }

    static func studioPublicConfigJSON(config: PairingConfig) throws -> String {
        let object: [String: Any] = [
            "schema": config.schema,
            "configId": config.configId,
            "appId": config.appId,
            "appName": config.appName,
            "issuedAt": config.issuedAt,
            "expiresAt": config.expiresAt,
            "oneTimeToken": config.oneTimeToken,
            "host": [
                "hostPubKey": config.host.hostPubKey,
                "hostPubKeyFingerprint": config.host.hostPubKeyFingerprint,
            ],
            "challenge": [
                "alg": config.challenge.alg,
                "challengePubKey": config.challenge.challengePubKey,
                "requireProofOnFirstPair": config.challenge.requireProofOnFirstPair,
            ],
            "trust": [
                "mode": config.trust.mode,
                "requireTokenOnFirstPair": config.trust.requireTokenOnFirstPair,
                "allowLanDiscovery": config.trust.allowLanDiscovery,
            ],
            "signature": config.signature,
        ]
        let data = try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])
        return String(decoding: data, as: UTF8.self)
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
