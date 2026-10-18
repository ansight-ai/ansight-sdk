import 'dart:convert';
import 'dart:math';

/// A timed operation (zone). An unfinished span remains incomplete in recordings.
class AnsightSpan {
  AnsightSpan._(this.id, this.name, this.channel, this.parentSpanId, this._emit)
      : _startedAtUtc = DateTime.now().toUtc().toIso8601String();

  final String id;
  final String name;
  final int channel;
  final String? parentSpanId;
  final Future<void> Function(String, String, int) _emit;
  final String _startedAtUtc;
  final Stopwatch _clock = Stopwatch()..start();
  Future<void>? _completion;

  static Future<AnsightSpan> begin(String name, int channel, String? parentSpanId,
      Future<void> Function(String, String, int) emit) async {
    if (name.trim().isEmpty) throw ArgumentError('Span name must not be blank.');
    if (channel < 0 || channel > 255) throw RangeError('Channel ids must be between 0 and 255.');
    final random = Random.secure();
    final id = List.generate(16, (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
    final span = AnsightSpan._(id, name.trim(), channel, parentSpanId, emit);
    await span._publish('start');
    return span;
  }

  Future<void> end({String status = 'ok'}) {
    if (!const ['ok', 'error', 'cancelled'].contains(status)) {
      throw ArgumentError('Span status must be ok, error, or cancelled.');
    }
    return _completion ??= _publish('end', durationMs: _clock.elapsedMicroseconds / 1000.0, status: status);
  }

  Future<void> _publish(String phase, {double? durationMs, String? status}) => _emit(name, jsonEncode({
    'spanId': id, if (parentSpanId != null) 'parentSpanId': parentSpanId,
    'phase': phase, 'startedAtUtc': _startedAtUtc,
    if (durationMs != null) 'durationMs': durationMs, if (status != null) 'status': status,
  }), channel);
}
