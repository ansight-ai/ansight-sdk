import AnsightCore
import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsSecureStorage
import AnsightToolsVisualTree
import SQLite3
import SwiftUI

struct ContentView: View {
    @State private var snapshot = AnsightRuntime.shared.snapshot()
    @State private var connectionMessage = ""
    @State private var hasBootstrapped = false
    @State private var hasRegisteredFileSystemTools = false
    @State private var hasRegisteredDatabaseTools = false
    @State private var hasRegisteredPreferencesTools = false
    @State private var hasRegisteredSecureStorageTools = false
    @State private var hasRegisteredVisualTreeTools = false

    var body: some View {
        NavigationView {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Ansight iOS Harness")
                        .font(.title2)
                        .fontWeight(.semibold)

                    Button("Initialize runtime") {
                        try? AnsightRuntime.shared.initialize()
                        refresh()
                    }

                    Button("Initialize and activate") {
                        try? AnsightRuntime.shared.initializeAndActivate(
                            options: AnsightOptions(
                                additionalChannels: [
                                    AnsightChannel(id: 42, name: "Harness Custom", color: "#0A84FF"),
                                ],
                                sessionJpegCapture: AnsightSessionJpegCaptureOptions(
                                    intervalMilliseconds: 1_000,
                                    quality: 70,
                                    maxWidth: 960
                                ),
                                toolGuard: .fullAccess
                            )
                        )
                        try? registerHarnessTools()
                        refresh()
                    }

                    Button("Record metric") {
                        let value = Int64(Date().timeIntervalSince1970 * 1000) % 10_000
                        try? AnsightRuntime.shared.metric(value, channel: 42)
                        refresh()
                    }

                    Button("Record event") {
                        try? AnsightRuntime.shared.event(
                            "ios_harness_tapped",
                            type: .navigation,
                            details: "source=native-harness",
                            channel: 42
                        )
                        refresh()
                    }

                    Button("Record screen") {
                        try? AnsightRuntime.shared.screenViewed("Harness", details: ["route": "/harness"])
                        refresh()
                    }

                    Button("Set foreground") {
                        AnsightRuntime.shared.setAppLifecycleState(.foreground)
                        refresh()
                    }

                    Button("Connect live session") {
                        Task {
                            let result = await AnsightRuntime.shared.connect(.auto(clientName: "iOS Native Harness"))
                            connectionMessage = result.message
                            refresh()
                        }
                    }

                    Button("Capture screen frame") {
                        Task {
                            let result = await AnsightRuntime.shared.captureScreenFrame()
                            connectionMessage = result.message
                            refresh()
                        }
                    }

                    Button("Disconnect live session") {
                        Task {
                            let result = await AnsightRuntime.shared.disconnect()
                            connectionMessage = result.message
                            refresh()
                        }
                    }

                    Button("Open harness session") {
                        _ = try? AnsightRuntime.shared.openSession(
                            pairingJson: "",
                            options: PairingOpenOptions(
                                clientName: "iOS Harness"
                            )
                        )
                        refresh()
                    }

                    Button("Clear buffers") {
                        AnsightRuntime.shared.clear()
                        refresh()
                    }

                    Divider()

                    Text(debugText)
                        .font(.system(.body, design: .monospaced))
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .padding(20)
            }
            .navigationTitle("Harness")
        }
        .task {
            await bootstrap()
        }
    }

    @MainActor
    private func bootstrap() async {
        guard !hasBootstrapped else {
            return
        }

        hasBootstrapped = true
        do {
            try AnsightRuntime.shared.initializeAndActivate(
                        options: AnsightOptions(
                            additionalChannels: [
                                AnsightChannel(id: 42, name: "Harness Custom", color: "#0A84FF"),
                            ],
                            sessionJpegCapture: AnsightSessionJpegCaptureOptions(
                                intervalMilliseconds: 1_000,
                                quality: 70,
                                maxWidth: 960
                            ),
                            toolGuard: .fullAccess,
                            hostAutoProbe: .disabledDefault
                        )
            )
            try registerHarnessTools()
            try AnsightRuntime.shared.screenViewed("Harness", details: ["route": "/harness"])
            AnsightRuntime.shared.setAppLifecycleState(.foreground)
            try AnsightRuntime.shared.metric(Int64(Date().timeIntervalSince1970 * 1000) % 10_000, channel: 42)

            let result = await AnsightRuntime.shared.connect(.auto(clientName: "iOS Native Harness"))
            connectionMessage = result.message
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
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
        connectionMessage=\(connectionMessage.isEmpty ? "<none>" : connectionMessage)
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
        databaseToolsRegistered=\(hasRegisteredDatabaseTools)
        lifecycleState=\(snapshot.lifecycleState.rawValue)
        currentScreen=\(snapshot.currentScreen?.name ?? "<none>")
        toolDiscoveryEnabled=\(snapshot.toolDiscoveryEnabled)
        toolExecutionEnabled=\(snapshot.toolExecutionEnabled)
        embeddedDeveloperPairingAvailable=\(snapshot.embeddedDeveloperPairingAvailable)
        detectedBundledTools=\(snapshot.detectedBundledTools.joined(separator: ","))
        lastPairingConfigId=\(snapshot.lastPairingConfigId ?? "<none>")
        resolvedHostAddress=\(snapshot.resolvedHostAddress ?? "<none>")
        sessionMessage=\(snapshot.sessionMessage ?? "<none>")
        lastMetric=\(String(describing: snapshot.lastMetric))
        lastEvent=\(String(describing: snapshot.lastEvent))
        """
    }

    private func refresh() {
        snapshot = AnsightRuntime.shared.snapshot()
    }

    private func registerHarnessTools() throws {
        if !hasRegisteredPreferencesTools {
            UserDefaults.standard.set("native-harness", forKey: "ansight.harness.mode")
            UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: "ansight.harness.startedAt")
            try AnsightRuntime.shared.registerPreferencesTools(
                options: AnsightPreferencesToolOptions(
                    allowedKeyPrefixes: ["ansight.harness."]
                )
            )
            hasRegisteredPreferencesTools = true
        }

        if !hasRegisteredFileSystemTools {
            try prepareHarnessFileSystemSample()
            try AnsightRuntime.shared.registerFileSystemTools()
            hasRegisteredFileSystemTools = true
        }

        if !hasRegisteredDatabaseTools {
            try prepareHarnessDatabaseSample()
            try AnsightRuntime.shared.registerDatabaseTools()
            hasRegisteredDatabaseTools = true
        }

        if !hasRegisteredSecureStorageTools {
            try AnsightRuntime.shared.registerSecureStorageTools(
                options: AnsightSecureStorageToolsOptions(
                    allowedKeyPrefixes: ["ansight.harness."]
                )
            )
            hasRegisteredSecureStorageTools = true
        }

        if !hasRegisteredVisualTreeTools {
            try AnsightRuntime.shared.registerVisualTreeTools()
            hasRegisteredVisualTreeTools = true
        }
    }

    private func prepareHarnessFileSystemSample() throws {
        guard let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first else {
            return
        }

        let directory = documents.appendingPathComponent("ansight-harness", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent("hello.txt")
        let contents = """
        Ansight NativeHarness file-system sample.
        This file is written at startup so Studio can validate iOS SDK file tools.
        """
        try Data(contents.utf8).write(to: file, options: [.atomic])
    }

    private func prepareHarnessDatabaseSample() throws {
        guard let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first else {
            return
        }

        let directory = documents.appendingPathComponent("ansight-harness", isDirectory: true)
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
            ('startup', 1, '2026-06-14T00:00:00Z', X'000102FF'),
            ('screen_capture', 2, '2026-06-14T00:01:00Z', NULL),
            ('touch_capture', 3, '2026-06-14T00:02:00Z', NULL);
        """)
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

    private func harnessError(_ message: String) -> NSError {
        NSError(
            domain: "AnsightNativeHarness",
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: message]
        )
    }
}
