import CryptoKit
import XCTest
@_spi(AnsightValidation) @testable import AnsightKit

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

    func testAutoConnectionUsesCachedPairingProfileBeforeSavedConfig() throws {
        try XCTSkipIf(
            AnsightDeveloperMode.embeddedPairingJson != nil,
            "Embedded developer pairing intentionally takes precedence over cached test stores."
        )
        let savedStore = MemoryPairingConfigStore()
        let cachedStore = MemoryPairingConfigStore()
        try savedStore.save(TestPairingFactory.configDocumentJSON(configId: "cfg-saved", hostAddress: "127.0.0.1"))
        try cachedStore.save(TestPairingFactory.cachedProfileJSON(configId: "cfg-cached", hostAddress: "127.0.0.2"))

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: savedStore, cached: cachedStore)

        let status = AnsightRuntime.shared.hostConnectionStatus()
        XCTAssertTrue(status.hasSavedConfig)
        XCTAssertTrue(status.hasCachedSession)
        XCTAssertEqual(status.summaryKind, .disconnectedMultipleConfigsAvailable)

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestForTesting(.auto())
        XCTAssertEqual(resolved.source, .cachedSession)
        XCTAssertEqual(resolved.document.config.configId, "cfg-cached")
        XCTAssertEqual(resolved.document.discoveryHint?.hostAddress, "127.0.0.2")
    }

    func testAutoConnectionOrdersCachedPairingProfilesNewestFirst() throws {
        try XCTSkipIf(
            AnsightDeveloperMode.embeddedPairingJson != nil,
            "Embedded developer pairing intentionally takes precedence over cached test stores."
        )
        let savedStore = MemoryPairingConfigStore()
        let cachedStore = MemoryPairingConfigStore()
        try savedStore.save(TestPairingFactory.configDocumentJSON(configId: "cfg-saved", hostAddress: "127.0.0.1"))
        try cachedStore.save(
            TestPairingFactory.cachedProfileCollectionJSON([
                TestPairingFactory.cachedProfile(
                    configId: "cfg-home",
                    hostAddress: "127.0.0.2",
                    wifiName: "Home Wi-Fi",
                    cachedAt: Date(timeIntervalSince1970: 1_000)
                ),
                TestPairingFactory.cachedProfile(
                    configId: "cfg-office",
                    hostAddress: "127.0.0.3",
                    wifiName: "Office Wi-Fi",
                    cachedAt: Date(timeIntervalSince1970: 2_000)
                ),
            ])
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: savedStore, cached: cachedStore)

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestsForTesting(.auto())

        XCTAssertEqual(resolved.map { $0.document.config.configId }, ["cfg-office", "cfg-home", "cfg-saved"])
        XCTAssertEqual(resolved[0].source, .cachedSession)
        XCTAssertEqual(resolved[1].source, .cachedSession)
        XCTAssertEqual(resolved[2].source, .savedConfig)
    }

    func testLegacySingleCachedPairingProfileMigratesToCollectionDocument() throws {
        let cachedStore = MemoryPairingConfigStore()
        try cachedStore.save(
            TestPairingFactory.cachedProfileJSON(
                configId: "cfg-legacy",
                hostAddress: "127.0.0.2",
                wifiName: "Legacy Wi-Fi"
            )
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: cachedStore)

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestForTesting(.auto())
        XCTAssertEqual(resolved.document.config.configId, "cfg-legacy")

        let rewrittenJson = try XCTUnwrap(cachedStore.load())
        let rewritten = try JSONDecoder.ansightDecoder.decode(
            CachedPairingProfileCollectionDocument.self,
            from: Data(rewrittenJson.utf8)
        )
        XCTAssertEqual(rewritten.schema, CachedPairingProfileCollectionDocument.schemaName)
        XCTAssertEqual(rewritten.profiles.count, 1)
        XCTAssertEqual(rewritten.profiles[0].networkKey, "wifi:Legacy Wi-Fi")
        XCTAssertEqual(rewritten.profiles[0].wifiName, "Legacy Wi-Fi")
    }

    func testCachedPairingProfileSaveRefreshesMatchingWifiNetwork() throws {
        let cachedStore = MemoryPairingConfigStore()
        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: cachedStore)

        AnsightRuntime.shared.saveCachedPairingProfileForTesting(
            TestPairingFactory.configDocument(
                configId: "cfg-home-old",
                hostAddress: "127.0.0.2",
                wifiName: "Home Wi-Fi"
            )
        )
        AnsightRuntime.shared.saveCachedPairingProfileForTesting(
            TestPairingFactory.configDocument(
                configId: "cfg-home-new",
                hostAddress: "127.0.0.3",
                wifiName: "Home Wi-Fi"
            )
        )

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestsForTesting(.auto())
        XCTAssertEqual(resolved.map { $0.document.config.configId }, ["cfg-home-new"])

        let json = try XCTUnwrap(cachedStore.load())
        let collection = try JSONDecoder.ansightDecoder.decode(
            CachedPairingProfileCollectionDocument.self,
            from: Data(json.utf8)
        )
        XCTAssertEqual(collection.profiles.count, 1)
        XCTAssertEqual(collection.profiles[0].networkKey, "wifi:Home Wi-Fi")
    }

    func testSavedPairingAndCachedSessionCanBeClearedSeparately() throws {
        let savedStore = MemoryPairingConfigStore()
        let cachedStore = MemoryPairingConfigStore()
        try savedStore.save(TestPairingFactory.configDocumentJSON(configId: "cfg-saved"))
        try cachedStore.save(TestPairingFactory.cachedProfileJSON(configId: "cfg-cached"))

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: savedStore, cached: cachedStore)
        AnsightRuntime.shared.clearSavedPairing()

        var status = AnsightRuntime.shared.hostConnectionStatus()
        XCTAssertFalse(status.hasSavedConfig)
        XCTAssertTrue(status.hasCachedSession)
        XCTAssertEqual(status.summaryKind, .disconnectedCachedSessionAvailable)

        AnsightRuntime.shared.clearCachedSession()
        status = AnsightRuntime.shared.hostConnectionStatus()
        XCTAssertFalse(status.hasSavedConfig)
        XCTAssertFalse(status.hasCachedSession)
        XCTAssertEqual(status.summaryKind, .disconnectedNoConfigs)
    }

    func testExpiredCachedPairingProfileFallsBackToSavedConfig() throws {
        try XCTSkipIf(
            AnsightDeveloperMode.embeddedPairingJson != nil,
            "Embedded developer pairing intentionally takes precedence over cached test stores."
        )
        let savedStore = MemoryPairingConfigStore()
        let cachedStore = MemoryPairingConfigStore()
        try savedStore.save(TestPairingFactory.configDocumentJSON(configId: "cfg-saved", hostAddress: "127.0.0.1"))
        try cachedStore.save(
            TestPairingFactory.cachedProfileJSON(
                configId: "cfg-expired",
                hostAddress: "127.0.0.2",
                expiresAt: Date().addingTimeInterval(-60)
            )
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: savedStore, cached: cachedStore)

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestForTesting(.auto())
        XCTAssertEqual(resolved.source, .savedConfig)
        XCTAssertEqual(resolved.document.config.configId, "cfg-saved")
        XCTAssertNil(cachedStore.load())

        let status = AnsightRuntime.shared.hostConnectionStatus()
        XCTAssertFalse(status.hasCachedSession)
        XCTAssertTrue(status.hasSavedConfig)
        XCTAssertEqual(status.summaryKind, .disconnectedSavedConfigAvailable)
    }

    func testAutoConnectionRetriesCachedProfilesInResolvedOrder() async throws {
        try XCTSkipIf(
            AnsightDeveloperMode.embeddedPairingJson != nil,
            "Embedded developer pairing intentionally takes precedence over cached test stores."
        )
        let cachedStore = MemoryPairingConfigStore()
        try cachedStore.save(
            TestPairingFactory.cachedProfileCollectionJSON([
                TestPairingFactory.cachedProfile(
                    configId: "cfg-home",
                    hostAddress: "127.0.0.2",
                    wifiName: "Home Wi-Fi",
                    cachedAt: Date(timeIntervalSince1970: 1_000)
                ),
                TestPairingFactory.cachedProfile(
                    configId: "cfg-office",
                    hostAddress: "127.0.0.3",
                    wifiName: "Office Wi-Fi",
                    cachedAt: Date(timeIntervalSince1970: 2_000)
                ),
            ])
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: cachedStore)
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-office": .failure("Office profile timed out.", code: PairingFailureCodes.udpBootstrapTimeout),
                "cfg-home": .failure("Home profile failed.", code: PairingFailureCodes.udpBootstrapFailed),
            ]
        )
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        let result = await AnsightRuntime.shared.connect(.auto(clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(connector.attemptedConfigIds, ["cfg-office", "cfg-home"])
        XCTAssertEqual(result.source, .cachedSession)
        XCTAssertEqual(result.reasonCode, PairingFailureCodes.udpBootstrapFailed)
        XCTAssertEqual(result.openSession?.configId, "cfg-home")
        XCTAssertNil(cachedStore.load())
    }

    func testAutoConnectionFallsBackFromSavedConfigToBundledConfigWhenSavedConfigIsStale() async throws {
        try XCTSkipIf(
            AnsightDeveloperMode.embeddedPairingJson != nil,
            "Embedded developer pairing intentionally takes precedence over cached test stores."
        )
        let savedStore = MemoryPairingConfigStore()
        try savedStore.save(TestPairingFactory.configDocumentJSON(configId: "cfg-saved", hostAddress: "127.0.0.2"))
        let bundledJson = try TestPairingFactory.configDocumentJSON(configId: "cfg-bundled", hostAddress: "127.0.0.3")

        try AnsightRuntime.shared.initialize(options: AnsightOptions(
            hostAutoProbe: .disabledDefault,
            hostConnection: AnsightHostConnectionOptions(bundledConfigJson: bundledJson)
        ))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: savedStore, cached: MemoryPairingConfigStore())
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-saved": .failure("Saved profile has no current host address.", code: PairingFailureCodes.hostAddressRequired),
                "cfg-bundled": .failure("Bundled profile failed.", code: PairingFailureCodes.udpBootstrapFailed),
            ]
        )
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        let result = await AnsightRuntime.shared.connect(.auto(clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(connector.attemptedConfigIds, ["cfg-saved", "cfg-bundled"])
        XCTAssertEqual(result.source, .bundledConfig)
        XCTAssertEqual(result.reasonCode, PairingFailureCodes.udpBootstrapFailed)
        XCTAssertEqual(result.openSession?.configId, "cfg-bundled")
    }

    func testQrCodeConnectionUsesRegisteredConfigReaderPayload() async throws {
        let pairingJson = try TestPairingFactory.configDocumentJSON(configId: "cfg-qr", hostAddress: "127.0.0.4")
        let reader = FakeHostConnectionConfigReader(supportedKinds: [.qrCode], payload: pairingJson)
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-qr": .failure("QR profile failed.", code: PairingFailureCodes.udpBootstrapFailed),
            ]
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: MemoryPairingConfigStore())
        AnsightRuntime.shared.setHostConnectionConfigReader(reader)
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.setHostConnectionConfigReader(nil)
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        let result = await AnsightRuntime.shared.connect(.qrCode(title: "Scan Pairing QR", clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(reader.readRequestKinds, [.qrCode])
        XCTAssertEqual(connector.attemptedConfigIds, ["cfg-qr"])
        XCTAssertEqual(result.source, .configReader)
        XCTAssertEqual(result.reasonCode, PairingFailureCodes.udpBootstrapFailed)
        XCTAssertEqual(result.openSession?.configId, "cfg-qr")
    }

    func testFileConnectionFailsWhenNoConfigReaderCanReadRequest() async throws {
        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.setHostConnectionConfigReader(nil)

        let result = await AnsightRuntime.shared.connect(.file(title: "Import Pairing Config", clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(result.source, .configReader)
        XCTAssertEqual(result.reasonCode, PairingFailureCodes.unsupportedSource)
        XCTAssertNil(result.openSession)
    }

    func testPlatformHostConnectionConfigReaderAdvertisesPlatformUiRequestKinds() {
        let reader = PlatformHostConnectionConfigReader()

        #if canImport(UIKit)
        XCTAssertTrue(reader.canRead(.file))
        XCTAssertTrue(reader.canRead(.qrCode))
        #else
        XCTAssertFalse(reader.canRead(.file))
        XCTAssertFalse(reader.canRead(.qrCode))
        #endif
        XCTAssertFalse(reader.canRead(.auto))
        XCTAssertFalse(reader.canRead(.savedConfig))
        XCTAssertFalse(reader.canRead(.bundledConfig))
        XCTAssertFalse(reader.canRead(.payload))
        XCTAssertFalse(reader.canRead(.config))
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

    func testDeviceProfileSubprofilesSerializeProtocolFields() throws {
        let gpu = DeviceGpuProfile(
            vendor: "Apple",
            model: "Apple GPU",
            driver: nil,
            renderer: "Apple GPU",
            apiCode: 3,
            driverVersion: "metal",
            vramMb: 1024,
            featureLevel: "common3"
        )
        let network = DeviceNetworkProfile(
            transportCode: 2,
            metered: false,
            effectiveType: "wifi",
            rttMs: nil,
            downKbps: nil
        )
        let stackEntry = DeviceRuntimeStackEntry(
            runtimeCode: 2,
            name: "ios",
            version: "26.4.0",
            layer: "platform"
        )

        let gpuObject = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder.ansightEncoder.encode(gpu)) as? [String: Any])
        let networkObject = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder.ansightEncoder.encode(network)) as? [String: Any])
        let stackObject = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder.ansightEncoder.encode(stackEntry)) as? [String: Any])

        XCTAssertEqual(gpuObject["renderer"] as? String, "Apple GPU")
        XCTAssertEqual(gpuObject["apiCode"] as? Int, 3)
        XCTAssertEqual(gpuObject["driverVersion"] as? String, "metal")
        XCTAssertEqual(gpuObject["vramMb"] as? Int, 1024)
        XCTAssertEqual(gpuObject["featureLevel"] as? String, "common3")
        XCTAssertEqual(networkObject["transportCode"] as? Int, 2)
        XCTAssertEqual(networkObject["metered"] as? Bool, false)
        XCTAssertEqual(networkObject["effectiveType"] as? String, "wifi")
        XCTAssertEqual(stackObject["runtimeCode"] as? Int, 2)
    }

    func testAutomaticDeviceProfileIncludesRuntimeAndCoarseNetworkShape() throws {
        let profile = AnsightDeviceAppProfileCollector.collect(reasonCode: 4, profileSeq: 99)

        XCTAssertEqual(profile.type, "DeviceAppProfile")
        XCTAssertEqual(profile.schema, "ansight.device-app-profile.v1")
        XCTAssertEqual(profile.reasonCode, 4)
        XCTAssertEqual(profile.profileSeq, 99)
        XCTAssertEqual(profile.sdk?.language?.lowercased(), "swift")
        XCTAssertEqual(profile.device?.osName, profile.device?.osName?.lowercased())
        XCTAssertNotNil(profile.runtime?.primary)
        XCTAssertTrue(profile.runtime?.stack?.contains { $0.runtimeCode != nil } == true)
        XCTAssertEqual(profile.runtime?.engine?.name, "Swift")
        XCTAssertTrue([1, 3].contains(try XCTUnwrap(profile.app?.environmentCode)))

        let data = try JSONEncoder.ansightEncoder.encode(profile)
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertFalse(json.contains(#""ssid""#))
        XCTAssertFalse(json.contains(#""wifiName""#))

        if let network = profile.device?.network {
            XCTAssertNotNil(network.transportCode)
            XCTAssertNil(network.rttMs)
            XCTAssertNil(network.downKbps)
        }
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

    func testRuntimeRecordsValidationTouchInputInDebugSnapshot() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(hostAutoProbe: .disabledDefault)
        )
        try AnsightRuntime.shared.activate()

        AnsightRuntime.shared.recordValidationTouchInput(
            action: "began",
            pointerId: 42,
            x: 10,
            y: 20,
            surfaceWidth: 100,
            surfaceHeight: 200,
            surfaceScale: 2
        )
        AnsightRuntime.shared.recordValidationTouchInput(
            action: "ended",
            pointerId: 42,
            x: 10,
            y: 20,
            surfaceWidth: 100,
            surfaceHeight: 200,
            surfaceScale: 2
        )

        let snapshot = AnsightRuntime.shared.snapshot()
        XCTAssertTrue(snapshot.touchCaptureEnabled)
        XCTAssertEqual(snapshot.touchesCaptured, 2)
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

    static func configDocumentJSON(
        configId: String,
        hostAddress: String = "127.0.0.1",
        wifiName: String? = nil,
        appId: String? = nil
    ) throws -> String {
        let data = try JSONEncoder.ansightEncoder.encode(
            configDocument(configId: configId, hostAddress: hostAddress, wifiName: wifiName, appId: appId)
        )
        return String(decoding: data, as: UTF8.self)
    }

    static func cachedProfileJSON(
        configId: String,
        hostAddress: String = "127.0.0.1",
        wifiName: String? = nil,
        cachedAt: Date = Date(),
        expiresAt: Date = Date().addingTimeInterval(600),
        appId: String? = nil
    ) throws -> String {
        let profile = cachedProfile(
            configId: configId,
            hostAddress: hostAddress,
            wifiName: wifiName,
            cachedAt: cachedAt,
            expiresAt: expiresAt,
            appId: appId
        )
        let data = try JSONEncoder.ansightEncoder.encode(profile)
        return String(decoding: data, as: UTF8.self)
    }

    static func cachedProfileCollectionJSON(_ profiles: [CachedPairingProfileDocument]) throws -> String {
        let data = try JSONEncoder.ansightEncoder.encode(
            CachedPairingProfileCollectionDocument(profiles: profiles)
        )
        return String(decoding: data, as: UTF8.self)
    }

    static func cachedProfile(
        configId: String,
        hostAddress: String = "127.0.0.1",
        wifiName: String? = nil,
        cachedAt: Date = Date(),
        expiresAt: Date = Date().addingTimeInterval(600),
        appId: String? = nil
    ) -> CachedPairingProfileDocument {
        CachedPairingProfileDocument(
            networkKey: wifiName.map { "wifi:\($0)" },
            wifiName: wifiName,
            hostName: "test-host",
            cachedAtUtc: timestamp(cachedAt),
            expiresAtUtc: timestamp(expiresAt),
            document: configDocument(configId: configId, hostAddress: hostAddress, wifiName: wifiName, appId: appId)
        )
    }

    static func configDocument(
        configId: String,
        hostAddress: String,
        wifiName: String? = nil,
        appId: String? = nil
    ) -> PairingConfigDocument {
        PairingConfigDocument(
            config: signedConfig(configId: configId, appId: appId ?? runtimeAppId()),
            discovery: PairingDiscoveryHint(
                source: "unit-test",
                hostAddress: hostAddress,
                discoveryPort: 45123,
                hostName: "test-host",
                wifiName: wifiName,
                capturedAt: timestamp(Date())
            )
        )
    }

    private static func runtimeAppId() -> String {
        Bundle.main.bundleIdentifier ?? "com.ansight.test"
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
