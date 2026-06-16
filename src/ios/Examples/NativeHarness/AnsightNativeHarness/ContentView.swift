import Ansight
import Security
import SQLite3
import SwiftUI

private enum HarnessConstants {
    static let clientName = "iOS Native Harness"
    static let preferencePrefix = "ansight.harness."
    static let secureStorageService = "ai.ansight.ios.native-harness.secure"
    static let secureStorageKey = "ansight.harness.token"
    static let customChannel = 42
}

struct ContentView: View {
    @State private var snapshot = AnsightRuntime.shared.snapshot()
    @State private var connectionMessage = ""
    @State private var hasBootstrapped = false
    @State private var isBusy = false
    @State private var metricCounter: Int64 = 0
    @State private var keyboardText = ""
    @State private var pickerValue = "Express"
    @State private var expeditedBilling = true
    @State private var quantity = 2.0
    @State private var seededAtUtc = AnsightClock.isoNow()

    private let shippingSpeeds = ["Standard", "Express", "Priority", "Overnight"]

    var body: some View {
        NavigationView {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    header

                    section("Pairing") {
                        actionGrid {
                            actionButton("Initialize", systemImage: "power") {
                                initializeTapped()
                            }
                            actionButton("Auto Connect", systemImage: "bolt.horizontal") {
                                runAsync { await connect(.auto(clientName: HarnessConstants.clientName)) }
                            }
                            actionButton("Pairing File", systemImage: "doc.badge.plus") {
                                runAsync {
                                    await connect(.file(
                                        title: "Import Ansight Pairing Config",
                                        clientName: HarnessConstants.clientName
                                    ))
                                }
                            }
                            actionButton("Scan QR", systemImage: "qrcode.viewfinder") {
                                runAsync {
                                    await connect(.qrCode(
                                        title: "Scan Ansight Pairing QR",
                                        clientName: HarnessConstants.clientName
                                    ))
                                }
                            }
                            actionButton("Disconnect", systemImage: "xmark.circle") {
                                runAsync { await disconnect() }
                            }
                            actionButton("Clear Pairing", systemImage: "trash", role: .destructive) {
                                clearPairingState()
                            }
                        }
                    }

                    section("Telemetry") {
                        actionGrid {
                            actionButton("Metric", systemImage: "waveform.path.ecg") {
                                recordMetric()
                            }
                            actionButton("Event", systemImage: "flag") {
                                recordEvent()
                            }
                            actionButton("Screen", systemImage: "rectangle.on.rectangle") {
                                recordScreen("Harness Manual Screen")
                            }
                            actionButton("Foreground", systemImage: "sun.max") {
                                setLifecycle(.foreground)
                            }
                            actionButton("Background", systemImage: "moon") {
                                setLifecycle(.background)
                            }
                            actionButton("Capture Frame", systemImage: "camera.viewfinder") {
                                runAsync { await captureScreenFrame() }
                            }
                            actionButton("Enable Touches", systemImage: "hand.tap") {
                                AnsightRuntime.shared.enableTouchCapture()
                                connectionMessage = "Touch capture enabled."
                                refresh()
                            }
                            actionButton("Disable Touches", systemImage: "hand.raised") {
                                AnsightRuntime.shared.disableTouchCapture()
                                connectionMessage = "Touch capture disabled."
                                refresh()
                            }
                            actionButton("Clear Buffers", systemImage: "eraser", role: .destructive) {
                                AnsightRuntime.shared.clear()
                                connectionMessage = "Runtime buffers cleared."
                                refresh()
                            }
                        }
                    }

                    section("Native UI") {
                        VStack(alignment: .leading, spacing: 12) {
                            TextField("Keyboard validation text", text: $keyboardText)
                                .textFieldStyle(.roundedBorder)
                                .onSubmit {
                                    recordEvent(label: "ios_harness_keyboard_submit")
                                }

                            HarnessPickerInputField(
                                title: "Shipping Speed",
                                values: shippingSpeeds,
                                selection: $pickerValue
                            )
                            .frame(height: 44)

                            Toggle("Expedited billing", isOn: $expeditedBilling)

                            VStack(alignment: .leading, spacing: 4) {
                                Text("Quantity: \(Int(quantity))")
                                    .font(.subheadline)
                                    .foregroundStyle(.secondary)
                                Slider(value: $quantity, in: 1...10, step: 1)
                            }
                        }
                    }

                    section("Seeded Data") {
                        VStack(alignment: .leading, spacing: 8) {
                            actionButton("Re-seed Harness Data", systemImage: "externaldrive.badge.plus") {
                                seedDataTapped()
                            }

                            Text(seededDataText)
                                .font(.system(.footnote, design: .monospaced))
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }

                    section("Runtime Snapshot") {
                        Text(debugText)
                            .font(.system(.footnote, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(20)
            }
            .navigationTitle("Ansight Harness")
        }
        .task {
            await bootstrap()
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Ansight iOS Native Harness")
                .font(.title2)
                .fontWeight(.semibold)

            Text(statusText)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            if isBusy {
                ProgressView()
                    .progressViewStyle(.linear)
            }
        }
    }

    private var statusText: String {
        if !connectionMessage.isEmpty {
            return connectionMessage
        }

        return snapshot.sessionMessage ?? "Ready"
    }

    private var seededDataText: String {
        """
        seededAtUtc=\(seededAtUtc)
        preferencePrefix=\(HarnessConstants.preferencePrefix)
        preferenceKeys=\(HarnessConstants.preferencePrefix)mode,\(HarnessConstants.preferencePrefix)lastSeededAtUtc,\(HarnessConstants.preferencePrefix)launchCount
        fileRoot=documents
        filePath=ansight-harness/hello.txt
        databasePath=ansight-harness/sample.sqlite
        databaseTable=harness_events
        secureStorageService=\(HarnessConstants.secureStorageService)
        secureStorageKey=\(HarnessConstants.secureStorageKey)
        """
    }

    private var debugText: String {
        """
        initialized=\(snapshot.initialized)
        active=\(snapshot.active)
        sessionOpen=\(snapshot.sessionOpen)
        metricsRecorded=\(snapshot.metricsRecorded)
        eventsRecorded=\(snapshot.eventsRecorded)
        registeredTools=\(snapshot.registeredTools)
        executableTools=\(snapshot.executableTools)
        connectionState=\(snapshot.hostConnectionStatus.connectionState.rawValue)
        connectionSummary=\(snapshot.hostConnectionStatus.summaryMessage)
        screenCaptureActive=\(snapshot.screenCaptureActive)
        screenFramesCaptured=\(snapshot.screenFramesCaptured)
        screenFramesSent=\(snapshot.screenFramesSent)
        lastScreenCaptureMessage=\(snapshot.lastScreenCaptureMessage ?? "<none>")
        frameRateCaptureActive=\(snapshot.frameRateCaptureActive)
        lastFrameRate=\(snapshot.lastFrameRate.map(String.init) ?? "<none>")
        touchCaptureEnabled=\(snapshot.touchCaptureEnabled)
        touchCaptureActive=\(snapshot.touchCaptureActive)
        touchCaptureStreamingActive=\(snapshot.touchCaptureStreamingActive)
        touchesCaptured=\(snapshot.touchesCaptured)
        touchesSent=\(snapshot.touchesSent)
        lastTouchCaptureMessage=\(snapshot.lastTouchCaptureMessage ?? "<none>")
        lifecycleState=\(snapshot.lifecycleState.rawValue)
        currentScreen=\(snapshot.currentScreen?.name ?? "<none>")
        toolDiscoveryEnabled=\(snapshot.toolDiscoveryEnabled)
        toolExecutionEnabled=\(snapshot.toolExecutionEnabled)
        embeddedDeveloperPairingAvailable=\(snapshot.embeddedDeveloperPairingAvailable)
        bundledHarnessPairingAvailable=\(HarnessBundledPairingConfig.json != nil)
        bundledHarnessPairingSource=\(HarnessBundledPairingConfig.sourceDescription)
        bundledHarnessPairingHasHostDiscovery=\(HarnessBundledPairingConfig.hasHostDiscovery)
        detectedBundledTools=\(snapshot.detectedBundledTools.joined(separator: ","))
        lastPairingConfigId=\(snapshot.lastPairingConfigId ?? "<none>")
        resolvedHostAddress=\(snapshot.resolvedHostAddress ?? "<none>")
        sessionMessage=\(snapshot.sessionMessage ?? "<none>")
        lastMetric=\(String(describing: snapshot.lastMetric))
        lastEvent=\(String(describing: snapshot.lastEvent))
        keyboardText=\(keyboardText.isEmpty ? "<empty>" : keyboardText)
        pickerValue=\(pickerValue)
        expeditedBilling=\(expeditedBilling)
        quantity=\(Int(quantity))
        """
    }

    @MainActor
    private func bootstrap() async {
        guard !hasBootstrapped else {
            return
        }

        hasBootstrapped = true
        do {
            try initializeHarness()
            recordScreen("Harness Home")
            setLifecycle(.foreground)
            recordMetric()

            if HarnessBundledPairingConfig.hasHostDiscovery {
                let result = await AnsightRuntime.shared.connect(.bundledConfig(clientName: HarnessConstants.clientName))
                connectionMessage = result.message
            } else if HarnessBundledPairingConfig.json != nil {
                connectionMessage = "Initialized with bundled public config. Scan QR or import a config with host discovery to connect."
            } else {
                connectionMessage = "Initialized. Use Auto, Pairing File, or Scan QR to connect to Studio."
            }
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func initializeTapped() {
        do {
            try initializeHarness()
            connectionMessage = "SDK initialized and remote tools registered."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func initializeHarness() throws {
        if AnsightRuntime.shared.snapshot().initialized {
            return
        }

        try seedHarnessData()
        AnsightRuntime.shared.setScreenRouteResolver(AnsightScreenRouteResolver { context in
            guard context.swiftUIRootTypeName?.contains("ContentView") == true
                || context.title?.contains("Harness") == true
            else {
                return nil
            }

            return AnsightScreenRoute(
                name: "iOS Native Harness",
                key: "ios-native-harness",
                details: [
                    "route": "/ios/native-harness",
                    "source": context.source,
                ]
            )
        })

        try AnsightRuntime.shared.initializeAndActivateAnsightSdk(
            options: harnessOptions,
            remoteToolOptions: harnessRemoteToolOptions
        )
    }

    private var harnessOptions: AnsightOptions {
        AnsightOptions(
            sampleFrequencyMilliseconds: 400,
            retentionPeriodSeconds: 120,
            additionalChannels: [
                AnsightChannel(id: HarnessConstants.customChannel, name: "Harness Custom", color: "#0A84FF"),
            ],
            enableFramesPerSecond: true,
            enableBatteryLevel: false,
            lifecycleCapture: .enabledDefault,
            sessionJpegCapture: AnsightSessionJpegCaptureOptions(
                intervalMilliseconds: 1_000,
                quality: 70,
                maxWidth: 960
            ),
            touchCapture: AnsightTouchCaptureOptions(),
            toolGuard: .fullAccess,
            customProperties: [
                "harness": [
                    "name": "ios-native",
                    "purpose": "sdk-validation",
                ],
            ],
            hostAutoProbe: .disabledDefault,
            hostConnection: AnsightHostConnectionOptions(
                bundledConfigJson: HarnessBundledPairingConfig.json
            )
        )
    }

    private var harnessRemoteToolOptions: AnsightRemoteToolOptions {
        var fileRoots: [AnsightFileSystemRoot] = []
        var databaseRoots: [AnsightDatabaseRoot] = []
        if let harnessDirectory = harnessDirectoryURL() {
            fileRoots.append(AnsightFileSystemRoot(alias: "harness", path: harnessDirectory.path))
            databaseRoots.append(AnsightDatabaseRoot(alias: "harness", path: harnessDirectory.path))
        }

        return AnsightRemoteToolOptions(
            database: AnsightDatabaseToolsOptions(additionalRoots: databaseRoots),
            fileSystem: AnsightFileSystemToolsOptions(additionalRoots: fileRoots),
            preferences: AnsightPreferencesToolOptions(
                allowedKeyPrefixes: [HarnessConstants.preferencePrefix]
            ),
            secureStorage: AnsightSecureStorageToolsOptions(
                appleService: HarnessConstants.secureStorageService,
                allowedKeyPrefixes: [HarnessConstants.preferencePrefix]
            )
        )
    }

    private func connect(_ request: HostConnectionRequest) async {
        let result = await AnsightRuntime.shared.connect(request)
        connectionMessage = result.message
        refresh()
    }

    private func disconnect() async {
        let result = await AnsightRuntime.shared.disconnect()
        connectionMessage = result.message
        refresh()
    }

    private func captureScreenFrame() async {
        let result = await AnsightRuntime.shared.captureScreenFrame()
        connectionMessage = result.message
        refresh()
    }

    private func clearPairingState() {
        AnsightRuntime.shared.clearSavedPairing()
        AnsightRuntime.shared.clearCachedSession()
        connectionMessage = "Saved pairing config and cached pairing session cleared."
        refresh()
    }

    private func recordMetric() {
        metricCounter += 1
        let value = Int64(Date().timeIntervalSince1970 * 1000) + metricCounter
        do {
            try AnsightRuntime.shared.metric(value, channel: HarnessConstants.customChannel)
            connectionMessage = "Recorded harness metric \(value)."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func recordEvent(label: String = "ios_harness_tapped") {
        do {
            try AnsightRuntime.shared.event(
                label,
                type: .navigation,
                details: "source=native-harness;picker=\(pickerValue);keyboard=\(keyboardText)",
                channel: HarnessConstants.customChannel
            )
            connectionMessage = "Recorded harness event."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func recordScreen(_ name: String) {
        do {
            try AnsightRuntime.shared.screenViewed(
                name,
                details: [
                    "route": "/ios/native-harness",
                    "picker": pickerValue,
                    "quantity": String(Int(quantity)),
                ]
            )
            connectionMessage = "Recorded screen \(name)."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func setLifecycle(_ state: AppLifecycleState) {
        AnsightRuntime.shared.setAppLifecycleState(state)
        connectionMessage = "Lifecycle state set to \(state.rawValue)."
        refresh()
    }

    private func seedDataTapped() {
        do {
            try seedHarnessData()
            connectionMessage = "Harness data re-seeded."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    private func seedHarnessData() throws {
        seededAtUtc = AnsightClock.isoNow()
        try prepareHarnessFileSystemSample()
        try prepareHarnessDatabaseSample()
        try prepareHarnessSecureStorageSample()

        let defaults = UserDefaults.standard
        defaults.set("native-harness", forKey: "\(HarnessConstants.preferencePrefix)mode")
        defaults.set(seededAtUtc, forKey: "\(HarnessConstants.preferencePrefix)lastSeededAtUtc")
        defaults.set(defaults.integer(forKey: "\(HarnessConstants.preferencePrefix)launchCount") + 1, forKey: "\(HarnessConstants.preferencePrefix)launchCount")
    }

    private func prepareHarnessFileSystemSample() throws {
        guard let directory = harnessDirectoryURL() else {
            throw harnessError("Unable to resolve the app Documents directory.")
        }

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent("hello.txt")
        let contents = """
        Ansight Native Harness file-system sample.
        Seeded at \(seededAtUtc).
        Use this file to validate iOS SDK file tools from Ansight Studio.
        """
        try Data(contents.utf8).write(to: file, options: [.atomic])
    }

    private func prepareHarnessDatabaseSample() throws {
        guard let directory = harnessDirectoryURL() else {
            throw harnessError("Unable to resolve the app Documents directory.")
        }

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let database = directory.appendingPathComponent("sample.sqlite")

        var handle: OpaquePointer?
        let openResult = sqlite3_open_v2(
            database.path,
            &handle,
            SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX,
            nil
        )
        guard openResult == SQLITE_OK, let handle else {
            throw harnessError("Unable to create harness SQLite sample: \(sqliteError(handle))")
        }
        defer {
            sqlite3_close_v2(handle)
        }

        try executeSQLite(handle, """
        CREATE TABLE IF NOT EXISTS harness_events (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            count INTEGER NOT NULL,
            recorded_at TEXT NOT NULL,
            payload BLOB
        );
        """)
        try executeSQLite(handle, "DELETE FROM harness_events;")
        try executeSQLite(handle, """
        INSERT INTO harness_events (name, count, recorded_at, payload)
        VALUES
            ('startup', 1, '\(seededAtUtc)', X'000102FF'),
            ('screen_capture', 2, '\(seededAtUtc)', NULL),
            ('touch_capture', 3, '\(seededAtUtc)', NULL),
            ('picker_overlay', 4, '\(seededAtUtc)', NULL);
        """)
    }

    private func prepareHarnessSecureStorageSample() throws {
        let data = Data("native-harness-secret-\(seededAtUtc)".utf8)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: HarnessConstants.secureStorageService,
            kSecAttrAccount as String: HarnessConstants.secureStorageKey,
        ]

        SecItemDelete(query as CFDictionary)

        var item = query
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw harnessError("Unable to seed harness secure storage: OSStatus \(status).")
        }
    }

    private func executeSQLite(_ handle: OpaquePointer, _ sql: String) throws {
        var errorPointer: UnsafeMutablePointer<CChar>?
        let result = sqlite3_exec(handle, sql, nil, nil, &errorPointer)
        if result != SQLITE_OK {
            let message = errorPointer.map { String(cString: $0) } ?? sqliteError(handle)
            if let errorPointer {
                sqlite3_free(errorPointer)
            }

            throw harnessError("Harness SQLite statement failed: \(message)")
        }
    }

    private func sqliteError(_ handle: OpaquePointer?) -> String {
        guard let handle, let pointer = sqlite3_errmsg(handle) else {
            return "unknown SQLite error"
        }

        return String(cString: pointer)
    }

    private func harnessDirectoryURL() -> URL? {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)
            .first?
            .appendingPathComponent("ansight-harness", isDirectory: true)
    }

    private func harnessError(_ message: String) -> NSError {
        NSError(
            domain: "AnsightNativeHarness",
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: message]
        )
    }

    private func refresh() {
        snapshot = AnsightRuntime.shared.snapshot()
    }

    private func runAsync(_ operation: @escaping () async -> Void) {
        isBusy = true
        Task {
            await operation()
            await MainActor.run {
                isBusy = false
                refresh()
            }
        }
    }

    @ViewBuilder
    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title)
                .font(.headline)
            content()
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
    }

    @ViewBuilder
    private func actionGrid<Content: View>(@ViewBuilder content: () -> Content) -> some View {
        LazyVGrid(
            columns: [GridItem(.adaptive(minimum: 145), spacing: 10, alignment: .top)],
            alignment: .leading,
            spacing: 10,
            content: content
        )
    }

    private func actionButton(
        _ title: String,
        systemImage: String,
        role: ButtonRole? = nil,
        action: @escaping () -> Void
    ) -> some View {
        Button(role: role, action: action) {
            Label(title, systemImage: systemImage)
                .frame(maxWidth: .infinity, minHeight: 34)
        }
        .buttonStyle(.bordered)
        .disabled(isBusy)
    }
}
