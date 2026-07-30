import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/material.dart';

import 'harness_fixtures.dart';

const AnsightChannel _harnessMetricChannel = AnsightChannel(
  id: 42,
  name: 'Flutter harness operations',
  unit: 'count',
  type: 'counter',
  colorHex: '#7557FF',
  source: 'flutter',
  group: 'harness',
  kind: 'scenario',
);

const String _harnessEnrollmentInviteBase64 = String.fromEnvironment(
  'ANSIGHT_ENROLLMENT_INVITE_BASE64',
);

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const AnsightHarnessApp());
}

class AnsightHarnessApp extends StatelessWidget {
  const AnsightHarnessApp({
    super.key,
    this.autoInitialize = true,
    this.enableSceneAnimation = true,
  });

  final bool autoInitialize;
  final bool enableSceneAnimation;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Ansight Flutter Harness',
    debugShowCheckedModeBanner: false,
    navigatorObservers: <NavigatorObserver>[AnsightNavigatorObserver()],
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(
        seedColor: const Color(0xff7557ff),
        brightness: Brightness.dark,
      ),
      scaffoldBackgroundColor: const Color(0xff101116),
      cardTheme: const CardThemeData(
        color: Color(0xff191b23),
        margin: EdgeInsets.zero,
      ),
      useMaterial3: true,
    ),
    home: HarnessHome(
      autoInitialize: autoInitialize,
      enableSceneAnimation: enableSceneAnimation,
    ),
  );
}

class HarnessHome extends StatefulWidget {
  const HarnessHome({
    super.key,
    required this.autoInitialize,
    required this.enableSceneAnimation,
  });

  final bool autoInitialize;
  final bool enableSceneAnimation;

  @override
  State<HarnessHome> createState() => _HarnessHomeState();
}

class _HarnessHomeState extends State<HarnessHome>
    with SingleTickerProviderStateMixin {
  final Ansight _ansight = Ansight.instance;
  final HarnessFixtureStore _fixtureStore = HarnessFixtureStore();
  final HarnessState _harnessState = HarnessState();
  final TextEditingController _pairingController = TextEditingController();
  final List<String> _activity = <String>[];
  final List<StreamSubscription<Object?>> _subscriptions =
      <StreamSubscription<Object?>>[];

  late final AnimationController _sceneController;
  AnsightDebugSnapshot? _snapshot;
  AnsightHostConnectionStatus? _connection;
  HarnessDatabaseSummary? _databaseSummary;
  Map<String, Object?>? _lastToolResult;
  bool _busy = false;
  bool _autoInitialize = false;
  bool _initialized = false;
  int _metricValue = 10;

  AnsightOptions get _harnessOptions => AnsightOptions.developer(
    clientName: 'Ansight Flutter Harness',
    toolGuard: AnsightToolGuard.fullAccess,
  );

  @override
  void initState() {
    super.initState();
    _autoInitialize = widget.autoInitialize;
    _sceneController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 12),
    );
    if (widget.enableSceneAnimation) {
      _sceneController.repeat();
    }
    _subscriptions
      ..add(
        _ansight.logs.listen((AnsightLogEntry value) {
          _append('${value.level.toUpperCase()} ${value.message}');
        }),
      )
      ..add(
        _ansight.connectionStatusChanges.listen((
          AnsightHostConnectionStatus value,
        ) {
          if (mounted) {
            setState(() => _connection = value);
          }
        }),
      );
    if (widget.autoInitialize) {
      unawaited(_initialize());
    }
  }

  @override
  void dispose() {
    for (final subscription in _subscriptions) {
      subscription.cancel();
    }
    _sceneController.dispose();
    _pairingController.dispose();
    super.dispose();
  }

  Future<void> _initialize() => _run('Initialize + activate', () async {
    _snapshot = await _ansight.initializeAndActivate(_harnessOptions);
    await _ansight.registerMetricChannel(_harnessMetricChannel);
    await AnsightFlutterInstrumentation.instance.install();
    await _fixtureStore.initialize();
    _databaseSummary = await _fixtureStore.summary();
    await _registerHarnessTools();
    await _registerArtifactProvider();
    await _writeStateFixture();
    if (_harnessEnrollmentInviteBase64.isNotEmpty) {
      final payload = utf8.decode(
        base64Decode(_harnessEnrollmentInviteBase64),
      );
      _pairingController.text = payload;
      var result = await _ansight.connect(
        pairingPayload: payload,
        clientName: 'Ansight Flutter Harness',
        expectedAppId: 'ai.ansight.flutter.harness',
      );
      if (!result.success) {
        result = await _ansight.connect(
          clientName: 'Ansight Flutter Harness',
          expectedAppId: 'ai.ansight.flutter.harness',
        );
      }
      if (!result.success) {
        throw StateError(result.message);
      }
    } else {
      final reconnect = await _ansight.connect(
        clientName: 'Ansight Flutter Harness',
        expectedAppId: 'ai.ansight.flutter.harness',
      );
      if (!reconnect.success) {
        _append(reconnect.message);
      }
    }
    _connection = await _ansight.hostConnectionStatus();
    _initialized = true;
    return 'runtime=${_snapshot!.active}, tools=${_snapshot!.registeredTools}';
  });

  Future<void> _registerHarnessTools() async {
    await _registerToolIfNeeded(
      const AnsightToolDefinition(
        id: 'harness.echo',
        name: 'Echo from Flutter',
        description:
            'Returns arguments from the Dart isolate to validate custom tools.',
        category: 'harness',
        scope: AnsightToolScope.read,
        keywords: <String>['flutter', 'dart', 'echo', 'harness'],
        argumentsSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        resultSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.low,
          summary: 'Returns caller-provided strings.',
        ),
      ),
      (Map<String, String> arguments, AnsightToolContext context) async =>
          AnsightToolResult.success(
            message: 'Flutter echo completed.',
            result: <String, Object?>{
              'echo': arguments,
              'platform': context.platform,
              'handledBy': 'dart',
            },
          ),
    );
    await _registerToolIfNeeded(
      const AnsightToolDefinition(
        id: 'harness.inspect_state',
        name: 'Inspect Harness State',
        description:
            'Returns Flutter navigation, database, scene, and runtime state.',
        category: 'harness',
        scope: AnsightToolScope.read,
        keywords: <String>[
          'harness',
          'flutter',
          'state',
          'navigation',
          'database',
          'scene',
        ],
        argumentsSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': false,
        },
        resultSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.low,
          summary: 'Reads synthetic harness state.',
        ),
      ),
      (Map<String, String> arguments, AnsightToolContext context) async {
        final state = await _inspectHarnessState();
        return AnsightToolResult.success(
          message: 'Flutter harness state inspected.',
          result: state,
        );
      },
    );
    await _registerToolIfNeeded(
      const AnsightToolDefinition(
        id: 'harness.advance_state',
        name: 'Advance Harness State',
        description: 'Mutates Flutter harness state using a named action.',
        category: 'harness',
        scope: AnsightToolScope.write,
        keywords: <String>[
          'harness',
          'flutter',
          'mutate',
          'navigation',
          'database',
          'tab',
        ],
        argumentsSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': false,
          'required': <String>['action'],
          'properties': <String, Object?>{
            'action': <String, Object?>{
              'type': 'string',
              'enum': <String>[
                'push',
                'pop',
                'tab_overview',
                'tab_navigation',
                'tab_data',
                'tab_tools',
                'seed_database',
                'insert_item',
                'palette',
                'modal',
              ],
            },
          },
        },
        resultSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.medium,
          summary: 'Mutates synthetic harness UI and fixture data.',
          implications: <String>[
            'May change the selected tab or navigation stack.',
            'May insert rows into the harness database.',
            'May present a dialog.',
          ],
        ),
      ),
      (Map<String, String> arguments, AnsightToolContext context) async {
        final state = await _advanceHarnessState(arguments['action'] ?? 'push');
        return AnsightToolResult.success(
          message: 'Flutter harness state advanced.',
          result: state,
        );
      },
    );
    await _registerToolIfNeeded(
      const AnsightToolDefinition(
        id: 'harness.database_summary',
        name: 'Harness Database Summary',
        description: 'Returns a summary of the seeded Flutter SQLite database.',
        category: 'harness',
        scope: AnsightToolScope.read,
        keywords: <String>[
          'harness',
          'flutter',
          'sqlite',
          'database',
          'summary',
        ],
        argumentsSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': false,
        },
        resultSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.low,
          summary: 'Reads metadata from the synthetic harness database.',
        ),
      ),
      (Map<String, String> arguments, AnsightToolContext context) async {
        _databaseSummary = await _fixtureStore.summary();
        if (mounted) {
          setState(() {});
        }
        return AnsightToolResult.success(
          message: 'Flutter harness database inspected.',
          result: _databaseSummary!.toJson(),
        );
      },
    );
    await _registerToolIfNeeded(
      const AnsightToolDefinition(
        id: 'harness.capture_builtin',
        name: 'Capture Built-in Session Frame',
        description:
            'Requests an immediate frame through the SDK session capture '
            'pipeline, independently of ui.get_screenshot.',
        category: 'harness',
        scope: AnsightToolScope.read,
        keywords: <String>[
          'harness',
          'flutter',
          'capture',
          'screenshot',
          'session',
        ],
        argumentsSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': false,
        },
        resultSchema: <String, Object?>{
          'type': 'object',
          'additionalProperties': true,
        },
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.high,
          summary: 'Captures and transfers the current app screen.',
          implications: <String>[
            'Captures visible app content.',
            'Transfers image data to the paired Studio session.',
          ],
        ),
      ),
      (Map<String, String> arguments, AnsightToolContext context) async {
        final result = await _captureBuiltInFrame();
        return AnsightToolResult(
          success: result.success,
          message: result.message,
          errorCode: result.errorCode,
          result: result.data,
        );
      },
    );
  }

  Future<void> _registerToolIfNeeded(
    AnsightToolDefinition definition,
    AnsightToolHandler handler,
  ) async {
    if (_ansight.registeredToolIds.contains(definition.id)) {
      return;
    }
    await _ansight.registerTool(definition, handler);
  }

  Future<void> _registerArtifactProvider() async {
    if (_ansight.registeredArtifactProviderIds.contains('harness')) {
      return;
    }
    await _ansight.registerArtifactProvider(
      HarnessArtifactProvider(
        fixtureStore: _fixtureStore,
        stateBuilder: _inspectHarnessState,
      ),
    );
  }

  Future<void> _run(String label, FutureOr<Object?> Function() action) async {
    if (_busy) {
      return;
    }
    setState(() => _busy = true);
    try {
      final result = await action();
      _append('✓ $label${result == null ? '' : ': $result'}');
      if (_initialized) {
        _snapshot = await _ansight.snapshot();
        _connection = await _ansight.hostConnectionStatus();
      }
    } catch (error, stackTrace) {
      _append('✕ $label: $error');
      debugPrintStack(stackTrace: stackTrace);
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  void _append(String value) {
    if (!mounted) {
      return;
    }
    setState(() {
      _activity.insert(0, '${TimeOfDay.now().format(context)}  $value');
      if (_activity.length > 100) {
        _activity.removeRange(100, _activity.length);
      }
    });
  }

  String get _pairingPayload => _pairingController.text.trim();

  Future<Map<String, Object?>> _inspectHarnessState() async {
    _databaseSummary = await _fixtureStore.summary();
    final state = _harnessState.toJson(_databaseSummary!);
    _lastToolResult = state;
    await _fixtureStore.writeStateFixture(state);
    if (mounted) {
      setState(() {});
    }
    return state;
  }

  Future<void> _writeStateFixture() async {
    _databaseSummary ??= await _fixtureStore.summary();
    await _fixtureStore.writeStateFixture(
      _harnessState.toJson(_databaseSummary!),
    );
  }

  Future<AnsightOperationResult> _captureBuiltInFrame() async {
    final result = await _ansight.captureScreenFrame();
    _harnessState.lastCapture = jsonEncode(result.data);
    if (_databaseSummary != null) {
      await _writeStateFixture();
    }
    if (mounted) {
      setState(() {});
    }
    return result;
  }

  Future<void> _scanEnrollmentQrCode() => _run(
    'Scan enrollment QR',
    () => _ansight.enrollFromQrCode(
      clientName: 'Ansight Flutter Harness',
      expectedAppId: 'ai.ansight.flutter.harness',
    ),
  );

  Future<void> _showPairingDialog() async {
    final controller = TextEditingController(text: _pairingPayload);
    final result = await showDialog<PairingDialogResult>(
      context: context,
      builder: (BuildContext dialogContext) => AlertDialog(
        key: const Key('pairing-dialog'),
        title: const Text('Enroll with Ansight Studio'),
        content: SizedBox(
          width: 520,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              const Text(
                'Scan the one-use Studio enrollment QR. The app reconnects '
                'automatically after the first successful scan.',
              ),
              const SizedBox(height: 14),
              TextField(
                key: const Key('pairing-dialog-payload'),
                controller: controller,
                minLines: 3,
                maxLines: 7,
                decoration: const InputDecoration(
                  labelText: 'Pairing payload',
                  border: OutlineInputBorder(),
                ),
              ),
            ],
          ),
        ),
        actions: <Widget>[
          TextButton(
            key: const Key('pairing-dialog-cancel'),
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton.tonalIcon(
            key: const Key('pairing-dialog-scan-qr'),
            onPressed: () => Navigator.of(
              dialogContext,
            ).pop(const PairingDialogResult.scanQr()),
            icon: const Icon(Icons.qr_code_scanner),
            label: const Text('Scan QR'),
          ),
          FilledButton(
            key: const Key('pairing-dialog-connect'),
            onPressed: () => Navigator.of(
              dialogContext,
            ).pop(PairingDialogResult.connectPayload(controller.text.trim())),
            child: const Text('Connect'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (result == null) {
      return;
    }
    switch (result.action) {
      case PairingDialogAction.scanQr:
        await _scanEnrollmentQrCode();
        break;
      case PairingDialogAction.connectPayload:
        _pairingController.text = result.payload ?? '';
        await _run(
          'Connect with pairing payload',
          () => _ansight.connect(
            pairingPayload: _pairingPayload,
            clientName: 'Ansight Flutter Harness',
            expectedAppId: 'ai.ansight.flutter.harness',
          ),
        );
        break;
    }
  }

  Future<Map<String, Object?>> _advanceHarnessState(String rawAction) async {
    final action = rawAction.trim().toLowerCase();
    var showModal = false;
    switch (action) {
      case 'push':
        _pushRoute(
          'Tool Route',
          'Pushed by custom Ansight tool',
          recordTelemetry: false,
        );
        break;
      case 'pop':
        _popRoute(recordTelemetry: false);
        break;
      case 'tab_overview':
        _selectTab(HarnessTab.overview, recordTelemetry: false);
        break;
      case 'tab_navigation':
        _selectTab(HarnessTab.navigation, recordTelemetry: false);
        break;
      case 'tab_data':
        _selectTab(HarnessTab.data, recordTelemetry: false);
        break;
      case 'tab_tools':
        _selectTab(HarnessTab.tools, recordTelemetry: false);
        break;
      case 'seed_database':
        await _fixtureStore.seed();
        break;
      case 'insert_item':
        _harnessState.lastInsertedItem = await _fixtureStore
            .insertGeneratedItem();
        break;
      case 'palette':
        _harnessState.scene.togglePalette();
        break;
      case 'modal':
        showModal = true;
        break;
      default:
        throw ArgumentError.value(rawAction, 'action', 'is unsupported');
    }
    _harnessState.customToolInvocations += 1;
    _databaseSummary = await _fixtureStore.summary();
    final state = _harnessState.toJson(_databaseSummary!);
    _lastToolResult = state;
    await _fixtureStore.writeStateFixture(state);
    if (mounted) {
      setState(() {});
      if (showModal) {
        unawaited(_showHarnessDialog(origin: 'remote tool'));
      }
    }
    return state;
  }

  void _selectTab(HarnessTab tab, {bool recordTelemetry = true}) {
    setState(() => _harnessState.selectedTab = tab);
    if (recordTelemetry) {
      unawaited(_recordHarnessScreen(tab));
    }
  }

  Future<void> _recordHarnessScreen(HarnessTab tab) async {
    if (!_initialized) {
      return;
    }
    await _ansight.screenViewed('Harness.${tab.name}');
  }

  void _pushRoute(String name, String detail, {bool recordTelemetry = true}) {
    setState(() {
      _harnessState.navigationStack.add(HarnessRoute(name, detail));
      _harnessState.navigationOperations += 1;
    });
    if (recordTelemetry) {
      unawaited(_recordHarnessEvent('navigation.push'));
    }
  }

  void _popRoute({bool recordTelemetry = true}) {
    setState(() {
      if (_harnessState.navigationStack.length > 1) {
        _harnessState.navigationStack.removeLast();
      }
      _harnessState.navigationOperations += 1;
    });
    if (recordTelemetry) {
      unawaited(_recordHarnessEvent('navigation.pop'));
    }
  }

  void _replaceRoute() {
    setState(() {
      final replacement = HarnessRoute('Settings', 'Route replaced in place');
      if (_harnessState.navigationStack.isEmpty) {
        _harnessState.navigationStack.add(replacement);
      } else {
        _harnessState.navigationStack.last = replacement;
      }
      _harnessState.navigationOperations += 1;
    });
    unawaited(_recordHarnessEvent('navigation.replace'));
  }

  Future<void> _recordHarnessEvent(String label) async {
    if (!_initialized) {
      return;
    }
    _harnessState.eventButtonTaps += 1;
    await _ansight.event(
      label,
      type: AnsightEventType.navigation,
      details:
          'tab=${_harnessState.selectedTab.title};'
          'route=${_harnessState.navigationStack.last.name}',
      channel: 42,
    );
  }

  Future<void> _showHarnessDialog({String origin = 'navigation panel'}) async {
    if (!mounted) {
      return;
    }
    setState(() => _harnessState.modalPresentations += 1);
    await showDialog<void>(
      context: context,
      builder: (BuildContext dialogContext) => AlertDialog(
        key: const Key('harness-dialog'),
        title: const Text('Ansight modal capture'),
        content: Text(
          'Presented from $origin. The rendered scene remains visible behind '
          'this dialog for screenshot and visual-tree validation.',
        ),
        actions: <Widget>[
          TextButton(
            key: const Key('dialog-push-route'),
            onPressed: () {
              Navigator.of(dialogContext).pop();
              _pushRoute('Modal Result', 'Created from Flutter dialog');
            },
            child: const Text('Push route'),
          ),
          FilledButton(
            key: const Key('dialog-dismiss'),
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Dismiss'),
          ),
        ],
      ),
    );
    if (mounted) {
      setState(() => _harnessState.modalDismissals += 1);
    }
  }

  Future<void> _showHarnessBottomSheet() async {
    setState(() => _harnessState.modalPresentations += 1);
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (BuildContext sheetContext) => Padding(
        key: const Key('harness-bottom-sheet'),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Text(
              'Bottom Sheet Flow',
              style: Theme.of(sheetContext).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            const Text(
              'Validates modal visual-tree placement and navigation from a '
              'Material bottom sheet.',
            ),
            const SizedBox(height: 16),
            FilledButton.tonal(
              key: const Key('bottom-sheet-push'),
              onPressed: () {
                Navigator.of(sheetContext).pop();
                _pushRoute('Bottom Sheet', 'Created from Flutter bottom sheet');
              },
              child: const Text('Push sheet route'),
            ),
            FilledButton.tonal(
              key: const Key('bottom-sheet-insert'),
              onPressed: () {
                Navigator.of(sheetContext).pop();
                unawaited(
                  _run('Insert database row', () async {
                    _harnessState.lastInsertedItem = await _fixtureStore
                        .insertGeneratedItem();
                    _databaseSummary = await _fixtureStore.summary();
                    return _harnessState.lastInsertedItem;
                  }),
                );
              },
              child: const Text('Insert database row'),
            ),
          ],
        ),
      ),
    );
    if (mounted) {
      setState(() => _harnessState.modalDismissals += 1);
    }
  }

  Future<void> _runEndToEndScenario() =>
      _run('Run end-to-end scenario', () async {
        if (!_initialized) {
          await _ansight.initializeAndActivate(_harnessOptions);
          await _ansight.registerMetricChannel(_harnessMetricChannel);
          await AnsightFlutterInstrumentation.instance.install();
          await _fixtureStore.initialize();
          await _registerHarnessTools();
          await _registerArtifactProvider();
          _initialized = true;
        }

        await _fixtureStore.seed();
        _harnessState.lastInsertedItem = await _fixtureStore
            .insertGeneratedItem();
        _harnessState.metricButtonTaps += 1;
        await _ansight.metric(4242, channel: 42);
        for (final type in AnsightEventType.values) {
          await _ansight.event(
            'Flutter E2E ${type.wireName}',
            type: type,
            details: 'Deterministic harness end-to-end scenario.',
            channel: 42,
          );
        }
        await _ansight.screenViewed(
          'Harness.e2e',
          details: <String, String>{'scenario': 'all-features'},
        );
        await _ansight.setAppLifecycleState(AnsightLifecycleState.foreground);
        await _ansight.setAppLifecycleState(AnsightLifecycleState.background);
        await _ansight.setAppLifecycleState(AnsightLifecycleState.foreground);
        await _ansight.captureBuiltInTelemetrySample();
        final capture = await _captureBuiltInFrame();
        await _ansight.enableFramesPerSecond();
        if (!await _ansight.isFramesPerSecondEnabled()) {
          throw StateError('FPS instrumentation did not enable.');
        }
        await _ansight.disableFramesPerSecond();
        await _ansight.enableTouchCapture();
        await _ansight.disableTouchCapture();
        await _ansight.updateSessionProperties(<String, Map<String, String>>{
          'harness': <String, String>{
            'framework': 'flutter',
            'scenario': 'end-to-end',
            'tab': HarnessTab.data.title,
          },
          'scene': <String, String>{
            'palette': _harnessState.scene.paletteName,
            'rotationSpeed': '${_harnessState.scene.rotationSpeed}',
          },
        });
        await _ansight.sendClientLog('Flutter harness E2E scenario completed');

        _harnessState
          ..selectedTab = HarnessTab.data
          ..navigationOperations += 1
          ..e2eRuns += 1;
        _harnessState.navigationStack.add(
          HarnessRoute('E2E Result', 'Created by deterministic scenario'),
        );
        _harnessState.scene.togglePalette();
        _snapshot = await _ansight.snapshot();
        _connection = await _ansight.hostConnectionStatus();
        _databaseSummary = await _fixtureStore.summary();
        _lastToolResult = _harnessState.toJson(_databaseSummary!);
        await _fixtureStore.writeStateFixture(_lastToolResult!);

        final metrics = await _ansight.recordedMetrics(limit: 50);
        final events = await _ansight.recordedEvents(limit: 50);
        if (metrics.isEmpty || events.length < AnsightEventType.values.length) {
          throw StateError(
            'Retained telemetry incomplete: '
            '${metrics.length} metrics, ${events.length} events.',
          );
        }
        return <String, Object?>{
          'metrics': metrics.length,
          'events': events.length,
          'tools': _ansight.registeredToolIds.length,
          'artifacts': _ansight.registeredArtifactProviderIds.length,
          'databaseItems': _databaseSummary!.itemCount,
          'stateFile': _databaseSummary!.fixtureFilePath,
          'builtInCapture': capture.success,
        };
      });

  @override
  Widget build(BuildContext context) => Scaffold(
    drawer: _buildHarnessDrawer(),
    appBar: AppBar(
      title: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('Ansight Flutter Harness'),
          Text(
            'Native + Dart observability validation',
            style: TextStyle(fontSize: 12, fontWeight: FontWeight.normal),
          ),
        ],
      ),
      actions: <Widget>[
        IconButton(
          key: const Key('open-pairing-dialog'),
          tooltip: 'Enroll with Studio',
          onPressed: _busy ? null : _showPairingDialog,
          icon: const Icon(Icons.qr_code_scanner),
        ),
        if (_busy)
          const Padding(
            padding: EdgeInsets.all(16),
            child: SizedBox.square(
              dimension: 22,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          ),
      ],
    ),
    body: SelectionArea(
      child: ListView(
        key: const Key('harness-scroll'),
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          _StatusCard(snapshot: _snapshot, connection: _connection),
          const SizedBox(height: 16),
          _buildFixtureDashboard(),
          const SizedBox(height: 16),
          FilledButton.icon(
            key: const Key('run-e2e-scenario'),
            onPressed: _busy ? null : _runEndToEndScenario,
            icon: const Icon(Icons.play_arrow),
            label: const Text('Run deterministic end-to-end scenario'),
          ),
          const SizedBox(height: 16),
          _section(
            'Runtime',
            'Initialize, activate, deactivate, clear, and inspect state.',
            <Widget>[
              _button(
                'Initialize + activate',
                _initialize,
                key: const Key('initialize'),
              ),
              _button(
                'Initialize only',
                () => _run('Initialize', () async {
                  _snapshot = await _ansight.initialize(_harnessOptions);
                  _initialized = true;
                  return null;
                }),
              ),
              _button(
                'Activate',
                () => _run('Activate', () async {
                  _snapshot = await _ansight.activate();
                  return null;
                }),
              ),
              _button(
                'Deactivate',
                () => _run('Deactivate', () async {
                  _snapshot = await _ansight.deactivate();
                  return null;
                }),
              ),
              _button(
                'Snapshot',
                () => _run('Snapshot', () async {
                  _snapshot = await _ansight.snapshot();
                  return jsonEncode(_snapshot!.data);
                }),
                key: const Key('snapshot'),
              ),
              _button(
                'Clear runtime',
                () => _run('Clear runtime', () async {
                  _snapshot = await _ansight.clear();
                  _initialized = false;
                  return null;
                }),
              ),
            ],
          ),
          _section(
            'Telemetry',
            'Metrics, events, screens, lifecycle, FPS, and retained buffers.',
            <Widget>[
              _button(
                'Record metric',
                () => _run('Record metric', () async {
                  _metricValue += 7;
                  await _ansight.metric(_metricValue, channel: 255);
                  return _metricValue;
                }),
                key: const Key('record-metric'),
              ),
              _button(
                'Record all event types',
                () => _run('Record all event types', () async {
                  for (final type in AnsightEventType.values) {
                    await _ansight.event(
                      'Flutter ${type.wireName}',
                      type: type,
                      details: 'Generated by the all-features harness.',
                    );
                  }
                  return '${AnsightEventType.values.length} events';
                }),
                key: const Key('record-events'),
              ),
              _button(
                'Capture built-in sample',
                () => _run(
                  'Capture built-in sample',
                  _ansight.captureBuiltInTelemetrySample,
                ),
              ),
              _button(
                'Enable FPS',
                () => _run('Enable FPS', _ansight.enableFramesPerSecond),
              ),
              _button(
                'Disable FPS',
                () => _run('Disable FPS', _ansight.disableFramesPerSecond),
              ),
              _button(
                'Read retained telemetry',
                () => _run('Read retained telemetry', () async {
                  final metrics = await _ansight.recordedMetrics(limit: 20);
                  final events = await _ansight.recordedEvents(limit: 20);
                  return '${metrics.length} metrics, ${events.length} events';
                }),
              ),
              _button(
                'Send client log',
                () => _run(
                  'Send client log',
                  () =>
                      _ansight.sendClientLog('Hello from the Flutter harness'),
                ),
              ),
            ],
          ),
          _section(
            'Visual evidence and input',
            'Native screenshot/touch capture plus Flutter widget inspection.',
            <Widget>[
              _button(
                'Capture screen frame',
                () => _run('Capture screen frame', _captureBuiltInFrame),
                key: const Key('capture-screen'),
              ),
              _button(
                'Enable touch capture',
                () => _run('Enable touch capture', _ansight.enableTouchCapture),
              ),
              _button(
                'Disable touch capture',
                () =>
                    _run('Disable touch capture', _ansight.disableTouchCapture),
              ),
              _button(
                'Install Flutter inspection',
                () => _run(
                  'Install Flutter inspection',
                  AnsightFlutterInstrumentation.instance.install,
                ),
                key: const Key('install-flutter-tools'),
              ),
              _button(
                'Open navigation fixture',
                () => Navigator.of(context).push<void>(
                  MaterialPageRoute<void>(
                    settings: const RouteSettings(name: '/fixture'),
                    builder: (BuildContext context) =>
                        const NavigationFixturePage(),
                  ),
                ),
              ),
              _button(
                'Report handled exception',
                () => _run('Report handled exception', () async {
                  try {
                    throw StateError('Harness-generated handled exception');
                  } catch (error, stackTrace) {
                    await _ansight.event(
                      error.toString(),
                      type: AnsightEventType.exception,
                      details: stackTrace.toString(),
                    );
                  }
                  return null;
                }),
              ),
            ],
          ),
          _section(
            'Host enrollment and sessions',
            'Scan once for normal use; payload controls exercise the lower-level test surface.',
            <Widget>[
              SizedBox(
                width: 560,
                child: TextField(
                  key: const Key('pairing-payload'),
                  controller: _pairingController,
                  minLines: 2,
                  maxLines: 5,
                  decoration: const InputDecoration(
                    labelText: 'Enrollment payload',
                    hintText: 'ans2… or {"schema":"ansight.enrollment-invite.v2",…}',
                    border: OutlineInputBorder(),
                  ),
                ),
              ),
              _button(
                'QR enrollment dialog',
                _showPairingDialog,
                key: const Key('show-pairing-dialog'),
              ),
              _button(
                'Auto connect',
                () => _run(
                  'Auto connect',
                  () => _ansight.connect(clientName: 'Ansight Flutter Harness'),
                ),
              ),
              _button(
                'Connect with payload',
                () => _run(
                  'Connect with payload',
                  () => _ansight.connect(
                    pairingPayload: _pairingPayload,
                    clientName: 'Ansight Flutter Harness',
                  ),
                ),
              ),
              _button(
                'Open session',
                () => _run(
                  'Open session',
                  () => _ansight.openSession(
                    _pairingPayload,
                    clientName: 'Ansight Flutter Harness',
                  ),
                ),
              ),
              _button(
                'Save enrollment',
                () => _run(
                  'Save enrollment',
                  () => _ansight.savePairingConfig(_pairingPayload),
                ),
              ),
              _button(
                'Disconnect',
                () => _run('Disconnect', _ansight.disconnect),
              ),
              _button(
                'Complete session',
                () => _run('Complete session', _ansight.completeSession),
              ),
              _button(
                'Close session',
                () => _run('Close session', _ansight.closeSession),
              ),
              _button(
                'Clear saved enrollment',
                () =>
                    _run('Clear saved enrollment', _ansight.clearSavedPairing),
              ),
              _button(
                'Clear cached session',
                () => _run('Clear cached session', _ansight.clearCachedSession),
              ),
              _button(
                'Refresh host config',
                () => _run(
                  'Refresh host config',
                  _ansight.notifyHostConnectionConfigChanged,
                ),
              ),
            ],
          ),
          _section(
            'Properties, tools, and artifacts',
            'Session metadata, Dart handlers, and binary export providers.',
            <Widget>[
              _button(
                'Set grouped properties',
                () => _run(
                  'Set grouped properties',
                  () => _ansight.updateSessionProperties(
                    <String, Map<String, String>>{
                      'flutter': <String, String>{
                        'mode': 'harness',
                        'renderer': 'impeller',
                      },
                      'user': <String, String>{'cohort': 'sdk-validation'},
                    },
                  ),
                ),
                key: const Key('set-properties'),
              ),
              _button(
                'Set one property',
                () => _run(
                  'Set one property',
                  () => _ansight.registerCustomProperty(
                    'flutter',
                    'lastAction',
                    DateTime.now().toUtc().toIso8601String(),
                  ),
                ),
              ),
              _button(
                'Remove one property',
                () => _run(
                  'Remove one property',
                  () => _ansight.removeCustomProperty('flutter', 'lastAction'),
                ),
              ),
              _button(
                'Clear properties',
                () => _run('Clear properties', _ansight.clearSessionProperties),
              ),
              _button(
                'Register harness tools',
                () => _run('Register harness tools', _registerHarnessTools),
              ),
              _button(
                'Register artifact provider',
                () => _run(
                  'Register artifact provider',
                  _registerArtifactProvider,
                ),
              ),
              _button(
                'Show native options',
                () => _run('Show native options', () async {
                  return jsonEncode(await _ansight.currentOptions());
                }),
              ),
              _button(
                'Show capabilities',
                () => _run('Show capabilities', () async {
                  final value = await _ansight.hostConnectionCapabilities();
                  return jsonEncode(value.data);
                }),
              ),
            ],
          ),
          _section(
            'Harness controls',
            'Fixture behavior and recent bridge activity.',
            <Widget>[
              Row(
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Switch(
                    value: _autoInitialize,
                    onChanged: (bool value) {
                      setState(() => _autoInitialize = value);
                    },
                  ),
                  const Text('Auto-initialize fixture flag'),
                ],
              ),
              _button(
                'Generate slow frame',
                () => _run('Generate slow frame', () {
                  final stopwatch = Stopwatch()..start();
                  while (stopwatch.elapsedMilliseconds < 45) {
                    // Deliberately block the UI isolate for jank telemetry.
                  }
                  return null;
                }),
              ),
              _button('Clear activity', () {
                setState(_activity.clear);
              }),
            ],
          ),
          const SizedBox(height: 12),
          Text('Activity', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 8),
          Container(
            key: const Key('activity-log'),
            constraints: const BoxConstraints(minHeight: 160),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: const Color(0xff0b0c10),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: Colors.white12),
            ),
            child: _activity.isEmpty
                ? const Text('No activity yet.')
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: _activity
                        .map(
                          (String line) => Padding(
                            padding: const EdgeInsets.only(bottom: 4),
                            child: Text(
                              line,
                              style: const TextStyle(
                                fontFamily: 'monospace',
                                fontSize: 12,
                              ),
                            ),
                          ),
                        )
                        .toList(growable: false),
                  ),
          ),
          const SizedBox(height: 40),
        ],
      ),
    ),
  );

  Widget _buildHarnessDrawer() => Drawer(
    key: const Key('harness-drawer'),
    child: SafeArea(
      child: ListView(
        padding: const EdgeInsets.symmetric(vertical: 12),
        children: <Widget>[
          const ListTile(
            leading: Icon(Icons.visibility),
            title: Text('Ansight Harness'),
            subtitle: Text('Flutter fixture navigation'),
          ),
          const Divider(),
          for (final tab in HarnessTab.values)
            ListTile(
              key: Key('drawer-tab-${tab.name}'),
              selected: _harnessState.selectedTab == tab,
              leading: Icon(_tabIcon(tab)),
              title: Text(tab.title),
              onTap: () {
                Navigator.of(context).pop();
                _harnessState.flyoutOpens += 1;
                _selectTab(tab);
              },
            ),
        ],
      ),
    ),
  );

  Widget _buildFixtureDashboard() => Card(
    key: const Key('fixture-dashboard'),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            'Interactive app fixtures',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 3),
          Text(
            'The same navigation, data, scene, and custom-tool contract used '
            'by the native harnesses.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: <Widget>[
              for (final tab in HarnessTab.values)
                ChoiceChip(
                  key: Key('fixture-tab-${tab.name}'),
                  avatar: Icon(_tabIcon(tab), size: 18),
                  label: Text(tab.title),
                  selected: _harnessState.selectedTab == tab,
                  onSelected: (bool selected) {
                    if (selected) {
                      _selectTab(tab);
                    }
                  },
                ),
            ],
          ),
          const SizedBox(height: 16),
          AnimatedSwitcher(
            duration: const Duration(milliseconds: 180),
            child: KeyedSubtree(
              key: ValueKey<HarnessTab>(_harnessState.selectedTab),
              child: switch (_harnessState.selectedTab) {
                HarnessTab.overview => _buildOverviewFixture(),
                HarnessTab.navigation => _buildNavigationFixture(),
                HarnessTab.data => _buildDataFixture(),
                HarnessTab.tools => _buildToolsFixture(),
              },
            ),
          ),
        ],
      ),
    ),
  );

  Widget _buildOverviewFixture() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: <Widget>[
      Text('Rendered scene', style: Theme.of(context).textTheme.titleMedium),
      const SizedBox(height: 6),
      const Text(
        'Animated custom painting provides a distinctive frame for screenshot '
        'and Flutter widget-tree validation.',
      ),
      const SizedBox(height: 12),
      RepaintBoundary(
        key: const Key('scene-repaint-boundary'),
        child: SizedBox(
          height: 220,
          width: double.infinity,
          child: AnimatedBuilder(
            animation: _sceneController,
            builder: (BuildContext context, Widget? child) {
              _harnessState.scene.lastFrameEpochMs =
                  DateTime.now().millisecondsSinceEpoch;
              return CustomPaint(
                key: const Key('rendered-scene'),
                painter: HarnessScenePainter(
                  progress: _sceneController.value,
                  paletteName: _harnessState.scene.paletteName,
                ),
              );
            },
          ),
        ),
      ),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          _button(
            'Slow scene',
            () => setState(() {
              _harnessState.scene.rotationSpeed = 22;
              _sceneController.duration = const Duration(seconds: 22);
              if (widget.enableSceneAnimation) {
                _sceneController.repeat();
              }
            }),
            key: const Key('scene-slow'),
          ),
          _button(
            'Fast scene',
            () => setState(() {
              _harnessState.scene.rotationSpeed = 92;
              _sceneController.duration = const Duration(seconds: 5);
              if (widget.enableSceneAnimation) {
                _sceneController.repeat();
              }
            }),
            key: const Key('scene-fast'),
          ),
          _button('Swap palette', () {
            setState(_harnessState.scene.togglePalette);
            unawaited(_recordHarnessEvent('scene.palette'));
          }, key: const Key('scene-palette')),
          _button(
            'Show modal',
            _showHarnessDialog,
            key: const Key('overview-modal'),
          ),
        ],
      ),
      const SizedBox(height: 10),
      Text(
        'Palette: ${_harnessState.scene.paletteName} · '
        'speed: ${_harnessState.scene.rotationSpeed.round()}°/s',
        key: const Key('scene-status'),
      ),
    ],
  );

  Widget _buildNavigationFixture() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: <Widget>[
      Text(
        'Navigation paradigms',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 6),
      const Text(
        'Tabs, drawer, push/pop/replace state, dialog, bottom sheet, and an '
        'actual Navigator route are all available.',
      ),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          _button(
            'Push details',
            () => _pushRoute('Details', 'Pushed from navigation controls'),
            key: const Key('nav-push'),
          ),
          _button('Pop', _popRoute, key: const Key('nav-pop')),
          _button(
            'Replace settings',
            _replaceRoute,
            key: const Key('nav-replace'),
          ),
          _button(
            'Bottom sheet',
            _showHarnessBottomSheet,
            key: const Key('nav-bottom-sheet'),
          ),
          _button(
            'Dialog modal',
            _showHarnessDialog,
            key: const Key('nav-dialog'),
          ),
          _button(
            'Push real route',
            () => Navigator.of(context).push<void>(
              MaterialPageRoute<void>(
                settings: const RouteSettings(name: '/fixture'),
                builder: (BuildContext context) =>
                    const NavigationFixturePage(),
              ),
            ),
            key: const Key('nav-real-route'),
          ),
        ],
      ),
      const SizedBox(height: 14),
      Text('Route stack', style: Theme.of(context).textTheme.titleSmall),
      const SizedBox(height: 6),
      Container(
        key: const Key('route-stack'),
        width: double.infinity,
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(
          color: const Color(0xff11131a),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            for (
              var index = 0;
              index < _harnessState.navigationStack.length;
              index += 1
            )
              Text(
                '${index + 1}. '
                '${_harnessState.navigationStack[index].name} — '
                '${_harnessState.navigationStack[index].detail}',
              ),
          ],
        ),
      ),
    ],
  );

  Widget _buildDataFixture() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: <Widget>[
      Text('SQLite data', style: Theme.of(context).textTheme.titleMedium),
      const SizedBox(height: 6),
      const Text(
        'A native SQLite database, preferences, and document file are seeded '
        'for Studio data and filesystem tools.',
      ),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          _button(
            'Seed database',
            () => _run('Seed database', () async {
              await _fixtureStore.seed();
              _databaseSummary = await _fixtureStore.summary();
              await _writeStateFixture();
              return _databaseSummary!.toJson();
            }),
            key: const Key('data-seed'),
          ),
          _button(
            'Insert row',
            () => _run('Insert database row', () async {
              _harnessState.lastInsertedItem = await _fixtureStore
                  .insertGeneratedItem();
              _databaseSummary = await _fixtureStore.summary();
              await _writeStateFixture();
              return _harnessState.lastInsertedItem;
            }),
            key: const Key('data-insert'),
          ),
          _button(
            'Refresh summary',
            () => _run('Refresh database summary', () async {
              _databaseSummary = await _fixtureStore.summary();
              return _databaseSummary!.toJson();
            }),
            key: const Key('data-refresh'),
          ),
        ],
      ),
      const SizedBox(height: 14),
      Container(
        key: const Key('database-summary'),
        width: double.infinity,
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(
          color: const Color(0xff11131a),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Text(
          _databaseSummary == null
              ? 'Database initializes with the runtime.'
              : const JsonEncoder.withIndent(
                  '  ',
                ).convert(_databaseSummary!.toJson()),
          style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
        ),
      ),
    ],
  );

  Widget _buildToolsFixture() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: <Widget>[
      Text(
        'Custom Ansight tools',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 6),
      const Text(
        'Studio can inspect or mutate this screen through Dart handlers '
        'registered beside the standard native and Flutter tools.',
      ),
      const SizedBox(height: 12),
      const SelectableText(
        'harness.inspect_state\n'
        'harness.advance_state\n'
        'harness.database_summary\n'
        'harness.capture_builtin\n'
        'harness.echo',
        key: Key('custom-tool-list'),
        style: TextStyle(fontFamily: 'monospace'),
      ),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          _button(
            'Inspect locally',
            () => _run('Inspect harness state', _inspectHarnessState),
            key: const Key('tool-inspect-local'),
          ),
          _button(
            'Advance locally',
            () => _run(
              'Advance harness state',
              () => _advanceHarnessState('push'),
            ),
            key: const Key('tool-advance-local'),
          ),
          _button(
            'Database summary',
            () => _run('Harness database summary', () async {
              _databaseSummary = await _fixtureStore.summary();
              _lastToolResult = _databaseSummary!.toJson();
              return _lastToolResult;
            }),
            key: const Key('tool-database-local'),
          ),
        ],
      ),
      const SizedBox(height: 14),
      Container(
        key: const Key('last-tool-result'),
        width: double.infinity,
        constraints: const BoxConstraints(maxHeight: 260),
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(
          color: const Color(0xff11131a),
          borderRadius: BorderRadius.circular(10),
        ),
        child: SingleChildScrollView(
          child: Text(
            _lastToolResult == null
                ? 'No tool result yet.'
                : const JsonEncoder.withIndent('  ').convert(_lastToolResult),
            style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
          ),
        ),
      ),
    ],
  );

  IconData _tabIcon(HarnessTab tab) => switch (tab) {
    HarnessTab.overview => Icons.dashboard,
    HarnessTab.navigation => Icons.route,
    HarnessTab.data => Icons.storage,
    HarnessTab.tools => Icons.build,
  };

  Widget _section(String title, String subtitle, List<Widget> children) =>
      Padding(
        padding: const EdgeInsets.only(bottom: 16),
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(title, style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 3),
                Text(subtitle, style: Theme.of(context).textTheme.bodySmall),
                const SizedBox(height: 14),
                Wrap(spacing: 8, runSpacing: 8, children: children),
              ],
            ),
          ),
        ),
      );

  Widget _button(String label, VoidCallback onPressed, {Key? key}) =>
      FilledButton.tonal(
        key: key,
        onPressed: _busy ? null : onPressed,
        child: Text(label),
      );
}

class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.snapshot, required this.connection});

  final AnsightDebugSnapshot? snapshot;
  final AnsightHostConnectionStatus? connection;

  @override
  Widget build(BuildContext context) => Card(
    key: const Key('runtime-status'),
    color: snapshot?.active == true
        ? const Color(0xff17271f)
        : const Color(0xff24212b),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Wrap(
        spacing: 28,
        runSpacing: 12,
        children: <Widget>[
          _value('Initialized', '${snapshot?.initialized ?? false}'),
          _value('Active', '${snapshot?.active ?? false}'),
          _value('Session', '${snapshot?.sessionOpen ?? false}'),
          _value('Connection', connection?.connectionState ?? 'loading'),
          _value('Metrics', '${snapshot?.metricsRecorded ?? 0}'),
          _value('Events', '${snapshot?.eventsRecorded ?? 0}'),
          _value('Tools', '${snapshot?.registeredTools ?? 0}'),
          _value('Touches', '${snapshot?.touchesRecorded ?? 0}'),
        ],
      ),
    ),
  );

  Widget _value(String label, String value) => SizedBox(
    width: 105,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(
          label.toUpperCase(),
          style: const TextStyle(fontSize: 10, color: Colors.white54),
        ),
        const SizedBox(height: 3),
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ],
    ),
  );
}

class NavigationFixturePage extends StatelessWidget {
  const NavigationFixturePage({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Navigation Fixture')),
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          const Icon(Icons.route, size: 64),
          const SizedBox(height: 16),
          const Text(
            'This route validates screen-view and navigator instrumentation.',
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Return to harness'),
          ),
        ],
      ),
    ),
  );
}

enum PairingDialogAction { scanQr, connectPayload }

final class PairingDialogResult {
  const PairingDialogResult.scanQr()
    : action = PairingDialogAction.scanQr,
      payload = null;

  const PairingDialogResult.connectPayload(this.payload)
    : action = PairingDialogAction.connectPayload;

  final PairingDialogAction action;
  final String? payload;
}

final class HarnessArtifactProvider implements AnsightArtifactProvider {
  HarnessArtifactProvider({
    required this.fixtureStore,
    required this.stateBuilder,
  });

  final HarnessFixtureStore fixtureStore;
  final Future<Map<String, Object?>> Function() stateBuilder;

  @override
  AnsightArtifactProviderDescriptor get descriptor =>
      const AnsightArtifactProviderDescriptor(
        id: 'harness',
        name: 'Flutter Harness',
        description: 'Synthetic files used to validate Dart artifact transfer.',
        category: 'diagnostics',
      );

  @override
  Future<List<AnsightArtifactDefinition>> query() async =>
      const <AnsightArtifactDefinition>[
        AnsightArtifactDefinition(
          id: 'state',
          name: 'Harness state',
          description: 'A JSON fixture generated in the Dart isolate.',
          kind: 'state',
          category: 'diagnostics',
          mimeType: 'application/json',
          fileName: 'flutter-harness-state.json',
        ),
        AnsightArtifactDefinition(
          id: 'binary',
          name: 'Binary fixture',
          description: 'A deterministic binary payload.',
          kind: 'binary',
          category: 'diagnostics',
          fileName: 'flutter-harness.bin',
        ),
      ];

  @override
  Future<AnsightArtifactPayload> create(AnsightArtifactRequest request) async {
    if (request.artifactId == 'state') {
      final state = await stateBuilder();
      await fixtureStore.writeStateFixture(state);
      return AnsightArtifactPayload.text(
        const JsonEncoder.withIndent('  ').convert(<String, Object?>{
          'framework': 'flutter',
          'provider': 'harness',
          'feature': 'dart-binary-artifacts',
          'generatedAtUtc': DateTime.now().toUtc().toIso8601String(),
          'state': state,
        }),
        mimeType: 'application/json',
        fileName: 'flutter-harness-state.json',
        name: 'Flutter harness state',
        kind: 'state',
      );
    }
    return AnsightArtifactPayload(
      bytes: fixtureStore.createBinaryFixture(),
      mimeType: 'application/octet-stream',
      fileName: 'flutter-harness.bin',
      name: 'Flutter binary fixture',
      kind: 'binary',
    );
  }
}

final class HarnessScenePainter extends CustomPainter {
  const HarnessScenePainter({
    required this.progress,
    required this.paletteName,
  });

  final double progress;
  final String paletteName;

  @override
  void paint(Canvas canvas, Size size) {
    final background = Paint()
      ..shader = LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: paletteName == 'studio'
            ? const <Color>[
                Color(0xff171331),
                Color(0xff0b2230),
                Color(0xff101116),
              ]
            : const <Color>[
                Color(0xff40130f),
                Color(0xff32152f),
                Color(0xff101116),
              ],
      ).createShader(Offset.zero & size);
    canvas.drawRRect(
      RRect.fromRectAndRadius(Offset.zero & size, const Radius.circular(14)),
      background,
    );

    final gridPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.08)
      ..strokeWidth = 1;
    for (var x = 0.0; x < size.width; x += 28) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), gridPaint);
    }
    for (var y = 0.0; y < size.height; y += 28) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), gridPaint);
    }

    final center = Offset(size.width / 2, size.height / 2);
    final radius = math.min(size.width, size.height) * 0.25;
    final angle = progress * math.pi * 2;
    final points = List<Offset>.generate(6, (int index) {
      final pointAngle = angle + index * math.pi / 3;
      return center +
          Offset(math.cos(pointAngle), math.sin(pointAngle) * 0.72) * radius;
    });
    final accentColors = paletteName == 'studio'
        ? const <Color>[Color(0xff7557ff), Color(0xff24c7b1), Color(0xff56a8ff)]
        : const <Color>[
            Color(0xffff6b3d),
            Color(0xffffc247),
            Color(0xffff4e91),
          ];
    final centerOffset = Offset(
      center.dx + math.cos(angle * 0.7) * radius * 0.22,
      center.dy + math.sin(angle * 0.7) * radius * 0.14,
    );
    for (var index = 0; index < points.length; index += 1) {
      final next = points[(index + 1) % points.length];
      final face = Path()
        ..moveTo(centerOffset.dx, centerOffset.dy)
        ..lineTo(points[index].dx, points[index].dy)
        ..lineTo(next.dx, next.dy)
        ..close();
      canvas.drawPath(
        face,
        Paint()
          ..color = accentColors[index % accentColors.length].withValues(
            alpha: 0.84,
          ),
      );
      canvas.drawPath(
        face,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.4
          ..color = Colors.white.withValues(alpha: 0.38),
      );
    }
    canvas.drawCircle(
      centerOffset,
      7,
      Paint()..color = Colors.white.withValues(alpha: 0.9),
    );
  }

  @override
  bool shouldRepaint(HarnessScenePainter oldDelegate) =>
      oldDelegate.progress != progress ||
      oldDelegate.paletteName != paletteName;
}
