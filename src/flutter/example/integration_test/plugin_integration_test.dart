import 'package:ansight_flutter/ansight.dart';
import 'package:ansight_example/harness_fixtures.dart';
import 'package:ansight_example/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('Flutter harness works through the native bridge end to end', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      AnsightFlutterCaptureBoundary(
        controller: ansightHarnessCaptureController,
        automaticCaptureOptions: null,
        child: const AnsightHarnessApp(enableSceneAnimation: false),
      ),
    );
    await tester.pump();

    AnsightDebugSnapshot? initial;
    for (var attempt = 0; attempt < 80; attempt += 1) {
      await tester.pump(const Duration(milliseconds: 100));
      try {
        final candidate = await Ansight.instance.snapshot();
        if (candidate.initialized && candidate.active) {
          initial = candidate;
          break;
        }
      } on Object {
        // The first native initialization can still be crossing the bridge.
      }
    }
    expect(initial, isNotNull, reason: 'Harness did not initialize in time.');
    expect(initial!.initialized, isTrue);
    expect(initial.active, isTrue);
    for (var attempt = 0; attempt < 80; attempt += 1) {
      if (Ansight.instance.registeredToolIds.contains(
        'harness.database_summary',
      )) {
        break;
      }
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(
      Ansight.instance.registeredToolIds,
      contains('harness.database_summary'),
      reason: 'Harness fixtures and custom tools did not finish initializing.',
    );

    AnsightHostConnectionStatus? connection;
    await Future<void>.delayed(const Duration(seconds: 12));
    await tester.pump();
    connection = await Ansight.instance.hostConnectionStatus();
    expect(
      connection.isConnected,
      isTrue,
      reason: 'Harness did not connect to the Ansight host in time.',
    );

    await tester.scrollUntilVisible(
      find.byKey(const Key('run-e2e-scenario')),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    for (var attempt = 0; attempt < 80; attempt += 1) {
      final button = tester.widget<FilledButton>(
        find.byKey(const Key('run-e2e-scenario')),
      );
      if (button.onPressed != null) {
        break;
      }
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('run-e2e-scenario')))
          .onPressed,
      isNotNull,
    );
    await tester.tap(find.byKey(const Key('run-e2e-scenario')));
    await tester.pumpAndSettle(const Duration(milliseconds: 100));

    final updated = await Ansight.instance.snapshot();
    expect(updated.metricsRecorded, greaterThan(initial.metricsRecorded));
    expect(updated.eventsRecorded, greaterThan(initial.eventsRecorded));
    expect(updated.active, isTrue);
    expect(
      updated.channels.map((AnsightChannel channel) => channel.id),
      contains(42),
    );
    expect(
      Ansight.instance.registeredToolIds,
      containsAll(<String>[
        'flutter.get_widget_tree',
        'flutter.inspect_widget',
        'flutter.find_widgets',
        'harness.echo',
        'harness.inspect_state',
        'harness.advance_state',
        'harness.database_summary',
        'harness.capture_builtin',
      ]),
    );
    expect(Ansight.instance.registeredArtifactProviderIds, contains('harness'));

    await Ansight.instance.enableTouchCapture();
    final beforeTouch = await Ansight.instance.snapshot();
    await tester.tapAt(const Offset(400, 100));
    await tester.pump(const Duration(milliseconds: 300));
    final afterTouch = await Ansight.instance.snapshot();
    expect(
      afterTouch.touchesCaptured,
      greaterThan(beforeTouch.touchesCaptured ?? 0),
      reason: 'Flutter pointer input did not reach native touch capture.',
    );

    final store = HarnessFixtureStore();
    await store.initialize();
    final database = await store.summary();
    expect(database.itemCount, greaterThanOrEqualTo(5));
    expect(database.eventCount, greaterThanOrEqualTo(2));
    expect(database.path, endsWith(HarnessFixtureStore.databaseName));
    expect(database.fixtureFilePath, isNotEmpty);

    await tester.drag(
      find.byKey(const Key('harness-scroll')),
      const Offset(0, 1200),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('database-summary')), findsOneWidget);
    await tester.tap(find.byKey(const Key('fixture-tab-navigation')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('nav-push')));
    await tester.pump();
    expect(find.textContaining('Details'), findsOneWidget);
    await tester.tap(find.byKey(const Key('nav-dialog')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('harness-dialog')), findsOneWidget);
    await tester.tap(find.byKey(const Key('dialog-dismiss')));
    await tester.pumpAndSettle();

    final deactivated = await Ansight.instance.deactivate();
    expect(deactivated.active, isFalse);
    final reactivated = await Ansight.instance.activate();
    expect(reactivated.active, isTrue);
    for (var attempt = 0; attempt < 120; attempt += 1) {
      connection = await Ansight.instance.hostConnectionStatus();
      if (connection.isConnected) {
        break;
      }
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(
      connection?.isConnected,
      isTrue,
      reason: 'Harness did not reconnect after runtime reactivation.',
    );
    await Ansight.instance.removeCustomProperty('harness', 'scenario');
    await Ansight.instance.clearSessionProperties();

    final metrics = await Ansight.instance.recordedMetrics(limit: 100);
    final events = await Ansight.instance.recordedEvents(limit: 100);
    expect(metrics, isNotEmpty);
    expect(
      events.map((AnsightRecordedEvent event) => event.type),
      contains(AnsightEventType.navigation.wireName),
    );
    AnsightOperationResult capture = const AnsightOperationResult(
      success: false,
      message: 'Flutter capture has not run.',
    );
    for (var attempt = 0; attempt < 5; attempt += 1) {
      await tester.pump();
      capture = await ansightHarnessCaptureController.capture(
        includeVisualTree: true,
      );
      if (capture.success) {
        break;
      }
    }
    expect(capture.success, isTrue, reason: capture.message);
    await Ansight.instance.sendClientLog(
      'Flutter macOS harness integration test completed',
    );
    for (var frame = 0; frame < 90; frame += 1) {
      await tester.pump(const Duration(milliseconds: 16));
    }
  });
}
