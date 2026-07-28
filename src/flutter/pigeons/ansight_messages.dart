import 'package:pigeon/pigeon.dart';

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
}

@FlutterApi()
abstract class AnsightDartApi {
  void onNativeEvent(String name, String payloadJson);

  void onToolCall(String requestJson);
}
