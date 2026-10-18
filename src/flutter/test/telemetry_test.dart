import 'dart:convert';
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('span preserves parent identity, monotonic duration and idempotent end', () async {
    final events = <Map<String, dynamic>>[];
    final span = await AnsightSpan.begin('Load', 64, 'parent', (label, details, channel) async {
      expect(channel, 64);
      events.add(jsonDecode(details) as Map<String, dynamic>);
    });
    await span.end(status: 'error');
    await span.end();
    expect(events.length, 2);
    expect(events.last['spanId'], span.id);
    expect(events.last['parentSpanId'], 'parent');
    expect(events.last['status'], 'error');
    expect(events.last['durationMs'] as num, greaterThanOrEqualTo(0));
    expect(AnsightRecordedMetric.fromJson({'value': 0.125, 'channel': 64}).value, .125);
  });
}
