import AnsightKit
import SwiftUI

struct ContentView: View {
    @State private var snapshot = AnsightRuntime.shared.snapshot()

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Ansight iOS Harness")
                        .font(.title2)
                        .fontWeight(.semibold)

                    Button("Initialize runtime") {
                        AnsightRuntime.shared.initialize()
                        refresh()
                    }

                    Button("Activate runtime") {
                        try? AnsightRuntime.shared.activate()
                        refresh()
                    }

                    Button("Record metric") {
                        try? AnsightRuntime.shared.metric(Int64(Date().timeIntervalSince1970 * 1000).truncatingRemainder(dividingBy: 10_000), channel: 42)
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

                    Button("Open harness session") {
                        _ = try? AnsightRuntime.shared.openSession(
                            pairingJson: #"{"schema":"ansight.pairing-config.v1"}"#,
                            options: PairingOpenOptions(
                                clientName: "iOS Harness",
                                manualHostAddress: "127.0.0.1"
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
    }

    private var debugText: String {
        """
        initialized=\(snapshot.initialized)
        active=\(snapshot.active)
        sessionOpen=\(snapshot.sessionOpen)
        metricsRecorded=\(snapshot.metricsRecorded)
        eventsRecorded=\(snapshot.eventsRecorded)
        registeredTools=\(snapshot.registeredTools)
        sessionMessage=\(snapshot.sessionMessage ?? "<none>")
        lastMetric=\(String(describing: snapshot.lastMetric))
        lastEvent=\(String(describing: snapshot.lastEvent))
        """
    }

    private func refresh() {
        snapshot = AnsightRuntime.shared.snapshot()
    }
}
