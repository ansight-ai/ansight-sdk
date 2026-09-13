import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:ansight_flutter/ansight.dart';

void main() {
  test(
      'purchase facade delegates to native core and preserves missing evidence',
      () async {
    final calls = <Map<String, Object?>>[];
    final purchases = PurchaseDiagnostics((method, arguments) async {
      expect(method, 'purchaseCommand');
      calls.add(Map<String, Object?>.from(
          jsonDecode(arguments!['json']! as String) as Map));
      return <String, Object?>{'json': '{"status":"inconclusive"}'};
    });
    await purchases.record(const PurchaseObservation(
        productId: 'premium', source: 'app', entitled: false));
    final report = await purchases.validate(
        productId: 'premium',
        expectedEntitled: true,
        requireBackendVerification: true);
    expect(calls[0]['action'], 'record');
    expect(calls[1]['action'], 'purchases.validate');
    expect(report['status'], 'inconclusive');
  });
}
