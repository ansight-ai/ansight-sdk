import 'dart:async';
import 'dart:typed_data';

import 'package:ansight_flutter/ansight.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class NativeCall {
  const NativeCall(this.method, this.arguments);

  final String method;
  final AnsightJson? arguments;
}

class FakeNativeTransport implements AnsightNativeTransport {
  final List<NativeCall> calls = <NativeCall>[];
  bool connected = false;

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
      return <String, Object?>{
        ..._connection,
        'isConnected': connected,
        'connectionState': connected ? 'connected' : 'disconnected',
      };
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

  @override
  Future<AnsightJson> recordNetworkRequest(
    AnsightNetworkRequest request,
  ) async {
    calls.add(NativeCall('recordNetworkRequest', <String, Object?>{
      'request': request,
    }));
    return <String, Object?>{'success': true, 'message': 'ok'};
  }

  void emitConnectionStatus(bool isConnected) {
    connected = isConnected;
    eventCallback?.call('connectionStatus', <String, Object?>{
      ..._connection,
      'isConnected': isConnected,
      'connectionState': isConnected ? 'connected' : 'disconnected',
    });
  }

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

class StreamingClient extends http.BaseClient {
  StreamingClient(this.callback);

  final Future<http.StreamedResponse> Function(http.BaseRequest request)
      callback;

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) =>
      callback(request);
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

  test('network sanitizer reapplies mandatory credential redaction', () {
    final request = AnsightNetworkRequestSanitizer.sanitize(
      const AnsightNetworkRequest(
        id: 'request-1',
        source: 'test',
        startedAtUtc: '2026-08-23T00:00:00.000Z',
        completedAtUtc: '2026-08-23T00:00:00.010Z',
        durationMilliseconds: 10,
        method: 'get',
        url:
            'https://user:password@example.test/items?token=secret&visible=yes',
        requestHeaders: <AnsightNetworkHeader>[
          AnsightNetworkHeader(name: 'Authorization', value: 'Bearer first'),
        ],
        errorMessage:
            'request failed token=secret at https://user:password@example.test?api_key=secret',
      ),
      AnsightNetworkSanitizationOptions(
        requestSanitizer: (AnsightNetworkRequest value) =>
            AnsightNetworkRequest(
          id: value.id,
          source: value.source,
          startedAtUtc: value.startedAtUtc,
          completedAtUtc: value.completedAtUtc,
          durationMilliseconds: value.durationMilliseconds,
          method: value.method,
          url: value.url,
          requestHeaders: const <AnsightNetworkHeader>[
            AnsightNetworkHeader(
              name: 'Authorization',
              value: 'Bearer restored',
            ),
          ],
        ),
      ),
    );

    expect(request, isNotNull);
    expect(request!.method, 'GET');
    expect(request.url, contains('token=%3Credacted%3E'));
    expect(request.url, isNot(contains('password')));
    expect(request.requestHeaders.single.value, ansightRedactedNetworkValue);
    expect(request.errorMessage, isNot(contains('secret')));
    expect(request.errorMessage, isNot(contains('password')));
  });

  test('network sanitizer redacts cloud signed URLs and text bodies', () {
    for (final url in <String>[
      'https://blob.test/a?sv=1&sp=rw&se=tomorrow&sig=azure-secret&safe=yes',
      'https://s3.test/a?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=credential-secret&X-Amz-Signature=aws-secret&safe=yes',
      'https://storage.test/a?X-Goog-Algorithm=GOOG4-RSA-SHA256&X-Goog-Credential=google-secret&X-Goog-Signature=gcs-secret&safe=yes',
    ]) {
      final request = AnsightNetworkRequestSanitizer.sanitize(
        AnsightNetworkRequest(
          id: 'cloud',
          source: 'test',
          startedAtUtc: '2026-08-23T00:00:00Z',
          completedAtUtc: '2026-08-23T00:00:01Z',
          durationMilliseconds: 1,
          method: 'POST',
          url: url,
          requestBody: const AnsightNetworkBody(
            contentType: 'application/json',
            encoding: 'utf8',
            data: '{"token":"body-secret","visible":"yes"}',
            capturedBytes: 39,
            totalBytes: 39,
            truncated: false,
          ),
        ),
      );
      expect(
          request!.url,
          isNot(matches(RegExp(
            'azure-secret|credential-secret|aws-secret|google-secret|gcs-secret',
          ))));
      expect(request.url, contains('safe=yes'));
      expect(request.requestBody!.data, isNot(contains('body-secret')));
      expect(request.requestBody!.data, contains('visible'));
    }
  });

  test('AnsightHttpClient captures bodies only while the host is connected',
      () async {
    final transport = FakeNativeTransport();
    transport.connected = true;
    final ansight = Ansight.withTransport(transport);
    final inner = MockClient((http.Request request) async => http.Response(
          'response body',
          201,
          reasonPhrase: 'Created',
          headers: <String, String>{
            'content-length': '13',
            'set-cookie': 'session=secret',
          },
          request: request,
        ));
    final client = AnsightHttpClient(inner: inner, ansight: ansight);
    await Future<void>.delayed(Duration.zero);

    final response = await client.post(
      Uri.parse('https://example.test/items?token=secret'),
      headers: <String, String>{'Authorization': 'Bearer secret'},
      body: 'request body',
    );
    await Future<void>.delayed(Duration.zero);

    expect(response.statusCode, 201);
    final networkCall = transport.calls.singleWhere(
      (NativeCall call) => call.method == 'recordNetworkRequest',
    );
    final captured = networkCall.arguments!['request'] as AnsightNetworkRequest;
    expect(captured.source, 'flutter.http');
    expect(captured.method, 'POST');
    expect(captured.statusCode, 201);
    expect(captured.requestBody?.data, 'request body');
    expect(captured.responseBody?.data, 'response body');
    expect(captured.url, contains('token=%3Credacted%3E'));
    expect(
      captured.requestHeaders
          .singleWhere((AnsightNetworkHeader value) =>
              value.name.toLowerCase() == 'authorization')
          .value,
      ansightRedactedNetworkValue,
    );
    client.close();
  });

  test('AnsightHttpClient stops an in-flight capture after disconnect',
      () async {
    final transport = FakeNativeTransport()..connected = true;
    final ansight = Ansight.withTransport(transport);
    final responseStream = StreamController<List<int>>();
    final inner = StreamingClient(
        (http.BaseRequest request) async => http.StreamedResponse(
              responseStream.stream,
              200,
              contentLength: 13,
              headers: <String, String>{'content-type': 'text/plain'},
              request: request,
            ));
    final client = AnsightHttpClient(inner: inner, ansight: ansight);
    await Future<void>.delayed(Duration.zero);

    final response = await client.send(
      http.Request('GET', Uri.parse('https://example.test/items')),
    );
    transport.emitConnectionStatus(false);
    final responseComplete = response.stream.drain<void>();
    responseStream.add('response body'.codeUnits);
    await responseStream.close();
    await responseComplete;
    await Future<void>.delayed(Duration.zero);

    expect(
      transport.calls.where(
        (NativeCall call) => call.method == 'recordNetworkRequest',
      ),
      isEmpty,
    );
    client.close();
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

  test('host handoff serializes explicit delivery settings', () {
    for (final enabled in [false, true]) {
      final options = createOptionsBuilder()
          .withCrashCapture(AnsightCrashCaptureOptions(hostHandoffEnabled: enabled))
          .build()
          .toJson();
      expect((options['crashCapture'] as Map)['hostHandoffEnabled'], enabled);
    }
    expect(const AnsightCrashCaptureOptions().hostHandoffEnabled, isTrue);
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
