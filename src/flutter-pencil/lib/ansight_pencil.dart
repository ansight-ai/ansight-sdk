import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'src/surface.dart';
export 'src/surface.dart' show AnsightPencilSurface, AnsightPencilPointerData;

/// Optional Simulator playback. Importing this library does not enable it.
abstract class AnsightPencil {
  static const MethodChannel _channel = MethodChannel('ai.ansight/pencil');
  static bool get _isIOS =>
      !kIsWeb && defaultTargetPlatform == TargetPlatform.iOS;

  /// Platform capability only; does not register tools or install hooks.
  static Future<bool> isSupported() async =>
      _isIOS && (await _channel.invokeMethod<bool>('isSupported') ?? false);

  /// Low-level registration. Prefer withSimulatorPenInput() in the options builder.
  /// Returns false outside iOS Simulator; throws if the SDK is not initialized.
  /// Idempotent. Grants only Pencil tools; global disable still wins. Does not install hooks.
  static Future<bool> register() async =>
      _isIOS && (await _channel.invokeMethod<bool>('register') ?? false);
}

/// This extension is available only when the optional Pencil package is imported.
extension AnsightPencilOptions on AnsightOptionsBuilder {
  AnsightOptionsBuilder withSimulatorPenInput() =>
      withPlugin(const _PencilPlugin());
}

class _PencilPlugin implements AnsightPlugin {
  const _PencilPlugin();
  @override
  String get id => 'ansight.pencil';
  @override
  Future<void> initialize() async {
    if (await AnsightPencil.register()) await initializePencilSurfaces();
  }
}
