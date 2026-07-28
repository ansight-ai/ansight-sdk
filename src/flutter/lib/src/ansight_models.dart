typedef AnsightJson = Map<String, Object?>;

enum AnsightEventType {
  event('Event'),
  debug('Debug'),
  info('Info'),
  warning('Warning'),
  error('Error'),
  exception('Exception'),
  gc('Gc'),
  navigation('Navigation'),
  screenViewed('ScreenViewed'),
  lifecycle('Lifecycle');

  const AnsightEventType(this.wireName);

  final String wireName;
}

enum AnsightLifecycleState {
  unknown('unknown'),
  foreground('foreground'),
  background('background');

  const AnsightLifecycleState(this.wireName);

  final String wireName;
}

enum AnsightToolGuard {
  disabled('disabled'),
  readOnly('readOnly'),
  readWrite('readWrite'),
  fullAccess('fullAccess');

  const AnsightToolGuard(this.wireName);

  final String wireName;
}

enum AnsightToolScope {
  read('read'),
  write('write'),
  delete('delete');

  const AnsightToolScope(this.wireName);

  final String wireName;
}

enum AnsightToolSecurityLevel {
  unspecified('unspecified'),
  low('low'),
  medium('medium'),
  moderate('moderate'),
  high('high'),
  critical('critical');

  const AnsightToolSecurityLevel(this.wireName);

  final String wireName;
}

class AnsightChannel {
  const AnsightChannel({
    required this.id,
    required this.name,
    this.unit,
    this.type = 'custom',
    this.colorHex,
    this.source,
    this.group,
    this.kind,
  });

  final int id;
  final String name;
  final String? unit;
  final String type;
  final String? colorHex;
  final String? source;
  final String? group;
  final String? kind;

  AnsightJson toJson() => <String, Object?>{
        'id': id,
        'name': name,
        if (unit != null) 'unit': unit,
        'type': type,
        if (colorHex != null) 'colorHex': colorHex,
        if (source != null) 'source': source,
        if (group != null) 'group': group,
        if (kind != null) 'kind': kind,
      };

  factory AnsightChannel.fromJson(AnsightJson json) => AnsightChannel(
        id: _int(json['id']),
        name: _string(json['name']),
        unit: _nullableString(json['unit']),
        type: _nullableString(json['type']) ?? 'custom',
        colorHex: _nullableString(json['colorHex']),
        source: _nullableString(json['source']),
        group: _nullableString(json['group']),
        kind: _nullableString(json['kind']),
      );
}

class AnsightOperationResult {
  const AnsightOperationResult({
    required this.success,
    required this.message,
    this.errorCode,
    this.data = const <String, Object?>{},
  });

  final bool success;
  final String message;
  final String? errorCode;
  final AnsightJson data;

  factory AnsightOperationResult.fromJson(AnsightJson json) =>
      AnsightOperationResult(
        success: _bool(json['success']),
        message: _nullableString(json['message']) ?? '',
        errorCode: _nullableString(json['errorCode']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightHostConnectionResult {
  const AnsightHostConnectionResult({
    required this.success,
    required this.message,
    this.kind,
    this.source,
    this.reasonCode,
    this.accepted,
    this.sessionId,
    this.configId,
    this.appId,
    this.resolvedHostAddress,
    this.usedEmbeddedDeveloperPairing,
    this.discoverySource,
    this.hostId,
    this.hostName,
    this.data = const <String, Object?>{},
  });

  final bool success;
  final String message;
  final String? kind;
  final String? source;
  final String? reasonCode;
  final bool? accepted;
  final String? sessionId;
  final String? configId;
  final String? appId;
  final String? resolvedHostAddress;
  final bool? usedEmbeddedDeveloperPairing;
  final String? discoverySource;
  final String? hostId;
  final String? hostName;
  final AnsightJson data;

  factory AnsightHostConnectionResult.fromJson(AnsightJson json) =>
      AnsightHostConnectionResult(
        success: _bool(json['success']),
        message: _nullableString(json['message']) ?? '',
        kind: _nullableString(json['kind']),
        source: _nullableString(json['source']),
        reasonCode: _nullableString(json['reasonCode']),
        accepted: _nullableBool(json['accepted']),
        sessionId: _nullableString(json['sessionId']),
        configId: _nullableString(json['configId']),
        appId: _nullableString(json['appId']),
        resolvedHostAddress: _nullableString(json['resolvedHostAddress']),
        usedEmbeddedDeveloperPairing: _nullableBool(
          json['usedEmbeddedDeveloperPairing'],
        ),
        discoverySource: _nullableString(json['discoverySource']),
        hostId: _nullableString(json['hostId']),
        hostName: _nullableString(json['hostName']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightHostConnectionStatus {
  const AnsightHostConnectionStatus({
    required this.isRuntimeActive,
    required this.isConnected,
    required this.connectionState,
    required this.hasCachedSession,
    required this.hasSavedConfig,
    required this.hasBundledConfig,
    required this.summaryKind,
    required this.summaryMessage,
    this.data = const <String, Object?>{},
  });

  final bool isRuntimeActive;
  final bool isConnected;
  final String connectionState;
  final bool hasCachedSession;
  final bool hasSavedConfig;
  final bool hasBundledConfig;
  final String summaryKind;
  final String summaryMessage;
  final AnsightJson data;

  factory AnsightHostConnectionStatus.fromJson(AnsightJson json) =>
      AnsightHostConnectionStatus(
        isRuntimeActive: _bool(json['isRuntimeActive']),
        isConnected: _bool(json['isConnected']),
        connectionState: _nullableString(json['connectionState']) ?? 'unknown',
        hasCachedSession: _bool(json['hasCachedSession']),
        hasSavedConfig: _bool(json['hasSavedConfig']),
        hasBundledConfig: _bool(json['hasBundledConfig']),
        summaryKind: _nullableString(json['summaryKind']) ?? 'unknown',
        summaryMessage: _nullableString(json['summaryMessage']) ?? '',
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightHostConnectionCapabilities {
  const AnsightHostConnectionCapabilities({
    required this.canConnectUsingSavedConfig,
    required this.canConnectUsingBundledConfig,
    required this.canChooseConfigFile,
    required this.canScanConfigQrCode,
    required this.canClearSavedConfigs,
    this.data = const <String, Object?>{},
  });

  final bool canConnectUsingSavedConfig;
  final bool canConnectUsingBundledConfig;
  final bool canChooseConfigFile;
  final bool canScanConfigQrCode;
  final bool canClearSavedConfigs;
  final AnsightJson data;

  factory AnsightHostConnectionCapabilities.fromJson(AnsightJson json) =>
      AnsightHostConnectionCapabilities(
        canConnectUsingSavedConfig: _bool(json['canConnectUsingSavedConfig']),
        canConnectUsingBundledConfig: _bool(
          json['canConnectUsingBundledConfig'],
        ),
        canChooseConfigFile: _bool(json['canChooseConfigFile']),
        canScanConfigQrCode: _bool(json['canScanConfigQrCode']),
        canClearSavedConfigs: _bool(json['canClearSavedConfigs']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightRecordedMetric {
  const AnsightRecordedMetric({
    required this.value,
    required this.channel,
    this.recordedAtUtc,
    this.sequence,
    this.data = const <String, Object?>{},
  });

  final int value;
  final int channel;
  final DateTime? recordedAtUtc;
  final int? sequence;
  final AnsightJson data;

  factory AnsightRecordedMetric.fromJson(AnsightJson json) =>
      AnsightRecordedMetric(
        value: _int(json['value']),
        channel: _int(json['channel']),
        recordedAtUtc: _dateTime(json['recordedAtUtc'] ?? json['timestampUtc']),
        sequence: _nullableInt(json['sequence']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightRecordedEvent {
  const AnsightRecordedEvent({
    required this.label,
    this.type,
    this.details,
    this.channel,
    this.recordedAtUtc,
    this.sequence,
    this.data = const <String, Object?>{},
  });

  final String label;
  final String? type;
  final String? details;
  final int? channel;
  final DateTime? recordedAtUtc;
  final int? sequence;
  final AnsightJson data;

  factory AnsightRecordedEvent.fromJson(AnsightJson json) =>
      AnsightRecordedEvent(
        label: _nullableString(json['label']) ?? '',
        type: _nullableString(json['type']),
        details: _nullableString(json['details']),
        channel: _nullableInt(json['channel']),
        recordedAtUtc: _dateTime(json['recordedAtUtc'] ?? json['timestampUtc']),
        sequence: _nullableInt(json['sequence']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

class AnsightDebugSnapshot {
  const AnsightDebugSnapshot({
    required this.initialized,
    required this.active,
    required this.sessionOpen,
    required this.metricsRecorded,
    required this.eventsRecorded,
    required this.registeredTools,
    required this.channels,
    required this.connectionStatus,
    this.lifecycleState,
    this.touchesRecorded,
    this.touchesCaptured,
    this.touchesSent,
    this.executableTools,
    this.sessionMessage,
    this.lastMetric,
    this.lastEvent,
    this.data = const <String, Object?>{},
  });

  final bool initialized;
  final bool active;
  final bool sessionOpen;
  final String? lifecycleState;
  final int metricsRecorded;
  final int eventsRecorded;
  final int? touchesRecorded;
  final int? touchesCaptured;
  final int? touchesSent;
  final int registeredTools;
  final int? executableTools;
  final String? sessionMessage;
  final List<AnsightChannel> channels;
  final AnsightHostConnectionStatus connectionStatus;
  final AnsightRecordedMetric? lastMetric;
  final AnsightRecordedEvent? lastEvent;
  final AnsightJson data;

  factory AnsightDebugSnapshot.fromJson(AnsightJson json) {
    final connection = _jsonMap(json['connectionStatus']);
    return AnsightDebugSnapshot(
      initialized: _bool(json['initialized']),
      active: _bool(json['active']),
      sessionOpen: _bool(json['sessionOpen']),
      lifecycleState: _nullableString(json['lifecycleState']),
      metricsRecorded: _int(json['metricsRecorded']),
      eventsRecorded: _int(json['eventsRecorded']),
      touchesRecorded: _nullableInt(json['touchesRecorded']),
      touchesCaptured: _nullableInt(json['touchesCaptured']),
      touchesSent: _nullableInt(json['touchesSent']),
      registeredTools: _int(json['registeredTools']),
      executableTools: _nullableInt(json['executableTools']),
      sessionMessage: _nullableString(json['sessionMessage']),
      channels: _jsonList(
        json['channels'],
      ).map(_jsonMap).map(AnsightChannel.fromJson).toList(growable: false),
      connectionStatus: AnsightHostConnectionStatus.fromJson(connection),
      lastMetric: json['lastMetric'] == null
          ? null
          : AnsightRecordedMetric.fromJson(_jsonMap(json['lastMetric'])),
      lastEvent: json['lastEvent'] == null
          ? null
          : AnsightRecordedEvent.fromJson(_jsonMap(json['lastEvent'])),
      data: Map<String, Object?>.unmodifiable(json),
    );
  }
}

class AnsightLogEntry {
  const AnsightLogEntry({
    required this.level,
    required this.message,
    this.platform,
    this.error,
    this.data = const <String, Object?>{},
  });

  final String level;
  final String message;
  final String? platform;
  final String? error;
  final AnsightJson data;

  factory AnsightLogEntry.fromJson(AnsightJson json) => AnsightLogEntry(
        level: _nullableString(json['level']) ?? 'info',
        message: _nullableString(json['message']) ?? '',
        platform: _nullableString(json['platform']),
        error: _nullableString(json['error']),
        data: Map<String, Object?>.unmodifiable(json),
      );
}

AnsightJson ansightJsonMap(Object? value) => _jsonMap(value);

List<Object?> ansightJsonList(Object? value) => _jsonList(value);

String _string(Object? value) => value?.toString() ?? '';

String? _nullableString(Object? value) {
  if (value == null) {
    return null;
  }
  return value.toString();
}

bool _bool(Object? value) => value is bool ? value : false;

bool? _nullableBool(Object? value) => value is bool ? value : null;

int _int(Object? value) => _nullableInt(value) ?? 0;

int? _nullableInt(Object? value) {
  if (value is int) {
    return value;
  }
  if (value is num) {
    return value.toInt();
  }
  return int.tryParse(value?.toString() ?? '');
}

DateTime? _dateTime(Object? value) =>
    DateTime.tryParse(value?.toString() ?? '');

AnsightJson _jsonMap(Object? value) {
  if (value is Map<String, Object?>) {
    return value;
  }
  if (value is Map) {
    return value.map(
      (Object? key, Object? item) => MapEntry(key.toString(), item),
    );
  }
  return <String, Object?>{};
}

List<Object?> _jsonList(Object? value) =>
    value is List ? List<Object?>.from(value) : const <Object?>[];
