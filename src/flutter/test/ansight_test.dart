import 'dart:typed_data';

import 'package:ansight_flutter/ansight.dart';
import 'package:flutter_test/flutter_test.dart';

class NativeCall {
  const NativeCall(this.method, this.arguments);

  final String method;
  final AnsightJson? arguments;
}

class FakeNativeTransport implements AnsightNativeTransport {
  final List<NativeCall> calls = <NativeCall>[];

  @override
  AnsightNativeEventCallback? eventCallback;

  @override
  AnsightNativeToolCallCallback? toolCallCallback;

  @override
  Future<AnsightJson> invoke(
    String method, [
    AnsightJson? arguments,
  ]) async {
    calls.add(NativeCall(method, arguments));
    if (<String>{
      'initialize',
      'registerMetricChannel',
      'recordMetric',
      'recordEvent',
    }.contains(method)) {
      return _snapshot;
    }
    if (method == 'hostConnectionStatus') {
      return _connection;
    }
    if (<String>{
      'registerCustomTool',
      'resolveToolCall',
      'enableFlutterVisualTreeProvider',
    }.contains(method)) {
      return <String, Object?>{
        'success': true,
        'message': 'ok',
      };
    }
    return <String, Object?>{'success': true, 'message': 'ok'};
  }

  @override
  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  }) async =>
      <String, Object?>{
        'success': true,
        'requestId': requestId,
        'sizeBytes': data.length,
      };

  static const AnsightJson _connection = <String, Object?>{
    'isRuntimeActive': true,
    'isConnected': false,
    'connectionState': 'disconnected',
    'hasCachedSession': false,
    'hasSavedConfig': false,
    'hasBundledConfig': false,
    'summaryKind': 'ready',
    'summaryMessage': 'Ready',
  };

  static const AnsightJson _snapshot = <String, Object?>{
    'initialized': true,
    'active': true,
    'sessionOpen': false,
    'metricsRecorded': 0,
    'eventsRecorded': 0,
    'registeredTools': 0,
    'channels': <Object?>[],
    'connectionStatus': _connection,
  };
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('cellular host connections are explicit and disabled by default', () {
    expect(
      const AnsightHostConnectionOptions().toJson()['allowCellularConnections'],
      isFalse,
    );

    final options =
        createOptionsBuilder().withCellularHostConnections().build();

    expect(
      options.toJson()['hostConnection'],
      containsPair('allowCellularConnections', true),
    );
  });

  test('runtime diagnostic tracking is opt-in', () {
    final defaults = AnsightOptions.developer().toJson();
    expect(defaults['enableOpenFileHandleTracking'], isFalse);
    expect(defaults['enableJniReferenceCountTracking'], isFalse);

    final enabled = createOptionsBuilder()
        .withOpenFileHandleTracking()
        .withJniReferenceCountTracking()
        .build()
        .toJson();
    expect(enabled['enableOpenFileHandleTracking'], isTrue);
    expect(enabled['enableJniReferenceCountTracking'], isTrue);

    final disabled = createOptionsBuilder()
        .withOpenFileHandleTracking()
        .withJniReferenceCountTracking()
        .withoutOpenFileHandleTracking()
        .withoutJniReferenceCountTracking()
        .build()
        .toJson();
    expect(disabled['enableOpenFileHandleTracking'], isFalse);
    expect(disabled['enableJniReferenceCountTracking'], isFalse);
  });

  test('serializes touch-triggered visual-tree capture mode', () {
    final options = const AnsightSessionJpegCaptureOptions(
      mode: AnsightSessionJpegCaptureMode.screenshotWithVisualTreeOnTouch,
      captureKeyboardPresence: true,
    ).toJson();

    expect(options['mode'], 'screenshotWithVisualTreeOnTouch');
    expect(options['captureKeyboardPresence'], isTrue);
  });

  test('initializes and records typed telemetry', () async {
    final transport = FakeNativeTransport();
    final ansight = Ansight.withTransport(transport);

    final snapshot = await ansight.initialize(
      const AnsightOptions(
        sampleFrequencyMilliseconds: 250,
        customProperties: <String, Map<String, String>>{
          'flutter': <String, String>{'buildMode': 'caller'},
          'localization': <String, String>{'language': 'fr'},
        },
      ),
    );
    await ansight.metric(12.6, channel: 40);
    await ansight.event(
      'route changed',
      type: AnsightEventType.navigation,
      channel: 7,
    );

    expect(snapshot.initialized, isTrue);
    expect(transport.calls[0].method, 'initialize');
    expect(transport.calls[0].arguments?['sampleFrequencyMilliseconds'], 250);
    final automaticProperties = transport
        .calls[0].arguments?['customProperties'] as Map<Object?, Object?>;
    expect(
      automaticProperties['flutter'] as Map<Object?, Object?>,
      containsPair('sdkVersion', isNotEmpty),
    );
    expect(
      automaticProperties['flutter'] as Map<Object?, Object?>,
      containsPair('dartVersion', isNotEmpty),
    );
    expect(
      automaticProperties['flutter'] as Map<Object?, Object?>,
      containsPair('buildMode', 'caller'),
    );
    expect(
      automaticProperties['localization'] as Map<Object?, Object?>,
      containsPair('language', 'fr'),
    );
    expect(
      automaticProperties['localization'] as Map<Object?, Object?>,
      containsPair('utcOffsetMinutes', isNotEmpty),
    );
    expect(transport.calls[2].method, 'recordMetric');
    expect(transport.calls[2].arguments, <String, Object?>{
      'value': 13,
      'channel': 40,
    });
  });

  test('automatic properties survive updates, clears, and removals', () async {
    final transport = FakeNativeTransport();
    final ansight = Ansight.withTransport(transport);

    await ansight.updateSessionProperties(<String, Map<String, String>>{
      'flutter': <String, String>{'buildMode': 'caller'},
      'app': <String, String>{'tenant': 'acme'},
    });
    await ansight.clearSessionProperties();
    await ansight.removeCustomProperty('flutter', 'sdkVersion');
    await ansight.removeCustomProperty('app', 'tenant');

    final updated =
        transport.calls[0].arguments?['properties'] as Map<Object?, Object?>;
    expect(
      updated['flutter'] as Map<Object?, Object?>,
      containsPair('buildMode', 'caller'),
    );
    expect(updated, containsPair('app', <String, String>{'tenant': 'acme'}));

    expect(transport.calls[1].method, 'updateSessionProperties');
    final cleared =
        transport.calls[1].arguments?['properties'] as Map<Object?, Object?>;
    expect(cleared, contains('flutter'));
    expect(cleared, contains('localization'));

    expect(transport.calls[2].method, 'registerCustomProperty');
    expect(
      transport.calls[2].arguments,
      containsPair('value', isNotEmpty),
    );
    expect(transport.calls[3].method, 'removeCustomProperty');
  });

  test('dispatches native custom-tool calls back to Dart', () async {
    final transport = FakeNativeTransport();
    final ansight = Ansight.withTransport(transport);
    await ansight.registerTool(
      const AnsightToolDefinition(id: 'app.echo', name: 'Echo'),
      (Map<String, String> arguments, AnsightToolContext context) async =>
          AnsightToolResult.success(result: arguments),
    );

    transport.toolCallCallback?.call(<String, Object?>{
      'requestId': 'request-1',
      'toolId': 'app.echo',
      'platform': 'test',
      'arguments': <String, Object?>{'value': 'hello'},
    });
    await Future<void>.delayed(Duration.zero);

    final resolution = transport.calls.last;
    expect(resolution.method, 'resolveToolCall');
    expect(
      (resolution.arguments?['result'] as AnsightJson)['result'],
      <String, Object?>{'value': 'hello'},
    );
  });

  test('encodes text artifacts as UTF-8', () {
    final payload = AnsightArtifactPayload.text('こんにちは');
    expect(payload.bytes.length, greaterThan(5));
  });

  test('configures crash capture and records framework context', () async {
    final options = createOptionsBuilder()
        .withCrashCapture(
          const AnsightCrashCaptureOptions(
            maximumPendingReports: 12,
            retentionDays: 3,
          ),
        )
        .build();
    expect(options.toJson()['crashCapture'], containsPair('enabled', true));
    expect(
      (options.toJson()['crashCapture']
          as AnsightJson)['maximumPendingReports'],
      12,
    );
    expect(
      createOptionsBuilder().withoutCrashCapture().build().toJson(),
      containsPair('crashCapture', false),
    );

    final transport = FakeNativeTransport();
    await Ansight.withTransport(transport).recordCrashCandidate(
      message: 'render failed',
      stack: 'stack',
      metadata: const <String, String>{'library': 'widgets'},
    );
    expect(transport.calls.single.method, 'recordCrashCandidate');
    expect(transport.calls.single.arguments, containsPair('fatal', false));
  });
}
