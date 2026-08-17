import 'dart:typed_data';

import 'package:ansight_flutter/ansight.dart';
import 'package:ansight_location/ansight_location.dart';
import 'package:test/test.dart';

class LocationTestTransport implements AnsightNativeTransport {
  final List<MapEntry<String, AnsightJson?>> calls =
      <MapEntry<String, AnsightJson?>>[];

  @override
  AnsightNativeEventCallback? eventCallback;

  @override
  AnsightNativeToolCallCallback? toolCallCallback;

  @override
  Future<AnsightJson> invoke(String method, [AnsightJson? arguments]) async {
    calls.add(MapEntry<String, AnsightJson?>(method, arguments));
    return <String, Object?>{'success': true, 'message': 'sent'};
  }

  @override
  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  }) async =>
      <String, Object?>{'success': true};
}

void main() {
  test('capture is disabled by default', () async {
    final transport = LocationTestTransport();
    final recorder = AnsightLocationRecorder(
      runtime: Ansight.withTransport(transport),
    );

    final result = await recorder.record(
      const AnsightLocationSample(latitude: -33.8688, longitude: 151.2093),
    );

    expect(result.success, isFalse);
    expect(transport.calls, isEmpty);
  });

  test('enabled capture emits through the supplied existing runtime', () async {
    final transport = LocationTestTransport();
    final recorder = AnsightLocationRecorder(
      runtime: Ansight.withTransport(transport),
      options: const AnsightLocationOptions(
        enabled: true,
        decimalPlaces: 3,
        minimumInterval: Duration.zero,
        minimumDistanceMeters: 0,
      ),
    );

    final result = await recorder.record(AnsightLocationSample(
      latitude: -33.868812,
      longitude: 151.209319,
      capturedAt: DateTime.parse('2026-08-17T01:00:00Z'),
      sampleId: 'sample-1',
      correlationId: 'command-1',
      runId: 'run-1',
    ));

    expect(result.success, isTrue);
    final call = transport.calls.singleWhere(
      (entry) => entry.key == 'sendSessionEvent',
    );
    expect(call.value?['type'], ansightLocationEventType);
    final payload = call.value?['payload'] as AnsightJson;
    expect(payload['source'], 'app_observed');
    expect(payload['latitude'], -33.869);
    expect(payload['longitude'], 151.209);
    expect(payload['correlationId'], 'command-1');
    expect(payload['runId'], 'run-1');
  });
}
