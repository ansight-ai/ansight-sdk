import 'dart:convert';
import 'dart:typed_data';

import 'ansight_models.dart';
import 'ansight_network_models.dart';
import 'generated/ansight_messages.g.dart';

typedef AnsightNativeEventCallback = void Function(
    String name, AnsightJson payload);
typedef AnsightNativeToolCallCallback = void Function(AnsightJson request);

abstract class AnsightNativeTransport {
  set eventCallback(AnsightNativeEventCallback? callback);

  set toolCallCallback(AnsightNativeToolCallCallback? callback);

  Future<AnsightJson> invoke(String method, [AnsightJson? arguments]);

  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  });

  Future<AnsightJson> recordNetworkRequest(
    AnsightNetworkRequest request,
  );
}

class PigeonAnsightNativeTransport
    implements AnsightNativeTransport, AnsightDartApi {
  PigeonAnsightNativeTransport({AnsightNativeHostApi? hostApi})
      : _hostApi = hostApi ?? AnsightNativeHostApi() {
    AnsightDartApi.setUp(this);
  }

  final AnsightNativeHostApi _hostApi;

  @override
  AnsightNativeEventCallback? eventCallback;

  @override
  AnsightNativeToolCallCallback? toolCallCallback;

  @override
  Future<AnsightJson> invoke(String method, [AnsightJson? arguments]) async {
    final encoded = arguments == null ? null : jsonEncode(arguments);
    final response = await _hostApi.invoke(method, encoded);
    return _decodeObject(response);
  }

  @override
  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  }) async {
    final response = await _hostApi.queueBinaryTransfer(
      requestId,
      data,
      chunkBytes,
    );
    return _decodeObject(response);
  }

  @override
  Future<AnsightJson> recordNetworkRequest(
    AnsightNetworkRequest request,
  ) async {
    final response = await _hostApi.recordNetworkRequest(request.toMessage());
    return _decodeObject(response);
  }

  @override
  void onNativeEvent(String name, String payloadJson) {
    eventCallback?.call(name, _decodeObject(payloadJson));
  }

  @override
  void onToolCall(String requestJson) {
    toolCallCallback?.call(_decodeObject(requestJson));
  }

  static AnsightJson _decodeObject(String json) {
    final decoded = jsonDecode(json);
    if (decoded is Map) {
      return decoded.map(
        (Object? key, Object? value) => MapEntry(key.toString(), value),
      );
    }
    return <String, Object?>{'value': decoded};
  }
}
