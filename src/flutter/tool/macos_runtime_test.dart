import 'package:ansight_flutter/ansight.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('initializes and installs Flutter instrumentation on macOS', (
    WidgetTester tester,
  ) async {
    final snapshot = await Ansight.instance.initializeAndActivate(
      AnsightOptions.developer(
        clientName: 'Ansight Flutter macOS validation',
        toolGuard: AnsightToolGuard.readOnly,
      ),
    );

    expect(snapshot.initialized, isTrue);
    expect(snapshot.active, isTrue);

    await AnsightFlutterInstrumentation.instance.install();
    expect(AnsightFlutterInstrumentation.instance.isInstalled, isTrue);

    final status = await Ansight.instance.status();
    expect(status.initialized, isTrue);
    expect(status.active, isTrue);
    expect(status.registeredTools, greaterThan(0));

    AnsightFlutterInstrumentation.instance.uninstall();
    await Ansight.instance.deactivate();
  });
}
