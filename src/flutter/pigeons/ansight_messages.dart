import 'package:pigeon/pigeon.dart';

class AnsightNetworkHeaderMessage {
  AnsightNetworkHeaderMessage({required this.name, required this.value});

  String name;
  String value;
}

class AnsightNetworkBodyMessage {
  AnsightNetworkBodyMessage({
    this.contentType,
    required this.encoding,
    required this.data,
    required this.capturedBytes,
    this.totalBytes,
    required this.truncated,
  });

  String? contentType;
  String encoding;
  String data;
  int capturedBytes;
  int? totalBytes;
  bool truncated;
}

class AnsightNetworkRequestMessage {
  AnsightNetworkRequestMessage({
    required this.schema,
    required this.id,
    required this.source,
    required this.startedAtUtc,
    required this.completedAtUtc,
    required this.durationMilliseconds,
    required this.method,
    required this.url,
    this.protocolName,
    required this.requestHeaders,
    this.requestBodySizeBytes,
    this.requestBody,
    this.statusCode,
    this.reasonPhrase,
    required this.responseHeaders,
    this.responseBodySizeBytes,
    this.responseBody,
    this.errorType,
    this.errorMessage,
  });

  String schema;
  String id;
  String source;
  String startedAtUtc;
  String completedAtUtc;
  double durationMilliseconds;
  String method;
  String url;
  String? protocolName;
  List<AnsightNetworkHeaderMessage> requestHeaders;
  int? requestBodySizeBytes;
  AnsightNetworkBodyMessage? requestBody;
  int? statusCode;
  String? reasonPhrase;
  List<AnsightNetworkHeaderMessage> responseHeaders;
  int? responseBodySizeBytes;
  AnsightNetworkBodyMessage? responseBody;
  String? errorType;
  String? errorMessage;
}

@ConfigurePigeon(
  PigeonOptions(
    dartOut: 'lib/src/generated/ansight_messages.g.dart',
    dartOptions: DartOptions(),
    kotlinOut:
        'android/src/main/kotlin/ai/ansight/flutter/AnsightMessages.g.kt',
    kotlinOptions: KotlinOptions(package: 'ai.ansight.flutter'),
    swiftOut:
        'ios/ansight_flutter/Sources/ansight_flutter/AnsightMessages.g.swift',
    swiftOptions: SwiftOptions(),
    dartPackageName: 'ansight_flutter',
  ),
)
@HostApi()
abstract class AnsightNativeHostApi {
  @async
  String invoke(String method, String? argumentsJson);

  @async
  String queueBinaryTransfer(String requestId, Uint8List data, int chunkBytes);

  @async
  String recordNetworkRequest(AnsightNetworkRequestMessage request);
}

@FlutterApi()
abstract class AnsightDartApi {
  void onNativeEvent(String name, String payloadJson);

  void onToolCall(String requestJson);
}
