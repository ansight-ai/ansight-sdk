import "package:flutter/services.dart";

import "types.dart";

class AnsightFlutter {
  static const MethodChannel _channel = MethodChannel("ansight_flutter");

  static Future<void> initialize([AnsightOptions options = const AnsightOptions()]) {
    return _channel.invokeMethod<void>("initialize", options.toMap());
  }

  static Future<void> activate() {
    return _channel.invokeMethod<void>("activate");
  }

  static Future<void> deactivate() {
    return _channel.invokeMethod<void>("deactivate");
  }

  static Future<void> clear() {
    return _channel.invokeMethod<void>("clear");
  }

  static Future<void> metric(int value, {int channel = 255}) {
    return _channel.invokeMethod<void>("metric", {
      "value": value.toString(),
      "channel": channel,
    });
  }

  static Future<void> event(
    String label, {
    AnsightEventType type = AnsightEventType.info,
    String? details,
    int channel = 255,
    String? id,
  }) {
    return _channel.invokeMethod<void>("event", {
      "label": label,
      "type": type.wireValue,
      "details": details,
      "channel": channel,
      "id": id,
    });
  }

  static Future<OpenSessionResult> openSession(String pairingJson, PairingOpenOptions options) async {
    final result = await _channel.invokeMapMethod<Object?, Object?>("openSession", {
      "pairingJson": pairingJson,
      "options": options.toMap(),
    });
    return OpenSessionResult.fromMap(result ?? const {});
  }

  static Future<void> completeSession() {
    return _channel.invokeMethod<void>("completeSession");
  }

  static Future<void> closeSession() {
    return _channel.invokeMethod<void>("closeSession");
  }

  static Future<void> registerTool(AnsightToolDescriptor tool) {
    return _channel.invokeMethod<void>("registerTool", tool.toMap());
  }

  static Future<AnsightDebugSnapshot> getDebugSnapshot() async {
    final result = await _channel.invokeMapMethod<Object?, Object?>("getDebugSnapshot");
    return AnsightDebugSnapshot.fromMap(result ?? const {});
  }
}
