import 'dart:convert';
import 'dart:typed_data';

import 'ansight_models.dart';

class AnsightToolDefinition {
  const AnsightToolDefinition({
    required this.id,
    required this.name,
    this.description = '',
    this.category = 'custom',
    this.policy = AnsightToolPolicy.read,
    this.keywords = const <String>[],
    this.argumentsSchema = const <String, Object?>{},
    this.resultSchema = const <String, Object?>{},
    this.timeout = const Duration(seconds: 30),
  });

  final String id;
  final String name;
  final String description;
  final String category;
  final AnsightToolPolicy policy;
  final List<String> keywords;
  final AnsightJson argumentsSchema;
  final AnsightJson resultSchema;
  final Duration timeout;

  AnsightJson toJson() => <String, Object?>{
        'id': id,
        'name': name,
        'description': description,
        'category': category,
        'policy': policy.wireName,
        'keywords': keywords,
        'argumentsSchema': argumentsSchema,
        'resultSchema': resultSchema,
        'timeoutMilliseconds': timeout.inMilliseconds,
      };
}

class AnsightToolContext {
  const AnsightToolContext({
    required this.requestId,
    required this.toolId,
    required this.platform,
    this.nativeRequestId,
    this.sessionId,
  });

  final String requestId;
  final String toolId;
  final String platform;
  final String? nativeRequestId;
  final String? sessionId;

  factory AnsightToolContext.fromJson(AnsightJson json) => AnsightToolContext(
        requestId: json['requestId']?.toString() ?? '',
        toolId: json['toolId']?.toString() ?? '',
        platform: json['platform']?.toString() ?? 'unknown',
        nativeRequestId: json['nativeRequestId']?.toString(),
        sessionId: json['sessionId']?.toString(),
      );
}

class AnsightToolResult {
  const AnsightToolResult({
    required this.success,
    this.message,
    this.errorCode,
    this.result,
  });

  const AnsightToolResult.success({this.result, this.message})
      : success = true,
        errorCode = null;

  const AnsightToolResult.failure({
    required this.message,
    this.errorCode,
    this.result,
  }) : success = false;

  final bool success;
  final String? message;
  final String? errorCode;
  final Object? result;

  AnsightJson toJson() => <String, Object?>{
        'success': success,
        if (message != null) 'message': message,
        if (errorCode != null) 'errorCode': errorCode,
        if (result != null) 'result': result,
      };
}

typedef AnsightToolHandler = Future<AnsightToolResult> Function(
  Map<String, String> arguments,
  AnsightToolContext context,
);

class AnsightToolRegistration {
  AnsightToolRegistration({
    required this.id,
    required Future<void> Function() unregister,
  }) : _unregister = unregister;

  final String id;
  final Future<void> Function() _unregister;
  bool _registered = true;

  bool get isRegistered => _registered;

  Future<void> unregister() async {
    if (!_registered) {
      return;
    }
    _registered = false;
    await _unregister();
  }
}

class AnsightArtifactProviderDescriptor {
  const AnsightArtifactProviderDescriptor({
    required this.id,
    required this.name,
    this.description = '',
    this.category = 'app',
  });

  final String id;
  final String name;
  final String description;
  final String category;

  AnsightJson toJson() => <String, Object?>{
        'id': id,
        'name': name,
        'description': description,
        'category': category,
      };
}

class AnsightArtifactDefinition {
  const AnsightArtifactDefinition({
    required this.id,
    required this.name,
    this.description = '',
    this.kind = 'artifact',
    this.category = 'app',
    this.mimeType = 'application/octet-stream',
    this.fileName,
  });

  final String id;
  final String name;
  final String description;
  final String kind;
  final String category;
  final String mimeType;
  final String? fileName;

  AnsightJson toJson(String providerId) => <String, Object?>{
        'id': id,
        'providerId': providerId,
        'name': name,
        'description': description,
        'kind': kind,
        'category': category,
        'content': <String, Object?>{
          'supportedMimeTypes': <String>[mimeType],
          'defaultMimeType': mimeType,
          if (fileName != null) 'suggestedFileName': fileName,
          'supportsBytes': true,
          'supportsText':
              mimeType.startsWith('text/') || mimeType.contains('json'),
        },
      };
}

class AnsightArtifactRequest {
  const AnsightArtifactRequest({
    required this.providerId,
    required this.artifactId,
    this.downloadId,
    this.mimeType,
    this.arguments = const <String, String>{},
  });

  final String providerId;
  final String artifactId;
  final String? downloadId;
  final String? mimeType;
  final Map<String, String> arguments;
}

class AnsightArtifactPayload {
  const AnsightArtifactPayload({
    required this.bytes,
    required this.mimeType,
    required this.fileName,
    this.name,
    this.kind = 'artifact',
    this.metadata = const <String, String>{},
  });

  factory AnsightArtifactPayload.text(
    String text, {
    String mimeType = 'text/plain',
    String fileName = 'artifact.txt',
    String? name,
    String kind = 'artifact',
    Map<String, String> metadata = const <String, String>{},
  }) =>
      AnsightArtifactPayload(
        bytes: Uint8List.fromList(utf8.encode(text)),
        mimeType: mimeType,
        fileName: fileName,
        name: name,
        kind: kind,
        metadata: metadata,
      );

  final Uint8List bytes;
  final String mimeType;
  final String fileName;
  final String? name;
  final String kind;
  final Map<String, String> metadata;
}

abstract class AnsightArtifactProvider {
  AnsightArtifactProviderDescriptor get descriptor;

  Future<List<AnsightArtifactDefinition>> query();

  Future<AnsightArtifactPayload> create(AnsightArtifactRequest request);
}
