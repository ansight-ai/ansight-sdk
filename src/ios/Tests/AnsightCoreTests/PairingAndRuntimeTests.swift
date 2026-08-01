import XCTest
@_spi(AnsightValidation) @testable import AnsightCore
@testable import AnsightPairingQR

final class PairingAndRuntimeTests: XCTestCase {
    func testLatestValueBufferReplacesOnlyThePendingValue() async {
        let buffer = AnsightLatestValueBuffer<Int>()

        let replacedFirstValue = await buffer.submit(1)
        let replacedSecondValue = await buffer.submit(2)
        let deliveredValue = await buffer.next()
        await buffer.finish()
        let valueAfterFinish = await buffer.next()

        XCTAssertFalse(replacedFirstValue)
        XCTAssertTrue(replacedSecondValue)
        XCTAssertEqual(deliveredValue, 2)
        XCTAssertNil(valueAfterFinish)
    }

    func testValidationAcceptsGenericInviteForRuntimeBundle() throws {
        let config = TestPairingFactory.enrollmentConfig(
            configId: "invite-any-app",
            appId: PairingConfig.anyAppId
        )

        XCTAssertNoThrow(
            try PairingConfigDocumentService().validateDocument(
                ParsedPairingDocument(config: config),
                expectedAppId: "com.example.actual"
            )
        )
    }

    func testParseDocumentParsesEnrollmentInviteDocument() throws {
        let config = TestPairingFactory.enrollmentConfig(configId: "invite-document")
        let documentJson = try TestPairingFactory.documentJSON(config: config)

        let document = try PairingConfigDocumentService().parseDocument(documentJson)

        XCTAssertEqual(document.config.configId, "invite-document")
        XCTAssertEqual(document.config.enrollment.accessToken, TestPairingFactory.accessToken)
        XCTAssertEqual(document.discoveryHint?.hostAddress, "127.0.0.1")
    }

    func testCompactEnrollmentCodeRoundTrips() throws {
        let config = TestPairingFactory.enrollmentConfig(configId: "invite-compact")
        let configDocument = PairingConfigDocument(
            config: config,
            discovery: PairingDiscoveryHint(
                source: "studio-qr",
                hostAddresses: [" 127.0.0.1 ", "127.0.0.1", "127.0.0.2"],
                discoveryPort: 45123
            )
        )
        let compactCode = try PairingConfigCodeGenerator.serialize(configDocument)

        let document = try PairingConfigDocumentService().parseAndValidateDocument(
            compactCode,
            expectedAppId: config.appId
        )

        XCTAssertTrue(compactCode.hasPrefix("ans2:"))
        XCTAssertEqual(document.config.configId, "invite-compact")
        XCTAssertEqual(document.discoveryHint?.hostAddresses, ["127.0.0.1", "127.0.0.2"])
        XCTAssertEqual(document.discoveryHint?.discoveryPort, 45123)
    }

    func testOpenSessionUsesTicketDiscoveryHint() throws {
        let config = TestPairingFactory.enrollmentConfig()
        let documentJson = try TestPairingFactory.documentJSON(config: config)
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
    }

    func testParseDocumentRejectsUnsupportedSchema() {
        let bootstrapJson = #"{"schema":"unsupported.enrollment","invite":{}}"#

        XCTAssertThrowsError(try PairingConfigDocumentService().parseDocument(bootstrapJson)) { error in
            XCTAssertTrue((error as NSError).localizedDescription.contains("Unsupported enrollment invite schema"))
        }
    }

    func testParseDocumentAcceptsBareEnrollmentInvitePayload() throws {
        let config = TestPairingFactory.enrollmentConfig()
        let configJson = String(decoding: try JSONEncoder().encode(config), as: UTF8.self)

        let document = try PairingConfigDocumentService().parseDocument(configJson)

        XCTAssertEqual(document.config.configId, config.configId)
        XCTAssertNil(document.discoveryHint)
    }

    func testValidationAllowsReconnectAfterInviteExpiry() throws {
        var config = TestPairingFactory.enrollmentConfig()
        config.expiresAt = "2020-01-01T00:00:00Z"
        config.enrollment.expiresAt = config.expiresAt

        XCTAssertNoThrow(
            try PairingConfigDocumentService().validateDocument(
                ParsedPairingDocument(config: config),
                expectedAppId: config.appId
            )
        )
    }

    func testPairingConnectorReturnsWifiRequiredWhenWifiPreflightReportsNotConnected() async {
        let datagramClient = FakePairingDatagramClient()
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .notConnected }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-wifi-required"),
            discoveryHint: PairingDiscoveryHint(
                hostAddress: "127.0.0.1",
                discoveryPort: 45123,
                wifiName: "Studio Wi-Fi"
            )
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(attempt.failureCode, PairingFailureCodes.wifiRequired)
        XCTAssertTrue(attempt.message.contains("same Wi-Fi"))
        XCTAssertTrue(attempt.message.contains("Last known Studio Wi-Fi: Studio Wi-Fi"))
        XCTAssertEqual(datagramClient.requestCount, 0)
    }

    func testPairingConnectorRejectsCellularWhenCellularConnectionsAreDisabled() async {
        let datagramClient = FakePairingDatagramClient()
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .cellular }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-cellular-disabled"),
            discoveryHint: PairingDiscoveryHint(hostAddress: "127.0.0.1", discoveryPort: 45_123)
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(attempt.failureCode, PairingFailureCodes.wifiRequired)
        XCTAssertTrue(attempt.message.contains("Cellular"))
        XCTAssertEqual(datagramClient.requestCount, 0)
    }

    func testPairingConnectorAttemptsCellularWhenCellularConnectionsAreEnabled() async throws {
        let response = ConnectResponse(
            type: "ENROLLMENT_RESULT",
            ver: 2,
            requestId: "",
            accepted: false,
            reason: "pairing-required",
            reasonMessage: "Need WebSocket handoff",
            hostId: "host-1",
            hostName: "Host",
            hostWifiName: nil,
            message: "Rejected",
            webSocketPort: nil,
            webSocketPath: nil,
            webSocketToken: nil
        )
        let responseData = try JSONEncoder.ansightEncoder.encode(response)
        let datagramClient = FakePairingDatagramClient(responseData: responseData)
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .cellular }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-cellular-enabled"),
            discoveryHint: PairingDiscoveryHint(hostAddress: "127.0.0.1", discoveryPort: 45_123)
        )
        let options = PairingConnectionOptions(allowCellularConnections: true)

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: options)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(datagramClient.requestedHosts, ["127.0.0.1"])
        XCTAssertNotEqual(attempt.failureCode, PairingFailureCodes.wifiRequired)
    }

    func testPairingHostAddressCandidatesPreferSimulatorLocalHostUnlessOverrideIsProvided() {
        let hint = PairingDiscoveryHint(hostAddresses: ["192.168.1.20", "127.0.0.1"])

        XCTAssertEqual(
            PairingHostAddressCandidates.resolve(
                discoveryHint: hint,
                hostAddressOverride: nil,
                simulatorLocalHostAddress: "127.0.0.1"
            ),
            ["127.0.0.1", "192.168.1.20"]
        )
        XCTAssertEqual(
            PairingHostAddressCandidates.resolve(
                discoveryHint: hint,
                hostAddressOverride: "10.0.0.20",
                simulatorLocalHostAddress: "127.0.0.1"
            ),
            ["10.0.0.20"]
        )
    }

    func testPairingConnectorUsesSimulatorLocalHostWhenWifiPreflightReportsNotConnected() async throws {
        let response = ConnectResponse(
            type: "ENROLLMENT_RESULT",
            ver: 2,
            requestId: "",
            accepted: false,
            reason: "pairing-required",
            reasonMessage: "Need WebSocket handoff",
            hostId: "host-1",
            hostName: "Host",
            hostWifiName: nil,
            message: "Rejected",
            webSocketPort: nil,
            webSocketPath: nil,
            webSocketToken: nil
        )
        let responseData = try JSONEncoder.ansightEncoder.encode(response)
        let datagramClient = FakePairingDatagramClient(responseData: responseData)
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .notConnected },
            simulatorLocalHostAddressProvider: { "127.0.0.1" }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-simulator-localhost"),
            discoveryHint: PairingDiscoveryHint(
                discoveryPort: 45123,
                wifiName: "Studio Wi-Fi"
            )
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(attempt.hostAddress, "127.0.0.1")
        XCTAssertEqual(datagramClient.requestedHosts, ["127.0.0.1"])
        XCTAssertNotEqual(attempt.failureCode, PairingFailureCodes.wifiRequired)
    }

    func testPairingConnectorUsesLocalEnrollmentModeForRuntimeLocalDocument() async throws {
        let response = ConnectResponse(
            type: "ENROLLMENT_RESULT",
            ver: 2,
            requestId: "",
            accepted: false,
            reason: "pairing-required",
            reasonMessage: "Need WebSocket handoff",
            hostId: "host-1",
            hostName: "Host",
            hostWifiName: nil,
            message: "Rejected",
            webSocketPort: nil,
            webSocketPath: nil,
            webSocketToken: nil
        )
        let datagramClient = FakePairingDatagramClient(
            responseData: try JSONEncoder.ansightEncoder.encode(response)
        )
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .notConnected },
            simulatorLocalHostAddressProvider: { "127.0.0.1" }
        )
        var config = TestPairingFactory.enrollmentConfig()
        config.configId = "local:\(config.appId)"

        let attempt = await connector.connect(
            document: ParsedPairingDocument(config: config),
            clientName: "Unit Test",
            options: nil
        )

        XCTAssertFalse(attempt.success)
        let requestData = try XCTUnwrap(datagramClient.requestedData.first)
        let request = try JSONDecoder().decode(ConnectRequest.self, from: requestData)
        XCTAssertEqual(request.enrollmentMode, PairingEnrollmentModes.local)
        XCTAssertEqual(datagramClient.requestedTimeouts, [1])
    }

    func testPairingConnectorUdpTimeoutMessageIncludesHostNetworkHint() async {
        let datagramClient = FakePairingDatagramClient(responseData: nil)
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .connected }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-wifi-timeout"),
            discoveryHint: PairingDiscoveryHint(
                hostAddress: "127.0.0.1",
                discoveryPort: 45123,
                wifiName: "Studio Wi-Fi"
            )
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(datagramClient.requestedTimeouts, [5])
        XCTAssertEqual(attempt.failureCode, PairingFailureCodes.udpBootstrapTimeout)
        XCTAssertTrue(attempt.message.contains("Last known Studio Wi-Fi: Studio Wi-Fi"))
        XCTAssertTrue(attempt.message.contains("Scan a fresh QR code"))
        XCTAssertEqual(datagramClient.requestCount, 1)
    }

    func testPairingConnectorTriesNextDiscoveryAddressWhenFirstTimesOut() async throws {
        let response = ConnectResponse(
            type: "ENROLLMENT_RESULT",
            ver: 2,
            requestId: "",
            accepted: false,
            reason: "pairing-required",
            reasonMessage: "Need WebSocket handoff",
            hostId: "host-1",
            hostName: "Host",
            hostWifiName: nil,
            message: "Rejected",
            webSocketPort: nil,
            webSocketPath: nil,
            webSocketToken: nil
        )
        let responseData = try JSONEncoder.ansightEncoder.encode(response)
        let datagramClient = FakePairingDatagramClient(responseProvider: { host, _ in
            host == "127.0.0.1" ? responseData : nil
        })
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .connected }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-multi-address"),
            discoveryHint: PairingDiscoveryHint(
                hostAddresses: ["192.0.2.1", "127.0.0.1"],
                discoveryPort: 45123,
                wifiName: "Studio Wi-Fi"
            )
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertFalse(attempt.success)
        XCTAssertEqual(attempt.hostAddress, "127.0.0.1")
        XCTAssertEqual(datagramClient.requestedHosts, ["192.0.2.1", "127.0.0.1"])
    }

    func testPairingConnectorBuildsWebSocketURLForIPv6Host() async throws {
        let response = ConnectResponse(
            type: "ENROLLMENT_RESULT",
            ver: 2,
            requestId: "",
            accepted: true,
            reason: "Ok",
            reasonMessage: nil,
            hostId: "host-1",
            hostName: "Host",
            hostWifiName: nil,
            message: "Accepted",
            webSocketPort: 56_598,
            webSocketPath: "/ws",
            webSocketToken: "test-token"
        )
        let datagramClient = FakePairingDatagramClient(
            responseData: try JSONEncoder.ansightEncoder.encode(response)
        )
        let connector = PairingSessionConnector(
            datagramClient: datagramClient,
            wifiStatusProvider: { .connected }
        )
        let document = ParsedPairingDocument(
            config: TestPairingFactory.enrollmentConfig(configId: "cfg-ipv6"),
            discoveryHint: PairingDiscoveryHint(
                hostAddress: "2405:6e00:c38:9be7:fdb7:47fd:7cc0:42ca",
                discoveryPort: 45_123
            )
        )

        let attempt = await connector.connect(document: document, clientName: "Unit Test", options: nil)

        XCTAssertTrue(attempt.success)
        XCTAssertEqual(
            attempt.webSocketURL?.absoluteString,
            "ws://[2405:6e00:c38:9be7:fdb7:47fd:7cc0:42ca]:56598/ws?token=test-token"
        )
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
        let config = TestPairingFactory.enrollmentConfig(configId: "cfg-document")
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

    func testAutoConnectionPrioritizesLocalStudioBeforeStoredProfiles() throws {
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

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestsForTesting(.auto())
        XCTAssertEqual(
            resolved.map(\.source),
            [.autoProbe, .autoProbe, .cachedSession, .savedConfig]
        )
        XCTAssertEqual(resolved[0].document.config.host.discoveryPort, PairingProtocolDefaults.discoveryPort)
        XCTAssertEqual(resolved[1].document.config.host.discoveryPort, PairingProtocolDefaults.developerDiscoveryPort)
        XCTAssertEqual(resolved[2].document.config.configId, "cfg-cached")
        XCTAssertEqual(resolved[3].document.config.configId, "cfg-saved")
    }

    func testAutoConnectionOrdersCachedPairingProfilesNewestFirst() throws {
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

        XCTAssertEqual(
            resolved.map { $0.document.config.configId },
            [
                "local:com.apple.dt.xctest.tool",
                "local:com.apple.dt.xctest.tool",
                "cfg-office",
                "cfg-home",
                "cfg-saved",
            ]
        )
        XCTAssertEqual(resolved[0].source, .autoProbe)
        XCTAssertEqual(resolved[1].source, .autoProbe)
        XCTAssertEqual(resolved[2].source, .cachedSession)
        XCTAssertEqual(resolved[3].source, .cachedSession)
        XCTAssertEqual(resolved[4].source, .savedConfig)
    }

    func testAutoConnectionClearsInvalidSavedRegistration() throws {
        let savedStore = MemoryPairingConfigStore()
        savedStore.save(#"{"schema":"unsupported.enrollment","invite":{}}"#)

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(
            saved: savedStore,
            cached: MemoryPairingConfigStore()
        )

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestsForTesting(.auto())

        XCTAssertEqual(
            resolved.map { $0.document.config.configId },
            [
                "local:com.apple.dt.xctest.tool",
                "local:com.apple.dt.xctest.tool",
            ]
        )
        XCTAssertEqual(
            resolved.map { $0.document.config.host.discoveryPort },
            PairingProtocolDefaults.localDiscoveryPorts
        )
        XCTAssertEqual(resolved[0].source, .autoProbe)
        XCTAssertNil(savedStore.load())
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
        XCTAssertEqual(
            resolved.map { $0.document.config.configId },
            [
                "local:com.apple.dt.xctest.tool",
                "local:com.apple.dt.xctest.tool",
                "cfg-home-new",
            ]
        )

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

    func testHostConnectionStatusListenerReceivesCurrentStatusAndConfigRefresh() throws {
        try AnsightRuntime.shared.initialize(options: AnsightOptions(
            hostAutoProbe: .disabledDefault,
            hostConnection: AnsightHostConnectionOptions(
                bundledConfigJson: #"{"schema":"test"}"#
            )
        ))
        AnsightRuntime.shared.replacePairingStoresForTesting(
            saved: MemoryPairingConfigStore(),
            cached: MemoryPairingConfigStore()
        )

        let recorder = HostConnectionStatusRecorder()
        let subscription = AnsightRuntime.shared.addHostConnectionStatusListener { status, nextCapabilities in
            recorder.record(status: status, capabilities: nextCapabilities)
        }
        defer {
            subscription.remove()
            AnsightRuntime.shared.setHostConnectionConfigReader(nil)
            AnsightRuntime.shared.deactivate()
        }

        var statuses = recorder.statuses
        var capabilities = recorder.capabilities
        XCTAssertEqual(statuses.count, 1)
        XCTAssertFalse(statuses[0].isRuntimeActive)
        XCTAssertTrue(statuses[0].hasBundledConfig)
        XCTAssertTrue(capabilities[0].canConnectUsingBundledConfig)

        try AnsightRuntime.shared.activate()

        statuses = recorder.statuses
        XCTAssertEqual(statuses.count, 2)
        XCTAssertTrue(statuses[1].isRuntimeActive)

        AnsightRuntime.shared.setHostConnectionConfigReader(
            FakeHostConnectionConfigReader(supportedKinds: [.file], payload: "{}")
        )

        statuses = recorder.statuses
        capabilities = recorder.capabilities
        XCTAssertEqual(statuses.count, 3)
        XCTAssertTrue(capabilities[2].canChooseConfigFile)

        let result = AnsightRuntime.shared.notifyHostConnectionConfigChanged()

        statuses = recorder.statuses
        XCTAssertTrue(result.success)
        XCTAssertEqual(result.source, .bundledConfig)
        XCTAssertEqual(statuses.count, 4)
        XCTAssertTrue(statuses[3].hasBundledConfig)

        subscription.remove()
        _ = AnsightRuntime.shared.notifyHostConnectionConfigChanged()

        XCTAssertEqual(recorder.statuses.count, 4)
    }

    func testExpiredCachedPairingProfileIsRemovedWithoutDisplacingLocalPriority() throws {
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

        let resolved = try AnsightRuntime.shared.resolveConnectionRequestsForTesting(.auto())
        XCTAssertEqual(
            resolved.map(\.source),
            [.autoProbe, .autoProbe, .savedConfig]
        )
        XCTAssertEqual(resolved[2].document.config.configId, "cfg-saved")
        XCTAssertNil(cachedStore.load())

        let status = AnsightRuntime.shared.hostConnectionStatus()
        XCTAssertFalse(status.hasCachedSession)
        XCTAssertTrue(status.hasSavedConfig)
        XCTAssertEqual(status.summaryKind, .disconnectedSavedConfigAvailable)
    }

    func testAutoConnectionRetriesCachedProfilesInResolvedOrder() async throws {
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

    func testAutoConnectionTriesEveryWellKnownLocalStudioPort() async throws {
        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(
            saved: MemoryPairingConfigStore(),
            cached: MemoryPairingConfigStore()
        )
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [:],
            localHostAddress: "127.0.0.1"
        )
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        let result = await AnsightRuntime.shared.connect(.auto(clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(
            connector.attemptedDiscoveryPorts,
            PairingProtocolDefaults.localDiscoveryPorts
        )
        XCTAssertEqual(result.source, .autoProbe)
    }

    func testAutoConnectionTriesLocalStudioPortsBeforeStaleSavedRegistration() async throws {
        let savedStore = MemoryPairingConfigStore()
        try savedStore.save(
            TestPairingFactory.configDocumentJSON(
                configId: "cfg-stale",
                hostAddress: "127.0.0.2"
            )
        )
        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(
            saved: savedStore,
            cached: MemoryPairingConfigStore()
        )
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-stale": .failure(
                    "Saved registration timed out.",
                    code: PairingFailureCodes.udpBootstrapTimeout
                ),
            ],
            localHostAddress: "127.0.0.1"
        )
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        let result = await AnsightRuntime.shared.connect(.auto(clientName: "Unit Test"))

        XCTAssertFalse(result.success)
        XCTAssertEqual(
            connector.attemptedConfigIds,
            [
                "local:com.apple.dt.xctest.tool",
                "local:com.apple.dt.xctest.tool",
                "cfg-stale",
            ]
        )
        XCTAssertEqual(
            connector.attemptedDiscoveryPorts,
            [
                PairingProtocolDefaults.discoveryPort,
                PairingProtocolDefaults.developerDiscoveryPort,
                PairingProtocolDefaults.discoveryPort,
            ]
        )
        XCTAssertEqual(result.source, .savedConfig)
    }

    func testAutoConnectionFallsBackFromSavedConfigToBundledConfigWhenSavedConfigIsStale() async throws {
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

    func testConcurrentConnectCallsShareInFlightConnectionAttempt() async throws {
        let pairingJson = try TestPairingFactory.configDocumentJSON(configId: "cfg-concurrent", hostAddress: "127.0.0.4")
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-concurrent": .failure("Concurrent profile failed.", code: PairingFailureCodes.udpBootstrapTimeout),
            ],
            responseDelayNanoseconds: 200_000_000
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(hostAutoProbe: .disabledDefault))
        try AnsightRuntime.shared.activate()
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: MemoryPairingConfigStore())
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        async let first = AnsightRuntime.shared.connect(.payloadText(pairingJson, clientName: "Unit Test"))
        try await Task.sleep(nanoseconds: 10_000_000)
        async let second = AnsightRuntime.shared.connect(.payloadText(pairingJson, clientName: "Unit Test"))

        let firstResult = await first
        let secondResult = await second

        XCTAssertEqual(connector.attemptedConfigIds, ["cfg-concurrent"])
        XCTAssertEqual(firstResult, secondResult)
        XCTAssertFalse(firstResult.success)
        XCTAssertEqual(firstResult.reasonCode, PairingFailureCodes.udpBootstrapTimeout)
    }

    func testForegroundRecoveryClosesStaleOpenTransportWhenRuntimeIsDisconnected() throws {
        try AnsightRuntime.shared.initialize(options: AnsightOptions(
            hostAutoProbe: AnsightHostAutoProbeOptions(
                enabled: true,
                initialDelayMilliseconds: 60_000,
                probeIntervalMilliseconds: 60_000,
                reconnectDelayMilliseconds: 60_000
            )
        ))
        try AnsightRuntime.shared.activate()

        let action = AnsightRuntime.shared.foregroundRecoveryActionForTesting(transportOpen: true)

        XCTAssertEqual(action, .closeStaleTransportAndReconnect)
    }

    func testForegroundLifecycleImmediatelyReconnectsCachedSessionWhenDisconnected() async throws {
        let cachedStore = MemoryPairingConfigStore()
        try cachedStore.save(TestPairingFactory.cachedProfileJSON(configId: "cfg-foreground", hostAddress: "127.0.0.4"))
        let connector = FakePairingSessionConnector(
            attemptsByConfigId: [
                "cfg-foreground": .failure("Foreground reconnect failed.", code: PairingFailureCodes.udpBootstrapTimeout),
            ]
        )

        try AnsightRuntime.shared.initialize(options: AnsightOptions(
            hostAutoProbe: AnsightHostAutoProbeOptions(
                enabled: true,
                initialDelayMilliseconds: 60_000,
                probeIntervalMilliseconds: 60_000,
                reconnectDelayMilliseconds: 60_000,
                clientName: "Unit Test"
            )
        ))
        AnsightRuntime.shared.replacePairingStoresForTesting(saved: MemoryPairingConfigStore(), cached: cachedStore)
        AnsightRuntime.shared.replaceConnectorForTesting(connector)
        try AnsightRuntime.shared.activate()
        defer {
            AnsightRuntime.shared.replaceConnectorForTesting(PairingSessionConnector())
        }

        AnsightRuntime.shared.setAppLifecycleState(.foreground, changedAtUtc: "2026-06-16T04:19:49.746Z")
        try await waitForCondition {
            connector.attemptedConfigIds == ["cfg-foreground"]
        }

        XCTAssertEqual(connector.attemptedConfigIds, ["cfg-foreground"])
        XCTAssertEqual(AnsightRuntime.shared.snapshot().hostConnectionStatus.connectionState, .disconnected)
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

    func testRuntimeSamplesRegisteredMetricStreams() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(
                defaultMemoryChannels: .none,
                enableFramesPerSecond: false,
                hostAutoProbe: .disabledDefault
            )
        )
        try AnsightRuntime.shared.registerMetricStream(
            AnsightMetricStream(
                channel: AnsightChannel(
                    id: 42,
                    name: "Flutter Build",
                    color: "#0A84FF",
                    unit: "microseconds",
                    type: "flutter"
                )
            ) {
                16_700
            }
        )
        try AnsightRuntime.shared.activate()

        AnsightRuntime.shared.captureBuiltInTelemetrySample()

        let metrics = AnsightRuntime.shared.recordedMetrics()
        XCTAssertEqual(metrics.count, 1)
        XCTAssertEqual(metrics.first?.channel, 42)
        XCTAssertEqual(metrics.first?.value, 16_700)
        let channel = AnsightRuntime.shared.snapshot().channels.first { $0.id == 42 }
        XCTAssertEqual(channel?.unit, "microseconds")
        XCTAssertEqual(channel?.type, "flutter")
    }

    func testRuntimeSamplesPhysicalFootprintByDefault() throws {
        try AnsightRuntime.shared.initialize(
            options: AnsightOptions(
                enableFramesPerSecond: false,
                hostAutoProbe: .disabledDefault
            )
        )
        defer {
            AnsightRuntime.shared.deactivate()
        }
        try AnsightRuntime.shared.activate()

        AnsightRuntime.shared.captureBuiltInTelemetrySample()

        let memoryMetric = AnsightRuntime.shared.recordedMetrics()
            .first { $0.channel == AnsightChannels.physicalFootprint }
        XCTAssertNotNil(memoryMetric)
        XCTAssertGreaterThan(memoryMetric?.value ?? 0, 0)
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

    func testAdaptiveScreenCaptureMaxWidthDownshiftsSlowRenders() {
        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureMaxWidth(
                configuredMaxWidth: 960,
                currentMaxWidth: 960,
                frameWidth: 960,
                renderMilliseconds: 18
            ),
            816
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureMaxWidth(
                configuredMaxWidth: nil,
                currentMaxWidth: nil,
                frameWidth: 480,
                renderMilliseconds: 18
            ),
            nil
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureMaxWidth(
                configuredMaxWidth: 960,
                currentMaxWidth: 960,
                frameWidth: 960,
                renderMilliseconds: 12
            ),
            960
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureMaxWidth(
                configuredMaxWidth: 720,
                currentMaxWidth: 720,
                frameWidth: 720,
                renderMilliseconds: 18
            ),
            720
        )
    }

    func testAdaptiveScreenCaptureIntervalBacksOffAndRecovers() {
        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureIntervalMilliseconds(
                configuredIntervalMilliseconds: 1_000,
                currentIntervalMilliseconds: 1_000,
                renderMilliseconds: 18
            ),
            1_500
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureIntervalMilliseconds(
                configuredIntervalMilliseconds: 1_000,
                currentIntervalMilliseconds: 1_500,
                renderMilliseconds: 18
            ),
            2_250
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureIntervalMilliseconds(
                configuredIntervalMilliseconds: 1_000,
                currentIntervalMilliseconds: 5_000,
                renderMilliseconds: 18
            ),
            5_000
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureIntervalMilliseconds(
                configuredIntervalMilliseconds: 1_000,
                currentIntervalMilliseconds: 2_250,
                renderMilliseconds: 10
            ),
            1_625
        )

        XCTAssertEqual(
            AnsightRuntime.adaptiveScreenCaptureIntervalMilliseconds(
                configuredIntervalMilliseconds: 1_000,
                currentIntervalMilliseconds: 1_000,
                renderMilliseconds: nil
            ),
            1_000
        )
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
        let expectedAllowBundledTools: Bool
        switch ProcessInfo.processInfo.environment["ANSIGHT_ALLOW_REMOTE_TOOLS"]?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "1", "true", "yes", "on":
            expectedAllowBundledTools = true
        default:
            expectedAllowBundledTools = false
        }

        XCTAssertTrue(report.detectedToolTypes.isEmpty)
        XCTAssertEqual(report.allowBundledTools, expectedAllowBundledTools)
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

private func waitForCondition(
    attempts: Int = 20,
    intervalNanoseconds: UInt64 = 50_000_000,
    _ condition: @escaping () -> Bool
) async throws {
    for _ in 0..<attempts {
        if condition() {
            return
        }

        try await Task.sleep(nanoseconds: intervalNanoseconds)
    }

    XCTAssertTrue(condition())
}

private final class HostConnectionStatusRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var recordedStatuses: [HostConnectionStatus] = []
    private var recordedCapabilities: [HostConnectionCapabilities] = []

    var statuses: [HostConnectionStatus] {
        lock.withLock { recordedStatuses }
    }

    var capabilities: [HostConnectionCapabilities] {
        lock.withLock { recordedCapabilities }
    }

    func record(status: HostConnectionStatus, capabilities: HostConnectionCapabilities) {
        lock.withLock {
            recordedStatuses.append(status)
            recordedCapabilities.append(capabilities)
        }
    }
}

private enum TestPairingFactory {
    static let accessToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

    static func enrollmentConfig(
        configId: String = "cfg-1",
        appId: String = "com.ansight.test"
    ) -> PairingConfig {
        let issuedAt = timestamp(Date().addingTimeInterval(-60))
        let expiresAt = timestamp(Date().addingTimeInterval(600))
        return PairingConfig(
            configId: configId,
            appId: appId,
            appName: "Ansight Tests",
            issuedAt: issuedAt,
            expiresAt: expiresAt,
            host: PairingHost(
                hostId: "host-1",
                hostName: "test-host",
                discoveryPort: 45123
            ),
            enrollment: PairingEnrollment(
                accessToken: accessToken,
                expiresAt: expiresAt,
                grantExpiresAt: timestamp(Date().addingTimeInterval(86_400))
            )
        )
    }

    static func documentJSON(
        config: PairingConfig
    ) throws -> String {
        let document = PairingConfigDocument(
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

        let data = try JSONEncoder().encode(document)
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
        let data = try JSONEncoder.ansightEncoder.encode(
            CachedPairingProfileCollectionDocument(profiles: [profile])
        )
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
            config: enrollmentConfig(configId: configId, appId: appId ?? runtimeAppId()),
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
