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

  test('initializes and records typed telemetry', () async {
    final transport = FakeNativeTransport();
    final ansight = Ansight.withTransport(transport);

    final snapshot = await ansight.initialize(
      const AnsightOptions(sampleFrequencyMilliseconds: 250),
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
    expect(transport.calls[2].method, 'recordMetric');
    expect(transport.calls[2].arguments, <String, Object?>{
      'value': 13,
      'channel': 40,
    });
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
}
