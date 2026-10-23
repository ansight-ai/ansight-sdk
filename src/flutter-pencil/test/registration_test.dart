import 'package:ansight_flutter/ansight.dart';
import 'package:ansight_flutter/src/generated/ansight_messages.g.dart';
import 'package:ansight_pencil/ansight_pencil.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('ai.ansight/pencil');
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  final calls = <String>[];
  setUp(() {
    calls.clear();
    debugDefaultTargetPlatformOverride = TargetPlatform.iOS;
    messenger.setMockDecodedMessageHandler<Object?>(
      const BasicMessageChannel<Object?>('dev.flutter.pigeon.ansight_flutter.AnsightNativeHostApi.invoke', AnsightNativeHostApi.pigeonChannelCodec),
      (message) async => ['{"success":true,"message":"Registered"}'],
    );
    messenger.setMockMethodCallHandler(channel, (call) async {
      calls.add(call.method);
      return true;
    });
  });
  tearDown(() {
    debugDefaultTargetPlatformOverride = null;
    messenger.setMockMethodCallHandler(channel, null);
  });
  test('builder configures lazy Pencil setup and retains permissions',
      () async {
    final options = createOptionsBuilder()
        .withReadOnlyToolAccess()
        .withSimulatorPenInput()
        .withSimulatorPenInput()
        .build();
    expect(calls, isEmpty);
    expect(options.plugins.length, 1);
    expect(options.toJson()['toolGuard'], 'readOnly');
    await options.plugins.single.initialize();
    expect(calls, ['register']);
  });
  test('capability does not register', () async {
    expect(await AnsightPencil.isSupported(), isTrue);
    expect(calls, ['isSupported']);
  });
  test('registration calls optional plugin explicitly', () async {
    expect(await AnsightPencil.register(), isTrue);
    expect(calls, ['register']);
  });
  test('device unsupported is preserved', () async {
    messenger.setMockMethodCallHandler(channel, (_) async => false);
    expect(await AnsightPencil.register(), isFalse);
  });
  test('other platforms do not call the native channel', () async {
    for (final platform in [
      TargetPlatform.android,
      TargetPlatform.macOS,
      TargetPlatform.linux,
      TargetPlatform.windows
    ]) {
      debugDefaultTargetPlatformOverride = platform;
      expect(await AnsightPencil.register(), isFalse);
      expect(await AnsightPencil.isSupported(), isFalse);
    }
    expect(calls, isEmpty);
  });
  test('native initialization errors reach the caller', () async {
    messenger.setMockMethodCallHandler(channel, (_) async {
      throw PlatformException(
          code: 'pen_registration_failed',
          message: 'Initialize Ansight first.');
    });
    await expectLater(
        AnsightPencil.register(), throwsA(isA<PlatformException>()));
  });
  test('missing plugin does not pretend registration succeeded', () async {
    messenger.setMockMethodCallHandler(channel, null);
    await expectLater(
        AnsightPencil.register(), throwsA(isA<MissingPluginException>()));
  });
}
