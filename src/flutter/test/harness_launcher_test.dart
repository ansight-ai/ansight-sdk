import 'package:flutter_test/flutter_test.dart';

import '../tool/run_harness.dart';

void main() {
  group('buildHarnessPairingDocument', () {
    test('wraps a signed public config without modifying it', () {
      final config = <String, Object?>{
        'schema': 'ansight.pairing-config.v1',
        'configId': 'config-1',
        'signature': 'signed-value',
      };

      final document = buildHarnessPairingDocument(
        config,
        hostAddress: '192.168.1.20',
        discoveryPort: 45123,
      );

      expect(document['schema'], 'ansight.pairing-config-document.v1');
      expect(document['config'], same(config));
      expect(
        document['config'],
        containsPair('signature', 'signed-value'),
      );
      expect(
        document['discovery'],
        containsPair('hostAddresses', <String>['192.168.1.20']),
      );
      expect(document['discovery'], containsPair('discoveryPort', 45123));
      expect(
        document['discovery'],
        containsPair('source', 'flutter-harness-launcher'),
      );
    });

    test('adds the selected host first and preserves existing hints', () {
      final document = <String, Object?>{
        'schema': 'ansight.pairing-config-document.v1',
        'config': <String, Object?>{
          'schema': 'ansight.pairing-config.v1',
          'configId': 'config-2',
        },
        'discovery': <String, Object?>{
          'hostAddresses': <Object?>['10.0.0.5', '192.168.1.20'],
          'wifiName': 'Harness WiFi',
        },
      };

      final result = buildHarnessPairingDocument(
        document,
        hostAddress: '192.168.1.20',
        discoveryPort: 45124,
      );

      expect(
        result['discovery'],
        containsPair(
          'hostAddresses',
          <String>['192.168.1.20', '10.0.0.5'],
        ),
      );
      expect(result['discovery'], containsPair('wifiName', 'Harness WiFi'));
      expect(result['discovery'], containsPair('discoveryPort', 45124));
    });

    test('rejects unsupported input schemas', () {
      expect(
        () => buildHarnessPairingDocument(
          <String, Object?>{'schema': 'unknown'},
          hostAddress: '192.168.1.20',
          discoveryPort: 45123,
        ),
        throwsFormatException,
      );
    });
  });
}
