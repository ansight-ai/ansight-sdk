import 'package:flutter_test/flutter_test.dart';

import '../tool/run_harness.dart';

void main() {
  group('buildHarnessPairingDocument', () {
    test('wraps an enrollment invite without modifying it', () {
      final config = <String, Object?>{
        'schema': 'ansight.enrollment-invite.v2',
        'inviteId': 'invite-1',
        'enrollment': <String, Object?>{'accessToken': 'token-value'},
      };

      final document = buildHarnessPairingDocument(
        config,
        hostAddress: '192.168.1.20',
        discoveryPort: 45123,
      );

      expect(document['schema'], 'ansight.enrollment-invite-document.v2');
      expect(document['invite'], same(config));
      expect(
        document['invite'],
        containsPair('inviteId', 'invite-1'),
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
        'schema': 'ansight.enrollment-invite-document.v2',
        'invite': <String, Object?>{
          'schema': 'ansight.enrollment-invite.v2',
          'inviteId': 'invite-2',
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
