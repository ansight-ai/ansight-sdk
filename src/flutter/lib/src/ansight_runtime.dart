import 'dart:async';
import 'dart:convert';
// Required by Flutter 3.0, where foundation.dart did not re-export Uint8List.
// ignore: unnecessary_import
import 'dart:typed_data';

import 'package:flutter/foundation.dart';

import 'ansight_models.dart';
import 'ansight_options.dart';
import 'ansight_tooling.dart';
import 'native_transport.dart';

class Ansight {
  Ansight._(this._transport) {
    _transport.eventCallback = _handleNativeEvent;
    _transport.toolCallCallback = _handleNativeToolCall;
  }

  @visibleForTesting
  factory Ansight.withTransport(AnsightNativeTransport transport) =>
      Ansight._(transport);

  static final Ansight instance = Ansight._(PigeonAnsightNativeTransport());

  final AnsightNativeTransport _transport;
  final Map<String, AnsightToolHandler> _toolHandlers =
      <String, AnsightToolHandler>{};
  final Map<String, AnsightToolDefinition> _toolDefinitions =
      <String, AnsightToolDefinition>{};
  final Map<String, AnsightArtifactProvider> _artifactProviders =
      <String, AnsightArtifactProvider>{};
  final StreamController<AnsightLogEntry> _logs =
      StreamController<AnsightLogEntry>.broadcast();
  final StreamController<AnsightHostConnectionStatus> _connectionStatuses =
      StreamController<AnsightHostConnectionStatus>.broadcast();
  final StreamController<AnsightJson> _nativeEvents =
      StreamController<AnsightJson>.broadcast();

  bool _artifactToolsInstalled = false;
  String? _lastConnectionStatusKey;

  Stream<AnsightLogEntry> get logs => _logs.stream;

  Stream<AnsightHostConnectionStatus> get connectionStatusChanges =>
      _connectionStatuses.stream;

  Stream<AnsightJson> get nativeEvents => _nativeEvents.stream;

  List<String> get registeredToolIds =>
      List<String>.unmodifiable(_toolHandlers.keys);

  List<String> get registeredArtifactProviderIds =>
      List<String>.unmodifiable(_artifactProviders.keys);

  Future<AnsightDebugSnapshot> initialize([
    AnsightOptions options = const AnsightOptions(),
  ]) async {
    final result = await _invoke('initialize', options.toJson());
    await _publishConnectionStatus();
    return AnsightDebugSnapshot.fromJson(result);
  }

  Future<AnsightDebugSnapshot> initializeAndActivate([
    AnsightOptions options = const AnsightOptions(),
  ]) async {
    final result = await _invoke('initializeAndActivate', options.toJson());
    await _publishConnectionStatus();
    return AnsightDebugSnapshot.fromJson(result);
  }

  Future<AnsightDebugSnapshot> activate() async {
    final result = await _invoke('activate');
    await _publishConnectionStatus();
    return AnsightDebugSnapshot.fromJson(result);
  }

  Future<AnsightDebugSnapshot> deactivate() async {
    final result = await _invoke('deactivate');
    await _publishConnectionStatus();
    return AnsightDebugSnapshot.fromJson(result);
  }

  Future<AnsightDebugSnapshot> clear() async {
    final result = await _invoke('clear');
    await _publishConnectionStatus();
    return AnsightDebugSnapshot.fromJson(result);
  }

  Future<AnsightDebugSnapshot> registerMetricChannel(
    AnsightChannel channel,
  ) async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('registerMetricChannel', channel.toJson()),
      );

  Future<AnsightDebugSnapshot> metric(num value, {int channel = 255}) async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('recordMetric', <String, Object?>{
          'value': value.round(),
          'channel': channel,
        }),
      );

  Future<AnsightDebugSnapshot> recordMetric(num value, {int channel = 255}) =>
      metric(value, channel: channel);

  Future<AnsightDebugSnapshot> event(
    String label, {
    AnsightEventType type = AnsightEventType.info,
    String? details,
    int channel = 255,
  }) async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('recordEvent', <String, Object?>{
          'label': label,
          'type': type.wireName,
          if (details != null) 'details': details,
          'channel': channel,
        }),
      );

  Future<AnsightDebugSnapshot> screenViewed(
    String name, {
    Map<String, String> details = const <String, String>{},
  }) async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('screenViewed', <String, Object?>{
          'name': name,
          'details': details,
        }),
      );

  Future<AnsightDebugSnapshot> setAppLifecycleState(
    AnsightLifecycleState state,
  ) async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('setAppLifecycleState', <String, Object?>{
          'state': state.wireName,
        }),
      );

  Future<AnsightHostConnectionResult> connect({
    Object? pairingPayload,
    String? clientName,
    String? expectedAppId,
    String? hostAddressOverride,
  }) async {
    final result = await _invoke('connect', <String, Object?>{
      if (pairingPayload != null)
        'pairingPayload': _normalizePairingPayload(pairingPayload),
      if (clientName != null) 'clientName': clientName,
      if (expectedAppId != null) 'expectedAppId': expectedAppId,
      if (hostAddressOverride != null)
        'hostAddressOverride': hostAddressOverride,
    });
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult> scanPairingQrCode({
    String title = 'Scan Ansight Pairing QR',
    String? clientName,
    String? expectedAppId,
    String? hostAddressOverride,
  }) async {
    final result = await _invoke('scanPairingQrCode', <String, Object?>{
      'title': title,
      if (clientName != null) 'clientName': clientName,
      if (expectedAppId != null) 'expectedAppId': expectedAppId,
      if (hostAddressOverride != null)
        'hostAddressOverride': hostAddressOverride,
    });
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult> openSession(
    Object pairingPayload, {
    String? clientName,
    String? expectedAppId,
    String? hostAddressOverride,
  }) async {
    final result = await _invoke('openSession', <String, Object?>{
      'pairingPayload': _normalizePairingPayload(pairingPayload),
      if (clientName != null) 'clientName': clientName,
      if (expectedAppId != null) 'expectedAppId': expectedAppId,
      if (hostAddressOverride != null)
        'hostAddressOverride': hostAddressOverride,
    });
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult> disconnect() async {
    final result = await _invoke('disconnect');
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightOperationResult> completeSession() async {
    final result = await _invoke('completeSession');
    await _publishConnectionStatus();
    return AnsightOperationResult.fromJson(result);
  }

  Future<AnsightOperationResult> closeSession() async {
    final result = await _invoke('closeSession');
    await _publishConnectionStatus();
    return AnsightOperationResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult> savePairingConfig(
    Object pairingPayload, {
    String? expectedAppId,
  }) async {
    final result = await _invoke('savePairingConfig', <String, Object?>{
      'pairingPayload': _normalizePairingPayload(pairingPayload),
      if (expectedAppId != null) 'expectedAppId': expectedAppId,
    });
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult> clearSavedPairing() async {
    final result = await _invoke('clearSavedPairing');
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightOperationResult> clearCachedSession() async {
    final result = await _invoke('clearCachedSession');
    await _publishConnectionStatus();
    return AnsightOperationResult.fromJson(result);
  }

  Future<AnsightHostConnectionResult>
      notifyHostConnectionConfigChanged() async {
    final result = await _invoke('notifyHostConnectionConfigChanged');
    await _publishConnectionStatus();
    return AnsightHostConnectionResult.fromJson(result);
  }

  Future<AnsightDebugSnapshot> status() async =>
      AnsightDebugSnapshot.fromJson(await _invoke('status'));

  Future<AnsightDebugSnapshot> snapshot() async =>
      AnsightDebugSnapshot.fromJson(await _invoke('snapshot'));

  Future<AnsightHostConnectionStatus> hostConnectionStatus() async =>
      AnsightHostConnectionStatus.fromJson(
        await _invoke('hostConnectionStatus'),
      );

  Future<AnsightHostConnectionCapabilities>
      hostConnectionCapabilities() async =>
          AnsightHostConnectionCapabilities.fromJson(
            await _invoke('hostConnectionCapabilities'),
          );

  Future<AnsightJson> currentOptions() => _invoke('currentOptions');

  Future<List<AnsightRecordedMetric>> recordedMetrics({int limit = 0}) async =>
      ansightJsonList(
        (await _invoke('recordedMetrics', <String, Object?>{
          'limit': limit,
        }))['items'],
      ).map(ansightJsonMap).map(AnsightRecordedMetric.fromJson).toList();

  Future<List<AnsightRecordedEvent>> recordedEvents({int limit = 0}) async =>
      ansightJsonList(
        (await _invoke('recordedEvents', <String, Object?>{
          'limit': limit,
        }))['items'],
      ).map(ansightJsonMap).map(AnsightRecordedEvent.fromJson).toList();

  Future<AnsightOperationResult> sendClientLog(String line) async =>
      AnsightOperationResult.fromJson(
        await _invoke('sendClientLog', <String, Object?>{'line': line}),
      );

  Future<AnsightDebugSnapshot> captureBuiltInTelemetrySample() async =>
      AnsightDebugSnapshot.fromJson(
        await _invoke('captureBuiltInTelemetrySample'),
      );

  Future<bool> isFramesPerSecondEnabled() async =>
      (await _invoke('isFramesPerSecondEnabled'))['value'] == true;

  Future<AnsightDebugSnapshot> enableFramesPerSecond() async =>
      AnsightDebugSnapshot.fromJson(await _invoke('enableFramesPerSecond'));

  Future<AnsightDebugSnapshot> disableFramesPerSecond() async =>
      AnsightDebugSnapshot.fromJson(await _invoke('disableFramesPerSecond'));

  Future<AnsightOperationResult> captureScreenFrame({
    AnsightSessionJpegCaptureOptions options =
        const AnsightSessionJpegCaptureOptions(),
  }) async =>
      AnsightOperationResult.fromJson(
        await _invoke('captureScreenFrame', options.toJson()),
      );

  Future<AnsightOperationResult> enableTouchCapture() async =>
      AnsightOperationResult.fromJson(await _invoke('enableTouchCapture'));

  Future<AnsightOperationResult> disableTouchCapture() async =>
      AnsightOperationResult.fromJson(await _invoke('disableTouchCapture'));

  Future<AnsightOperationResult> updateSessionProperties(
    Map<String, Map<String, String>> properties,
  ) async =>
      AnsightOperationResult.fromJson(
        await _invoke('updateSessionProperties', <String, Object?>{
          'properties': properties,
        }),
      );

  Future<AnsightOperationResult> clearSessionProperties() async =>
      AnsightOperationResult.fromJson(await _invoke('clearSessionProperties'));

  Future<AnsightOperationResult> registerCustomProperty(
    String group,
    String key,
    Object? value,
  ) async =>
      AnsightOperationResult.fromJson(
        await _invoke('registerCustomProperty', <String, Object?>{
          'group': group,
          'key': key,
          'value': value?.toString() ?? '',
        }),
      );

  Future<AnsightOperationResult> removeCustomProperty(
    String group,
    String key,
  ) async =>
      AnsightOperationResult.fromJson(
        await _invoke('removeCustomProperty', <String, Object?>{
          'group': group,
          'key': key,
        }),
      );

  /// Registers Flutter as a source for the native visual-tree tools.
  ///
  /// This is normally called by [AnsightFlutterInstrumentation.install].
  Future<AnsightOperationResult> enableFlutterVisualTreeProvider() async =>
      AnsightOperationResult.fromJson(
        await _invoke('enableFlutterVisualTreeProvider'),
      );

  Future<AnsightToolRegistration> registerTool(
    AnsightToolDefinition definition,
    AnsightToolHandler handler,
  ) async {
    final id = definition.id.trim();
    if (id.isEmpty) {
      throw ArgumentError.value(definition.id, 'definition.id', 'is blank');
    }
    _toolDefinitions[id] = definition;
    _toolHandlers[id] = handler;
    try {
      await _invoke('registerCustomTool', <String, Object?>{
        'definition': definition.toJson(),
      });
    } catch (_) {
      _toolDefinitions.remove(id);
      _toolHandlers.remove(id);
      rethrow;
    }
    return AnsightToolRegistration(
      id: id,
      unregister: () => unregisterTool(id),
    );
  }

  Future<void> unregisterTool(String id) async {
    _toolDefinitions.remove(id);
    _toolHandlers.remove(id);
    await _invoke('unregisterCustomTool', <String, Object?>{'id': id});
  }

  Future<void> clearRegisteredTools() async {
    _toolDefinitions.clear();
    _toolHandlers.clear();
    _artifactProviders.clear();
    _artifactToolsInstalled = false;
    await _invoke('clearRegisteredCustomTools');
  }

  Future<AnsightToolRegistration> registerArtifactProvider(
    AnsightArtifactProvider provider,
  ) async {
    final id = provider.descriptor.id.trim();
    if (id.isEmpty) {
      throw ArgumentError.value(id, 'provider.descriptor.id', 'is blank');
    }
    _artifactProviders[id] = provider;
    await _installArtifactToolsIfNeeded();
    return AnsightToolRegistration(
      id: id,
      unregister: () => unregisterArtifactProvider(id),
    );
  }

  Future<void> unregisterArtifactProvider(String providerId) async {
    _artifactProviders.remove(providerId);
  }

  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  }) =>
      _transport.queueBinaryTransfer(
        requestId: requestId,
        data: data,
        chunkBytes: chunkBytes,
      );

  void registerLocalToolHandler(String id, AnsightToolHandler handler) {
    _toolHandlers[id] = handler;
  }

  void removeLocalToolHandler(String id) {
    _toolHandlers.remove(id);
  }

  Future<AnsightJson> _invoke(String method, [AnsightJson? arguments]) =>
      _transport.invoke(method, arguments);

  void _handleNativeEvent(String name, AnsightJson payload) {
    _nativeEvents.add(<String, Object?>{'name': name, ...payload});
    switch (name) {
      case 'log':
        _logs.add(AnsightLogEntry.fromJson(payload));
        break;
      case 'connectionStatus':
        _emitConnectionStatus(AnsightHostConnectionStatus.fromJson(payload));
        break;
    }
  }

  void _handleNativeToolCall(AnsightJson request) {
    unawaited(_executeNativeToolCall(request));
  }

  Future<void> _executeNativeToolCall(AnsightJson request) async {
    final context = AnsightToolContext.fromJson(request);
    final handler = _toolHandlers[context.toolId];
    AnsightToolResult result;
    if (handler == null) {
      result = AnsightToolResult.failure(
        message: "Dart tool '${context.toolId}' is not registered.",
        errorCode: 'dart_tool_unregistered',
      );
    } else {
      try {
        final rawArguments = ansightJsonMap(request['arguments']);
        final arguments = rawArguments.map(
          (String key, Object? value) => MapEntry(key, value?.toString() ?? ''),
        );
        result = await handler(arguments, context).timeout(
          _toolDefinitions[context.toolId]?.timeout ??
              const Duration(seconds: 30),
        );
      } on TimeoutException {
        result = AnsightToolResult.failure(
          message: "Dart tool '${context.toolId}' timed out.",
          errorCode: 'dart_tool_timeout',
        );
      } catch (error, stackTrace) {
        result = AnsightToolResult.failure(
          message: error.toString(),
          errorCode: 'dart_tool_exception',
          result: <String, Object?>{'stackTrace': stackTrace.toString()},
        );
      }
    }

    try {
      await _invoke('resolveToolCall', <String, Object?>{
        'requestId': context.requestId,
        'result': result.toJson(),
      });
    } catch (error) {
      _logs.add(
        AnsightLogEntry(
          level: 'error',
          message: "Failed to resolve Dart tool '${context.toolId}': $error",
          platform: 'dart',
        ),
      );
    }
  }

  Future<void> _publishConnectionStatus() async {
    try {
      _emitConnectionStatus(await hostConnectionStatus());
    } catch (_) {
      // Connection status is diagnostic; it must not replace the operation
      // result that triggered this refresh.
    }
  }

  void _emitConnectionStatus(AnsightHostConnectionStatus status) {
    final key = <Object?>[
      status.isRuntimeActive,
      status.isConnected,
      status.connectionState,
      status.hasCachedSession,
      status.hasSavedConfig,
      status.hasBundledConfig,
      status.summaryKind,
      status.summaryMessage,
    ].join('|');
    if (_lastConnectionStatusKey == key) {
      return;
    }
    _lastConnectionStatusKey = key;
    _connectionStatuses.add(status);
  }

  Future<void> _installArtifactToolsIfNeeded() async {
    if (_artifactToolsInstalled) {
      return;
    }
    _artifactToolsInstalled = true;
    try {
      await registerTool(
        const AnsightToolDefinition(
          id: 'artifacts.query',
          name: 'Query App Artifacts',
          description: 'Lists artifacts currently available from the app.',
          category: 'artifacts',
          scope: AnsightToolScope.read,
          security: AnsightToolSecurity(
            level: AnsightToolSecurityLevel.medium,
            summary: 'Enumerates app-provided diagnostic exports.',
            implications: <String>['metadata_disclosure'],
          ),
        ),
        _queryArtifacts,
      );
      await registerTool(
        const AnsightToolDefinition(
          id: 'artifacts.request',
          name: 'Request App Artifact',
          description:
              'Creates one app-provided artifact and transfers it to Studio.',
          category: 'artifacts',
          scope: AnsightToolScope.read,
          security: AnsightToolSecurity(
            level: AnsightToolSecurityLevel.high,
            summary: 'Exports app-provided diagnostic data.',
            implications: <String>[
              'metadata_disclosure',
              'binary_data_transfer',
            ],
          ),
        ),
        _requestArtifact,
      );
    } catch (_) {
      _artifactToolsInstalled = false;
      rethrow;
    }
  }

  Future<AnsightToolResult> _queryArtifacts(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final providers = <Object?>[];
    for (final provider in _artifactProviders.values) {
      try {
        final definitions = await provider.query();
        providers.add(<String, Object?>{
          ...provider.descriptor.toJson(),
          'artifacts': definitions
              .map(
                (AnsightArtifactDefinition definition) =>
                    definition.toJson(provider.descriptor.id),
              )
              .toList(growable: false),
        });
      } catch (error) {
        providers.add(<String, Object?>{
          ...provider.descriptor.toJson(),
          'artifacts': const <Object?>[],
          'error': error.toString(),
        });
      }
    }
    return AnsightToolResult.success(
      message: 'App artifact catalog captured.',
      result: <String, Object?>{
        'providers': providers,
        'providerCount': providers.length,
      },
    );
  }

  Future<AnsightToolResult> _requestArtifact(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final providerId = arguments['providerId']?.trim() ?? '';
    final artifactId = arguments['artifactId']?.trim() ?? '';
    final provider = _artifactProviders[providerId];
    if (provider == null) {
      return AnsightToolResult.failure(
        message: "Artifact provider '$providerId' was not found.",
        errorCode: 'artifact_provider_not_found',
      );
    }
    if (artifactId.isEmpty) {
      return const AnsightToolResult.failure(
        message: 'Artifact id is required.',
        errorCode: 'artifact_id_required',
      );
    }

    final payload = await provider.create(
      AnsightArtifactRequest(
        providerId: providerId,
        artifactId: artifactId,
        downloadId: arguments['downloadId'],
        mimeType: arguments['mimeType'],
        arguments: arguments,
      ),
    );
    final chunkBytes = (int.tryParse(
              arguments['chunkBytes'] ?? '',
            )?.clamp(1024, 524288) ??
            65536)
        .toInt();
    final transfer = await queueBinaryTransfer(
      requestId: context.nativeRequestId ?? context.requestId,
      data: payload.bytes,
      chunkBytes: chunkBytes,
    );

    return AnsightToolResult.success(
      message: 'Artifact transfer queued.',
      result: <String, Object?>{
        ...transfer,
        'providerId': providerId,
        'artifactId': artifactId,
        'name': payload.name ?? artifactId,
        'kind': payload.kind,
        'mimeType': payload.mimeType,
        'fileName': payload.fileName,
        'sizeBytes': payload.bytes.length,
        'metadata': payload.metadata,
      },
    );
  }

  static String _normalizePairingPayload(Object payload) =>
      payload is String ? payload : jsonEncode(payload);
}
