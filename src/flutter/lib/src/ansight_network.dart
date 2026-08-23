import 'dart:async';
import 'dart:io';

import 'package:http/http.dart' as http;

import 'ansight_network_models.dart';
import 'ansight_runtime.dart';

/// Explicitly enabled network-capturing wrapper for package:http clients.
class AnsightHttpClient extends http.BaseClient {
  AnsightHttpClient({
    http.Client? inner,
    Ansight? ansight,
    this.sanitizationOptions = const AnsightNetworkSanitizationOptions(),
  })  : _inner = inner ?? http.Client(),
        _ansight = ansight ?? Ansight.instance {
    _connectionSubscription = _ansight.connectionStatusChanges.listen(
      (status) => _hostConnected = status.isConnected,
    );
    unawaited(_ansight.hostConnectionStatus().then(
          (status) => _hostConnected = status.isConnected,
          onError: (_) => _hostConnected = false,
        ));
  }

  final http.Client _inner;
  final Ansight _ansight;
  late final StreamSubscription _connectionSubscription;
  bool _hostConnected = false;
  final AnsightNetworkSanitizationOptions sanitizationOptions;

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (!_hostConnected) return _inner.send(request);

    final startedAtUtc = DateTime.now().toUtc();
    final stopwatch = Stopwatch()..start();
    final requestBody = _requestBody(request);
    try {
      final response = await _inner.send(request);
      if (!_hostConnected) return response;
      if (!sanitizationOptions.captureResponseBody) {
        stopwatch.stop();
        _capture(_record(
          request,
          startedAtUtc,
          stopwatch.elapsedMicroseconds / 1000,
          requestBody: requestBody,
          response: response,
        ));
        return response;
      }

      final maximum = AnsightNetworkRequestSanitizer.maximumBodyBytes(
        sanitizationOptions,
      );
      final captured = _BodyCaptureAccumulator(maximum);
      var observedBytes = 0;
      var captureActive = _hostConnected;
      Object? responseError;
      Stream<List<int>> captureStream() async* {
        try {
          await for (final chunk in response.stream) {
            observedBytes += chunk.length;
            if (captureActive && _hostConnected) {
              await captured.add(chunk);
            } else if (captureActive) {
              captureActive = false;
              await captured.discard();
            }
            yield chunk;
          }
        } catch (error) {
          responseError = error;
          rethrow;
        } finally {
          stopwatch.stop();
          if (!captureActive || !_hostConnected) {
            await captured.discard();
          } else {
            final capturedBytes = await captured.complete();
            final responseBody = AnsightNetworkRequestSanitizer.createBody(
              capturedBytes,
              totalBytes: response.contentLength ?? observedBytes,
              contentType: _header(response.headers, 'content-type'),
              options: sanitizationOptions,
            );
            _capture(_record(
              request,
              startedAtUtc,
              stopwatch.elapsedMicroseconds / 1000,
              requestBody: requestBody,
              response: response,
              responseBody: responseBody,
              error: responseError,
            ));
          }
        }
      }

      return http.StreamedResponse(
        captureStream(),
        response.statusCode,
        contentLength: response.contentLength,
        request: response.request,
        headers: response.headers,
        isRedirect: response.isRedirect,
        persistentConnection: response.persistentConnection,
        reasonPhrase: response.reasonPhrase,
      );
    } catch (error) {
      stopwatch.stop();
      _capture(
        _record(
          request,
          startedAtUtc,
          stopwatch.elapsedMicroseconds / 1000,
          requestBody: requestBody,
          error: error,
        ),
      );
      rethrow;
    }
  }

  @override
  void close() {
    unawaited(_connectionSubscription.cancel());
    _inner.close();
  }

  AnsightNetworkRequest _record(
    http.BaseRequest request,
    DateTime startedAtUtc,
    double durationMilliseconds, {
    AnsightNetworkBody? requestBody,
    http.StreamedResponse? response,
    AnsightNetworkBody? responseBody,
    Object? error,
  }) =>
      AnsightNetworkRequest(
        id: '${startedAtUtc.microsecondsSinceEpoch.toRadixString(36)}${identityHashCode(request).toRadixString(36)}',
        source: 'flutter.http',
        startedAtUtc: startedAtUtc.toIso8601String(),
        completedAtUtc: DateTime.now().toUtc().toIso8601String(),
        durationMilliseconds: durationMilliseconds,
        method: request.method,
        url: request.url.toString(),
        requestHeaders: _headers(request.headers),
        requestBodySizeBytes: request.contentLength,
        requestBody: requestBody,
        statusCode: response?.statusCode,
        reasonPhrase: response?.reasonPhrase,
        responseHeaders: _headers(response?.headers),
        responseBodySizeBytes: response?.contentLength,
        responseBody: responseBody,
        errorType: error?.runtimeType.toString(),
        errorMessage: error?.toString(),
      );

  AnsightNetworkBody? _requestBody(http.BaseRequest request) {
    if (!sanitizationOptions.captureRequestBody || request is! http.Request) {
      return null;
    }
    return AnsightNetworkRequestSanitizer.createBody(
      request.bodyBytes,
      totalBytes: request.bodyBytes.length,
      contentType: _header(request.headers, 'content-type'),
      options: sanitizationOptions,
    );
  }

  static String? _header(Map<String, String> headers, String name) {
    for (final entry in headers.entries) {
      if (entry.key.toLowerCase() == name) return entry.value;
    }
    return null;
  }

  void _capture(AnsightNetworkRequest request) {
    if (!_hostConnected) return;
    _ansight
        .recordNetworkRequest(request, sanitizationOptions)
        .then<void>((_) {}, onError: (_) {});
  }

  static List<AnsightNetworkHeader> _headers(Map<String, String>? values) =>
      (values ?? const <String, String>{})
          .entries
          .map(
            (MapEntry<String, String> entry) => AnsightNetworkHeader(
              name: entry.key,
              value: entry.value,
            ),
          )
          .toList(growable: false);
}

class _BodyCaptureAccumulator {
  _BodyCaptureAccumulator(this.maximumBytes);

  static const int _memoryThresholdBytes = 1024 * 1024;

  final int maximumBytes;
  final List<int> memory = <int>[];
  RandomAccessFile? file;
  File? temporaryFile;
  int capturedBytes = 0;

  Future<void> add(List<int> chunk) async {
    final remaining = maximumBytes - capturedBytes;
    if (remaining <= 0) return;
    final kept = chunk.take(remaining).toList(growable: false);
    if (file == null && capturedBytes + kept.length > _memoryThresholdBytes) {
      final directory =
          await Directory.systemTemp.createTemp('ansight-network-');
      temporaryFile = File('${directory.path}/body.bin');
      file = await temporaryFile!.open(mode: FileMode.write);
      await file!.writeFrom(memory);
      memory.clear();
    }
    final output = file;
    if (output != null) {
      await output.writeFrom(kept);
    } else {
      memory.addAll(kept);
    }
    capturedBytes += kept.length;
  }

  Future<List<int>> complete() async {
    final output = file;
    if (output == null) return memory;
    await output.flush();
    await output.close();
    file = null;
    final value = await temporaryFile!.readAsBytes();
    final directory = temporaryFile!.parent;
    try {
      await temporaryFile!.delete();
      await directory.delete();
    } catch (_) {
      // Temporary capture cleanup is best-effort.
    }
    return value;
  }

  Future<void> discard() async {
    memory.clear();
    final output = file;
    file = null;
    if (output != null) {
      try {
        await output.close();
      } catch (_) {
        // Temporary capture cleanup is best-effort.
      }
    }
    final capturedFile = temporaryFile;
    temporaryFile = null;
    if (capturedFile != null) {
      final directory = capturedFile.parent;
      try {
        if (await capturedFile.exists()) await capturedFile.delete();
        if (await directory.exists()) await directory.delete();
      } catch (_) {
        // Temporary capture cleanup is best-effort.
      }
    }
  }
}
