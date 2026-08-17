import 'dart:math' as math;

import 'package:ansight_flutter/ansight.dart';

const String ansightLocationEventType = 'CLIENT_LOCATION';
const String ansightLocationSchema = 'ansight.location.sample.v1';

class AnsightLocationOptions {
  const AnsightLocationOptions({
    this.enabled = false,
    this.decimalPlaces = 5,
    this.minimumInterval = const Duration(seconds: 1),
    this.minimumDistanceMeters = 1,
  });

  final bool enabled;
  final int decimalPlaces;
  final Duration minimumInterval;
  final double minimumDistanceMeters;
}

class AnsightLocationSample {
  const AnsightLocationSample({
    required this.latitude,
    required this.longitude,
    this.altitudeMeters,
    this.horizontalAccuracyMeters,
    this.verticalAccuracyMeters,
    this.speedMetersPerSecond,
    this.headingDegrees,
    this.capturedAt,
    this.sampleId,
    this.correlationId,
    this.runId,
  });

  final double latitude;
  final double longitude;
  final double? altitudeMeters;
  final double? horizontalAccuracyMeters;
  final double? verticalAccuracyMeters;
  final double? speedMetersPerSecond;
  final double? headingDegrees;
  final DateTime? capturedAt;
  final String? sampleId;
  final String? correlationId;
  final String? runId;
}

class AnsightLocationRecorder {
  AnsightLocationRecorder({
    Ansight? runtime,
    this.options = const AnsightLocationOptions(),
  }) : runtime = runtime ?? Ansight.instance;

  final Ansight runtime;
  final AnsightLocationOptions options;
  AnsightLocationSample? _lastSample;

  Future<AnsightOperationResult> record(AnsightLocationSample sample) async {
    if (!options.enabled) {
      return const AnsightOperationResult(
        success: false,
        message: 'Observed location capture is disabled.',
      );
    }
    if (!sample.latitude.isFinite || sample.latitude < -90 || sample.latitude > 90 ||
        !sample.longitude.isFinite || sample.longitude < -180 || sample.longitude > 180) {
      throw RangeError('Observed location coordinates are invalid.');
    }

    final normalized = _normalize(sample);
    final previous = _lastSample;
    if (previous != null && _shouldSuppress(previous, normalized)) {
      return const AnsightOperationResult(
        success: true,
        message: 'Observed location suppressed by sampling controls.',
      );
    }
    _lastSample = normalized;
    return runtime.sendSessionEvent(ansightLocationEventType, <String, Object?>{
      'schema': ansightLocationSchema,
      'sampleId': normalized.sampleId,
      'capturedAtUtc': normalized.capturedAt!.toUtc().toIso8601String(),
      'source': 'app_observed',
      'latitude': normalized.latitude,
      'longitude': normalized.longitude,
      if (normalized.altitudeMeters != null) 'altitudeMeters': normalized.altitudeMeters,
      if (normalized.horizontalAccuracyMeters != null) 'horizontalAccuracyMeters': normalized.horizontalAccuracyMeters,
      if (normalized.verticalAccuracyMeters != null) 'verticalAccuracyMeters': normalized.verticalAccuracyMeters,
      if (normalized.speedMetersPerSecond != null) 'speedMetersPerSecond': normalized.speedMetersPerSecond,
      if (normalized.headingDegrees != null) 'headingDegrees': normalized.headingDegrees,
      if (normalized.correlationId != null) 'correlationId': normalized.correlationId,
      if (normalized.runId != null) 'runId': normalized.runId,
    });
  }

  Future<AnsightOperationResult> recordCoordinates({
    required double latitude,
    required double longitude,
    double? altitudeMeters,
    double? horizontalAccuracyMeters,
    double? verticalAccuracyMeters,
    double? speedMetersPerSecond,
    double? headingDegrees,
    DateTime? capturedAt,
    String? correlationId,
    String? runId,
  }) => record(AnsightLocationSample(
    latitude: latitude,
    longitude: longitude,
    altitudeMeters: altitudeMeters,
    horizontalAccuracyMeters: horizontalAccuracyMeters,
    verticalAccuracyMeters: verticalAccuracyMeters,
    speedMetersPerSecond: speedMetersPerSecond,
    headingDegrees: headingDegrees,
    capturedAt: capturedAt,
    correlationId: correlationId,
    runId: runId,
  ));

  AnsightLocationSample _normalize(AnsightLocationSample sample) {
    final places = options.decimalPlaces.clamp(0, 7);
    final scale = math.pow(10, places).toDouble();
    return AnsightLocationSample(
      latitude: (sample.latitude * scale).round() / scale,
      longitude: (sample.longitude * scale).round() / scale,
      altitudeMeters: _finite(sample.altitudeMeters),
      horizontalAccuracyMeters: _nonNegative(sample.horizontalAccuracyMeters),
      verticalAccuracyMeters: _nonNegative(sample.verticalAccuracyMeters),
      speedMetersPerSecond: _nonNegative(sample.speedMetersPerSecond),
      headingDegrees: _finite(sample.headingDegrees),
      capturedAt: sample.capturedAt ?? DateTime.now().toUtc(),
      sampleId: _text(sample.sampleId) ?? '${DateTime.now().microsecondsSinceEpoch}',
      correlationId: _text(sample.correlationId),
      runId: _text(sample.runId),
    );
  }

  bool _shouldSuppress(AnsightLocationSample previous, AnsightLocationSample current) =>
      current.capturedAt!.difference(previous.capturedAt!) < options.minimumInterval ||
      _distance(previous, current) < math.max(0, options.minimumDistanceMeters);

  double _distance(AnsightLocationSample first, AnsightLocationSample second) {
    double radians(double value) => value * math.pi / 180;
    final latitudeDelta = radians(second.latitude - first.latitude);
    final longitudeDelta = radians(second.longitude - first.longitude);
    final haversine = math.pow(math.sin(latitudeDelta / 2), 2) +
        math.cos(radians(first.latitude)) * math.cos(radians(second.latitude)) *
            math.pow(math.sin(longitudeDelta / 2), 2);
    return 12742000 * math.asin(math.sqrt(haversine));
  }

  double? _finite(double? value) => value?.isFinite == true ? value : null;
  double? _nonNegative(double? value) => value?.isFinite == true && value! >= 0 ? value : null;
  String? _text(String? value) => value?.trim().isNotEmpty == true ? value!.trim() : null;
}
