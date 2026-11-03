import Ansight
import Foundation

extension HarnessViewModel {
    func bootstrap() async {
        guard !hasBootstrapped else {
            return
        }

        hasBootstrapped = true
        do {
            try initializeHarness()
            recordScreen("Harness Dashboard")
            setLifecycle(.foreground)
            recordMetric()

            if offlineUploadEnabled {
                try configureOfflineUploadHarness()
                connectionMessage = "Offline capture upload harness. Host connection disabled."
            } else {
                let result = await AnsightRuntime.shared.connect(.auto(clientName: HarnessConstants.clientName))
                connectionMessage = result.success
                    ? result.message
                    : "Initialized. Waiting for a running local host; physical devices enroll by QR once."
            }
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func initializeTapped() {
        do {
            try initializeHarness()
            connectionMessage = "SDK initialized, remote tools registered, and harness roots exposed."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func initializeHarness() throws {
        if AnsightRuntime.shared.snapshot().initialized {
            try registerHarnessToolsIfNeeded()
            return
        }

        try seedHarnessData()
        AnsightRuntime.shared.setScreenRouteResolver(AnsightScreenRouteResolver { context in
            guard context.swiftUIRootTypeName?.contains("Harness") == true
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
        try registerHarnessToolsIfNeeded()
    }

    func registerHarnessToolsIfNeeded() throws {
        guard !customToolsRegistered else {
            return
        }

        syncInspectionState()
        try AnsightRuntime.shared.registerTool(HarnessStateSnapshotTool(store: inspectionStore))
        try AnsightRuntime.shared.registerTool(HarnessListReflectionRootsTool(store: inspectionStore))
        try AnsightRuntime.shared.registerTool(HarnessInspectReflectionRootTool(store: inspectionStore))
        customToolsRegistered = true
    }

    var harnessOptions: AnsightOptions {
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
                intervalMilliseconds: 700,
                quality: 70,
                maxWidth: 960,
                mode: offlineUploadEnabled ? .screenshotAndVisualTree : .screenshotOnly
            ),
            touchCapture: AnsightTouchCaptureOptions(),
            toolGuard: .fullAccess,
            customProperties: [
                "harness": [
                    "name": "ios-native",
                    "purpose": "sdk-validation",
                    "features": "tabs,modals,flyout,push-pop,3d,database,custom-tools",
                ],
            ],
            hostAutoProbe: AnsightHostAutoProbeOptions(
                enabled: !offlineUploadEnabled,
                initialDelayMilliseconds: 1_000,
                probeIntervalMilliseconds: 5_000,
                reconnectDelayMilliseconds: 10_000,
                clientName: HarnessConstants.clientName
            ),
            offlineCapture: offlineUploadEnabled ? OfflineCaptureOptions(
                enabled: true, transferMode: .manual, maximumSessions: 512,
                maximumSessionBytes: 4 * 1024 * 1024, chunkDurationSeconds: 10
            ) : nil
        )
    }

    var harnessRemoteToolOptions: AnsightRemoteToolOptions {
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
}
