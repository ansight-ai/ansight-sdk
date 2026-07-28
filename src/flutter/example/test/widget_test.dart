import 'package:ansight_example/main.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders every harness feature section', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      const AnsightHarnessApp(
        autoInitialize: false,
        enableSceneAnimation: false,
      ),
    );
    await tester.pump();

    expect(find.text('Ansight Flutter Harness'), findsOneWidget);
    expect(find.byKey(const Key('fixture-dashboard')), findsOneWidget);
    expect(find.byKey(const Key('rendered-scene')), findsOneWidget);
    for (final title in <String>[
      'Runtime',
      'Telemetry',
      'Visual evidence and input',
      'Host pairing and sessions',
      'Properties, tools, and artifacts',
      'Harness controls',
      'Activity',
    ]) {
      await tester.scrollUntilVisible(
        find.text(title),
        400,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text(title), findsOneWidget);
    }
  });

  testWidgets('drives navigation, dialog, bottom sheet, and drawer fixtures', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      const AnsightHarnessApp(
        autoInitialize: false,
        enableSceneAnimation: false,
      ),
    );
    await tester.pump();

    await tester.tap(find.byKey(const Key('open-pairing-dialog')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('pairing-dialog')), findsOneWidget);
    expect(find.byKey(const Key('pairing-dialog-scan-qr')), findsOneWidget);
    await tester.tap(find.byKey(const Key('pairing-dialog-cancel')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('fixture-tab-navigation')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('nav-push')));
    await tester.pump();
    expect(find.textContaining('2. Details'), findsOneWidget);

    await tester.tap(find.byKey(const Key('nav-dialog')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('harness-dialog')), findsOneWidget);
    await tester.tap(find.byKey(const Key('dialog-dismiss')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('nav-bottom-sheet')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('harness-bottom-sheet')), findsOneWidget);
    await tester.tap(find.byKey(const Key('bottom-sheet-push')));
    await tester.pumpAndSettle();
    expect(find.textContaining('3. Bottom Sheet'), findsOneWidget);

    await tester.tap(find.byTooltip('Open navigation menu'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('harness-drawer')), findsOneWidget);
    await tester.tap(find.byKey(const Key('drawer-tab-tools')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('custom-tool-list')), findsOneWidget);
  });
}
