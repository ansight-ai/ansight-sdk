import 'dart:convert';
import 'dart:typed_data';

import 'ansight_models.dart';
import 'generated/ansight_messages.g.dart';

const String ansightNetworkRequestSchema = 'ansight.network-request.v1';
const String ansightRedactedNetworkValue = '<redacted>';

class AnsightNetworkHeader {
  const AnsightNetworkHeader({required this.name, required this.value});

  final String name;
  final String value;

  AnsightJson toJson() => <String, Object?>{'name': name, 'value': value};

  AnsightNetworkHeaderMessage toMessage() => AnsightNetworkHeaderMessage(
        name: name,
        value: value,
      );
}

class AnsightNetworkBody {
  const AnsightNetworkBody({
    this.contentType,
    required this.encoding,
    required this.data,
    required this.capturedBytes,
    this.totalBytes,
    required this.truncated,
  });

  final String? contentType;
  final String encoding;
  final String data;
  final int capturedBytes;
  final int? totalBytes;
  final bool truncated;

  AnsightJson toJson() => <String, Object?>{
        if (contentType != null) 'contentType': contentType,
        'encoding': encoding,
        'data': data,
        'capturedBytes': capturedBytes,
        if (totalBytes != null) 'totalBytes': totalBytes,
        'truncated': truncated,
      };

  AnsightNetworkBodyMessage toMessage() => AnsightNetworkBodyMessage(
        contentType: contentType,
        encoding: encoding,
        data: data,
        capturedBytes: capturedBytes,
        totalBytes: totalBytes,
        truncated: truncated,
      );
}

/// Captured data for one completed HTTP request.
/// Text bodies are captured by default with an app-configurable byte cap.
class AnsightNetworkRequest {
  const AnsightNetworkRequest({
    this.schema = ansightNetworkRequestSchema,
    required this.id,
    required this.source,
    required this.startedAtUtc,
    required this.completedAtUtc,
    required this.durationMilliseconds,
    required this.method,
    required this.url,
    this.protocol,
    this.requestHeaders = const <AnsightNetworkHeader>[],
    this.requestBodySizeBytes,
    this.requestBody,
    this.statusCode,
    this.reasonPhrase,
    this.responseHeaders = const <AnsightNetworkHeader>[],
    this.responseBodySizeBytes,
    this.responseBody,
    this.errorType,
    this.errorMessage,
  });

  final String schema;
  final String id;
  final String source;
  final String startedAtUtc;
  final String completedAtUtc;
  final double durationMilliseconds;
  final String method;
  final String url;
  final String? protocol;
  final List<AnsightNetworkHeader> requestHeaders;
  final int? requestBodySizeBytes;
  final AnsightNetworkBody? requestBody;
  final int? statusCode;
  final String? reasonPhrase;
  final List<AnsightNetworkHeader> responseHeaders;
  final int? responseBodySizeBytes;
  final AnsightNetworkBody? responseBody;
  final String? errorType;
  final String? errorMessage;

  AnsightJson toJson() => <String, Object?>{
        'schema': ansightNetworkRequestSchema,
        'id': id,
        'source': source,
        'startedAtUtc': startedAtUtc,
        'completedAtUtc': completedAtUtc,
        'durationMilliseconds': durationMilliseconds,
        'method': method,
        'url': url,
        if (protocol != null) 'protocol': protocol,
        'requestHeaders': requestHeaders
            .map((AnsightNetworkHeader header) => header.toJson())
            .toList(growable: false),
        if (requestBodySizeBytes != null)
          'requestBodySizeBytes': requestBodySizeBytes,
        if (requestBody != null) 'requestBody': requestBody!.toJson(),
        if (statusCode != null) 'statusCode': statusCode,
        if (reasonPhrase != null) 'reasonPhrase': reasonPhrase,
        'responseHeaders': responseHeaders
            .map((AnsightNetworkHeader header) => header.toJson())
            .toList(growable: false),
        if (responseBodySizeBytes != null)
          'responseBodySizeBytes': responseBodySizeBytes,
        if (responseBody != null) 'responseBody': responseBody!.toJson(),
        if (errorType != null) 'errorType': errorType,
        if (errorMessage != null) 'errorMessage': errorMessage,
      };

  AnsightNetworkRequestMessage toMessage() => AnsightNetworkRequestMessage(
        schema: ansightNetworkRequestSchema,
        id: id,
        source: source,
        startedAtUtc: startedAtUtc,
        completedAtUtc: completedAtUtc,
        durationMilliseconds: durationMilliseconds,
        method: method,
        url: url,
        protocolName: protocol,
        requestHeaders: requestHeaders
            .map((AnsightNetworkHeader header) => header.toMessage())
            .toList(growable: false),
        requestBodySizeBytes: requestBodySizeBytes,
        requestBody: requestBody?.toMessage(),
        statusCode: statusCode,
        reasonPhrase: reasonPhrase,
        responseHeaders: responseHeaders
            .map((AnsightNetworkHeader header) => header.toMessage())
            .toList(growable: false),
        responseBodySizeBytes: responseBodySizeBytes,
        responseBody: responseBody?.toMessage(),
        errorType: errorType,
        errorMessage: errorMessage,
      );
}

typedef AnsightUrlSanitizer = String Function(String url);
typedef AnsightNetworkRequestSanitizerCallback = AnsightNetworkRequest?
    Function(
  AnsightNetworkRequest request,
);

class AnsightNetworkSanitizationOptions {
  const AnsightNetworkSanitizationOptions({
    this.includeRequestHeaders = true,
    this.includeResponseHeaders = true,
    this.includeQueryString = true,
    this.includeBodySizes = true,
    this.captureRequestBody = true,
    this.captureResponseBody = true,
    this.maximumBodyBytes = 64 * 1024,
    this.captureBinaryBodies = false,
    this.additionalSensitiveHeaderNames = const <String>[],
    this.additionalSensitiveQueryParameterNames = const <String>[],
    this.urlSanitizer,
    this.requestSanitizer,
  });

  final bool includeRequestHeaders;
  final bool includeResponseHeaders;
  final bool includeQueryString;
  final bool includeBodySizes;
  final bool captureRequestBody;
  final bool captureResponseBody;
  final int maximumBodyBytes;
  final bool captureBinaryBodies;
  final List<String> additionalSensitiveHeaderNames;
  final List<String> additionalSensitiveQueryParameterNames;
  final AnsightUrlSanitizer? urlSanitizer;
  final AnsightNetworkRequestSanitizerCallback? requestSanitizer;
}

class AnsightNetworkSanitizationOptionsBuilder {
  AnsightNetworkSanitizationOptionsBuilder([
    AnsightNetworkSanitizationOptions options =
        const AnsightNetworkSanitizationOptions(),
  ])  : _captureRequestBody = options.captureRequestBody,
        _captureResponseBody = options.captureResponseBody,
        _maximumBodyBytes = options.maximumBodyBytes,
        _captureBinaryBodies = options.captureBinaryBodies,
        _base = options;

  final AnsightNetworkSanitizationOptions _base;
  bool _captureRequestBody;
  bool _captureResponseBody;
  int _maximumBodyBytes;
  bool _captureBinaryBodies;

  AnsightNetworkSanitizationOptionsBuilder withRequestBodies(
      [bool include = true]) {
    _captureRequestBody = include;
    return this;
  }

  AnsightNetworkSanitizationOptionsBuilder withoutRequestBodies() =>
      withRequestBodies(false);

  AnsightNetworkSanitizationOptionsBuilder withResponseBodies(
      [bool include = true]) {
    _captureResponseBody = include;
    return this;
  }

  AnsightNetworkSanitizationOptionsBuilder withoutResponseBodies() =>
      withResponseBodies(false);

  AnsightNetworkSanitizationOptionsBuilder withMaximumBodyBytes(int value) {
    _maximumBodyBytes = value;
    return this;
  }

  AnsightNetworkSanitizationOptionsBuilder withBinaryBodies(
      [bool include = true]) {
    _captureBinaryBodies = include;
    return this;
  }

  AnsightNetworkSanitizationOptions build() =>
      AnsightNetworkSanitizationOptions(
        includeRequestHeaders: _base.includeRequestHeaders,
        includeResponseHeaders: _base.includeResponseHeaders,
        includeQueryString: _base.includeQueryString,
        includeBodySizes: _base.includeBodySizes,
        captureRequestBody: _captureRequestBody,
        captureResponseBody: _captureResponseBody,
        maximumBodyBytes: _maximumBodyBytes,
        captureBinaryBodies: _captureBinaryBodies,
        additionalSensitiveHeaderNames: _base.additionalSensitiveHeaderNames,
        additionalSensitiveQueryParameterNames:
            _base.additionalSensitiveQueryParameterNames,
        urlSanitizer: _base.urlSanitizer,
        requestSanitizer: _base.requestSanitizer,
      );
}

class AnsightNetworkRequestSanitizer {
  const AnsightNetworkRequestSanitizer._();

  static const int _maximumHeaderCount = 128;
  static const int _maximumHeaderValueLength = 4096;
  static const int _maximumErrorMessageLength = 4096;
  static const int _maximumUrlLength = 16384;
  static const Set<String> _sensitiveHeaderNames = <String>{
    'authorization',
    'cookie',
    'proxy-authorization',
    'set-cookie',
    'x-api-key',
    'x-auth-token',
  };
  static const Set<String> _sensitiveQueryNames = <String>{
    'access_token',
    'accesskey',
    'access_key',
    'api_key',
    'apikey',
    'auth',
    'authorization',
    'client_secret',
    'code',
    'credential',
    'credentials',
    'id_token',
    'jwt',
    'key',
    'password',
    'passwd',
    'refresh_token',
    'sas',
    'sastoken',
    'secret',
    'secret_key',
    'security_token',
    'session_token',
    'sig',
    'signature',
    'token',
  };
  static const Set<String> _azureSasFingerprintNames = <String>{
    'se',
    'skoid',
    'sp',
    'sr',
    'srt',
    'ss',
    'sv'
  };
  static const Set<String> _azureSasQueryNames = <String>{
    'epk',
    'erk',
    'rscc',
    'rscd',
    'rsce',
    'rscl',
    'rsct',
    'saoid',
    'scid',
    'se',
    'sig',
    'si',
    'sip',
    'ske',
    'skoid',
    'sks',
    'skt',
    'sktid',
    'skv',
    'snapshot',
    'sp',
    'spk',
    'spr',
    'sr',
    'srk',
    'srt',
    'ss',
    'st',
    'suoid',
    'tn',
    'versionid',
    'sv',
  };

  static AnsightNetworkRequest? sanitize(
    AnsightNetworkRequest request, [
    AnsightNetworkSanitizationOptions options =
        const AnsightNetworkSanitizationOptions(),
  ]) {
    try {
      var normalized = _normalize(request, options);
      if (options.urlSanitizer != null) {
        normalized = _normalize(
          _replace(normalized, url: options.urlSanitizer!(normalized.url)),
          options,
        );
      }
      if (options.requestSanitizer != null) {
        final transformed = options.requestSanitizer!(normalized);
        if (transformed == null) return null;
        normalized = _normalize(transformed, options);
      }
      return normalized;
    } catch (_) {
      return null;
    }
  }

  static AnsightNetworkRequest _normalize(
    AnsightNetworkRequest request,
    AnsightNetworkSanitizationOptions options,
  ) {
    final now = DateTime.now().toUtc();
    final started = DateTime.tryParse(request.startedAtUtc)?.toUtc() ?? now;
    var completed =
        DateTime.tryParse(request.completedAtUtc)?.toUtc() ?? started;
    if (completed.isBefore(started)) completed = started;
    return AnsightNetworkRequest(
      id: _required(request.id, _newId(), 128),
      source: _required(request.source, 'unknown', 128),
      startedAtUtc: started.toIso8601String(),
      completedAtUtc: completed.toIso8601String(),
      durationMilliseconds: request.durationMilliseconds.isFinite
          ? request.durationMilliseconds.clamp(0, double.infinity).toDouble()
          : 0,
      method: _required(request.method, 'GET', 32).toUpperCase(),
      url: _sanitizeUrl(request.url, options),
      protocol: _optional(request.protocol, 64),
      requestHeaders: options.includeRequestHeaders
          ? _sanitizeHeaders(request.requestHeaders, options)
          : const <AnsightNetworkHeader>[],
      requestBodySizeBytes:
          options.includeBodySizes ? _size(request.requestBodySizeBytes) : null,
      requestBody: options.captureRequestBody
          ? _sanitizeBody(request.requestBody, options)
          : null,
      statusCode: request.statusCode != null &&
              request.statusCode! >= 100 &&
              request.statusCode! <= 999
          ? request.statusCode
          : null,
      reasonPhrase: _optional(request.reasonPhrase, 512),
      responseHeaders: options.includeResponseHeaders
          ? _sanitizeHeaders(request.responseHeaders, options)
          : const <AnsightNetworkHeader>[],
      responseBodySizeBytes: options.includeBodySizes
          ? _size(request.responseBodySizeBytes)
          : null,
      responseBody: options.captureResponseBody
          ? _sanitizeBody(request.responseBody, options)
          : null,
      errorType: _optional(request.errorType, 512),
      errorMessage: _sanitizeError(request.errorMessage, options),
    );
  }

  static AnsightNetworkRequest _replace(
    AnsightNetworkRequest request, {
    required String url,
  }) =>
      AnsightNetworkRequest(
        id: request.id,
        source: request.source,
        startedAtUtc: request.startedAtUtc,
        completedAtUtc: request.completedAtUtc,
        durationMilliseconds: request.durationMilliseconds,
        method: request.method,
        url: url,
        protocol: request.protocol,
        requestHeaders: request.requestHeaders,
        requestBodySizeBytes: request.requestBodySizeBytes,
        requestBody: request.requestBody,
        statusCode: request.statusCode,
        reasonPhrase: request.reasonPhrase,
        responseHeaders: request.responseHeaders,
        responseBodySizeBytes: request.responseBodySizeBytes,
        responseBody: request.responseBody,
        errorType: request.errorType,
        errorMessage: request.errorMessage,
      );

  static List<AnsightNetworkHeader> _sanitizeHeaders(
    List<AnsightNetworkHeader> headers,
    AnsightNetworkSanitizationOptions options,
  ) =>
      headers
          .where((AnsightNetworkHeader header) => header.name.trim().isNotEmpty)
          .take(_maximumHeaderCount)
          .map((AnsightNetworkHeader header) {
        final name = _required(header.name, 'Header', 256);
        return AnsightNetworkHeader(
          name: name,
          value: _isSensitiveHeader(name, options)
              ? ansightRedactedNetworkValue
              : _required(header.value, '', _maximumHeaderValueLength),
        );
      }).toList(growable: false);

  static bool _isSensitiveHeader(
    String name,
    AnsightNetworkSanitizationOptions options,
  ) {
    final lowered = name.toLowerCase();
    if (_sensitiveHeaderNames.contains(lowered) ||
        options.additionalSensitiveHeaderNames
            .map((String value) => value.toLowerCase())
            .contains(lowered)) {
      return true;
    }
    final compact = lowered.replaceAll('-', '');
    return compact.contains('token') ||
        compact.contains('secret') ||
        compact.contains('apikey');
  }

  static String _sanitizeUrl(
    String value,
    AnsightNetworkSanitizationOptions options,
  ) {
    var normalized = _required(value, '<unknown>', _maximumUrlLength);
    normalized = normalized.replaceFirstMapped(
      RegExp(r'^(https?://)[^/@]+@', caseSensitive: false),
      (Match match) => '${match.group(1)}$ansightRedactedNetworkValue@',
    );
    final queryIndex = normalized.indexOf('?');
    if (queryIndex < 0) return _truncate(normalized, _maximumUrlLength);
    final fragmentIndex = normalized.indexOf('#', queryIndex);
    if (!options.includeQueryString) {
      return _truncate(
        normalized.substring(0, queryIndex) +
            (fragmentIndex < 0 ? '' : normalized.substring(fragmentIndex)),
        _maximumUrlLength,
      );
    }
    final queryEnd = fragmentIndex < 0 ? normalized.length : fragmentIndex;
    final query = normalized.substring(queryIndex + 1, queryEnd);
    final fragment =
        fragmentIndex < 0 ? '' : normalized.substring(fragmentIndex);
    return _truncate(
      '${normalized.substring(0, queryIndex + 1)}${_sanitizeQuery(query, options)}$fragment',
      _maximumUrlLength,
    );
  }

  static String _sanitizeQuery(
    String query,
    AnsightNetworkSanitizationOptions options,
  ) {
    final pairs = query.split('&');
    final names = pairs
        .map(_decodeQueryName)
        .map((String value) => value.toLowerCase())
        .toSet();
    final hasAzureSas =
        names.contains('sig') && _azureSasFingerprintNames.any(names.contains);
    final hasAwsSignature = names.contains('x-amz-signature');
    final hasGoogleSignature = names.contains('x-goog-signature');
    final hasCloudFrontSignature = names.contains('signature') &&
        <String>{'key-pair-id', 'policy', 'expires'}.any(names.contains);
    final hasLegacyGoogleSignature =
        names.contains('signature') && names.contains('googleaccessid');
    final hasAlibabaSignature =
        (names.contains('signature') && names.contains('ossaccesskeyid')) ||
            names.contains('x-oss-signature');
    return pairs.map((String pair) {
      final equalsIndex = pair.indexOf('=');
      final encodedName =
          equalsIndex < 0 ? pair : pair.substring(0, equalsIndex);
      final decodedName = _decodeQueryName(pair);
      final lowered = decodedName.toLowerCase();
      final providerSensitive =
          (hasAzureSas && _azureSasQueryNames.contains(lowered)) ||
              (hasAwsSignature && lowered.startsWith('x-amz-')) ||
              (hasGoogleSignature && lowered.startsWith('x-goog-')) ||
              (hasCloudFrontSignature &&
                  <String>{
                    'signature',
                    'key-pair-id',
                    'policy',
                    'expires',
                    'hash-algorithm'
                  }.contains(lowered)) ||
              (hasLegacyGoogleSignature &&
                  <String>{'signature', 'googleaccessid', 'expires'}
                      .contains(lowered)) ||
              (hasAlibabaSignature &&
                  (lowered.startsWith('x-oss-') ||
                      <String>{'signature', 'ossaccesskeyid', 'security-token'}
                          .contains(lowered)));
      final sensitive = providerSensitive ||
          _sensitiveQueryNames.contains(lowered) ||
          options.additionalSensitiveQueryParameterNames
              .map((String value) => value.toLowerCase())
              .contains(lowered);
      return sensitive
          ? '$encodedName=${Uri.encodeQueryComponent(ansightRedactedNetworkValue)}'
          : pair;
    }).join('&');
  }

  static String _decodeQueryName(String pair) {
    final equalsIndex = pair.indexOf('=');
    final encodedName = equalsIndex < 0 ? pair : pair.substring(0, equalsIndex);
    try {
      return Uri.decodeQueryComponent(encodedName);
    } catch (_) {
      return encodedName;
    }
  }

  static int maximumBodyBytes(AnsightNetworkSanitizationOptions options) =>
      options.maximumBodyBytes < 0 ? 0 : options.maximumBodyBytes;

  static bool isTextContentType(String? contentType) {
    if (contentType == null || contentType.trim().isEmpty) return true;
    final mediaType = contentType.split(';').first.trim().toLowerCase();
    return mediaType.startsWith('text/') ||
        mediaType.endsWith('+json') ||
        mediaType.endsWith('+xml') ||
        <String>{
          'application/json',
          'application/xml',
          'application/graphql',
          'application/javascript',
          'application/x-www-form-urlencoded',
        }.contains(mediaType);
  }

  static AnsightNetworkBody? createBody(
    List<int> bytes, {
    required int? totalBytes,
    required String? contentType,
    required AnsightNetworkSanitizationOptions options,
  }) {
    final maximum = maximumBodyBytes(options);
    final binary = !isTextContentType(contentType);
    if (maximum <= 0 || (binary && !options.captureBinaryBodies)) return null;
    var captured = Uint8List.fromList(bytes.take(maximum).toList());
    if (!binary) captured = _completeUtf8(captured);
    return AnsightNetworkBody(
      contentType: _optional(contentType, 512),
      encoding: binary ? 'base64' : 'utf8',
      data: binary ? base64Encode(captured) : utf8.decode(captured),
      capturedBytes: captured.length,
      totalBytes: _size(totalBytes),
      truncated: bytes.length > captured.length ||
          (totalBytes != null && totalBytes > captured.length),
    );
  }

  static AnsightNetworkBody? _sanitizeBody(
    AnsightNetworkBody? body,
    AnsightNetworkSanitizationOptions options,
  ) {
    if (body == null) return null;
    final maximum = maximumBodyBytes(options);
    if (maximum <= 0) return null;
    List<int> decoded;
    try {
      if (body.encoding.toLowerCase() == 'utf8') {
        decoded = utf8.encode(_sanitizeSensitiveText(body.data, options));
      } else if (body.encoding.toLowerCase() == 'base64' &&
          options.captureBinaryBodies) {
        decoded = base64Decode(body.data);
      } else {
        return null;
      }
    } catch (_) {
      return null;
    }
    var captured = Uint8List.fromList(decoded.take(maximum).toList());
    final encoding = body.encoding.toLowerCase();
    if (encoding == 'utf8') captured = _completeUtf8(captured);
    final totalBytes = _size(body.totalBytes);
    return AnsightNetworkBody(
      contentType: _optional(body.contentType, 512),
      encoding: encoding,
      data:
          encoding == 'base64' ? base64Encode(captured) : utf8.decode(captured),
      capturedBytes: captured.length,
      totalBytes: totalBytes,
      truncated: body.truncated ||
          decoded.length > captured.length ||
          (totalBytes != null && totalBytes > captured.length),
    );
  }

  static Uint8List _completeUtf8(Uint8List bytes) {
    var length = bytes.length;
    while (length > 0) {
      try {
        utf8.decode(bytes.sublist(0, length));
        return Uint8List.fromList(bytes.sublist(0, length));
      } on FormatException {
        length--;
      }
    }
    return Uint8List(0);
  }

  static String _sanitizeSensitiveText(
    String value,
    AnsightNetworkSanitizationOptions options,
  ) {
    final assignments = value.replaceAllMapped(
      RegExp(
        r'''(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)(["']?\s*[:=]\s*["']?)([^&\s,;}"']+)''',
        caseSensitive: false,
      ),
      (Match match) =>
          '${match.group(1)}${match.group(2)}$ansightRedactedNetworkValue',
    );
    return assignments.replaceAllMapped(
      RegExp(r'''https?://[^\s"'<>]+''', caseSensitive: false),
      (Match match) => _sanitizeUrl(match.group(0)!, options),
    );
  }

  static String? _sanitizeError(
    String? value,
    AnsightNetworkSanitizationOptions options,
  ) {
    final normalized = _optional(value, _maximumErrorMessageLength);
    if (normalized == null) return null;
    final assignments = normalized.replaceAllMapped(
      RegExp(
        r'(access_token|api_key|apikey|auth|authorization|code|key|password|passwd|secret|signature|token)(\s*=\s*)([^&\s,;]+)',
        caseSensitive: false,
      ),
      (Match match) =>
          '${match.group(1)}${match.group(2)}$ansightRedactedNetworkValue',
    );
    return _truncate(
      assignments.replaceAllMapped(
        RegExp(r'''https?://[^\s"'<>]+''', caseSensitive: false),
        (Match match) => _sanitizeUrl(match.group(0)!, options),
      ),
      _maximumErrorMessageLength,
    );
  }

  static int? _size(int? value) => value != null && value >= 0 ? value : null;

  static String _newId() =>
      '${DateTime.now().microsecondsSinceEpoch.toRadixString(36)}${identityHashCode(Object()).toRadixString(36)}';

  static String _required(
    String? value,
    String fallback,
    int maximumLength,
  ) {
    final normalized = value?.trim();
    return _truncate(
      normalized == null || normalized.isEmpty ? fallback : normalized,
      maximumLength,
    );
  }

  static String? _optional(String? value, int maximumLength) {
    final normalized = value?.trim();
    return normalized == null || normalized.isEmpty
        ? null
        : _truncate(normalized, maximumLength);
  }

  static String _truncate(String value, int maximumLength) =>
      value.length <= maximumLength
          ? value
          : '${value.substring(0, maximumLength)}…';
}
