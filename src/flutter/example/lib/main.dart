import "package:ansight_flutter/ansight_flutter.dart";
import "package:flutter/material.dart";

void main() {
  runApp(const HarnessApp());
}

class HarnessApp extends StatefulWidget {
  const HarnessApp({super.key});

  @override
  State<HarnessApp> createState() => _HarnessAppState();
}

class _HarnessAppState extends State<HarnessApp> {
  AnsightDebugSnapshot? snapshot;

  Future<void> refresh() async {
    final nextSnapshot = await AnsightFlutter.getDebugSnapshot();
    setState(() {
      snapshot = nextSnapshot;
    });
  }

  @override
  void initState() {
    super.initState();
    refresh();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      home: Scaffold(
        appBar: AppBar(title: const Text("Ansight Flutter Harness")),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.initialize();
                await refresh();
              },
              child: const Text("Initialize"),
            ),
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.activate();
                await refresh();
              },
              child: const Text("Activate"),
            ),
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.metric(DateTime.now().millisecondsSinceEpoch % 10000, channel: 42);
                await refresh();
              },
              child: const Text("Record metric"),
            ),
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.event(
                  "flutter_harness_tapped",
                  type: AnsightEventType.navigation,
                  details: "source=flutter-harness",
                  channel: 42,
                );
                await refresh();
              },
              child: const Text("Record event"),
            ),
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.openSession(
                  '{"schema":"ansight.pairing-config.v1"}',
                  const PairingOpenOptions(
                    clientName: "Flutter Harness",
                    manualHostAddress: "127.0.0.1",
                  ),
                );
                await refresh();
              },
              child: const Text("Open harness session"),
            ),
            ElevatedButton(
              onPressed: () async {
                await AnsightFlutter.clear();
                await refresh();
              },
              child: const Text("Clear buffers"),
            ),
            const SizedBox(height: 16),
            SelectableText(snapshot == null ? "<no snapshot>" : snapshotToString(snapshot!)),
          ],
        ),
      ),
    );
  }

  String snapshotToString(AnsightDebugSnapshot snapshot) {
    return """
initialized=${snapshot.initialized}
active=${snapshot.active}
sessionOpen=${snapshot.sessionOpen}
metricsRecorded=${snapshot.metricsRecorded}
eventsRecorded=${snapshot.eventsRecorded}
registeredTools=${snapshot.registeredTools}
sessionMessage=${snapshot.sessionMessage ?? "<none>"}
lastMetric=${snapshot.lastMetric}
lastEvent=${snapshot.lastEvent}
""";
  }
}
