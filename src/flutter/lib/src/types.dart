class AnsightOptions {
  const AnsightOptions({
    this.sampleFrequencyMilliseconds = 500,
    this.retentionPeriodSeconds = 600,
    this.enableFramesPerSecond = true,
    this.additionalChannels = const [],
    this.toolAccess,
  });

  final int sampleFrequencyMilliseconds;
  final int retentionPeriodSeconds;
  final bool enableFramesPerSecond;
  final List<AnsightChannel> additionalChannels;
  final String? toolAccess;

  Map<String, Object?> toMap() {
    return {
      "sampleFrequencyMilliseconds": sampleFrequencyMilliseconds,
      "retentionPeriodSeconds": retentionPeriodSeconds,
      "enableFramesPerSecond": enableFramesPerSecond,
      "additionalChannels": additionalChannels.map((channel) => channel.toMap()).toList(),
      "toolAccess": toolAccess,
    };
  }
}

class AnsightChannel {
  const AnsightChannel({
    required this.id,
    required this.name,
    this.colorHex,
  });

  final int id;
  final String name;
  final String? colorHex;

  Map<String, Object?> toMap() => {
        "id": id,
        "name": name,
        "colorHex": colorHex,
      };
}

enum AnsightEventType {
  event("Event"),
  debug("Debug"),
  info("Info"),
  warning("Warning"),
  error("Error"),
  exception("Exception"),
  gc("Gc"),
  navigation("Navigation");

  const AnsightEventType(this.wireValue);
  final String wireValue;
}

class PairingOpenOptions {
  const PairingOpenOptions({
    required this.clientName,
    required this.manualHostAddress,
    this.expectedAppId,
    this.profileOverride = const {},
    this.allowDiscoveryHintHostFallback = true,
  });

  final String clientName;
  final String manualHostAddress;
  final String? expectedAppId;
  final Map<String, String> profileOverride;
  final bool allowDiscoveryHintHostFallback;

  Map<String, Object?> toMap() => {
        "clientName": clientName,
        "manualHostAddress": manualHostAddress,
        "expectedAppId": expectedAppId,
        "profileOverride": profileOverride,
        "allowDiscoveryHintHostFallback": allowDiscoveryHintHostFallback,
      };
}

class OpenSessionResult {
  const OpenSessionResult({
    required this.success,
    required this.message,
    this.sessionId,
    this.configId,
    this.appId,
    this.resolvedHostAddress,
    this.usedEmbeddedDeveloperPairing = false,
    this.discoverySource,
  });

  final bool success;
  final String message;
  final String? sessionId;
  final String? configId;
  final String? appId;
  final String? resolvedHostAddress;
  final bool usedEmbeddedDeveloperPairing;
  final String? discoverySource;

  factory OpenSessionResult.fromMap(Map<Object?, Object?> map) {
    return OpenSessionResult(
      success: map["success"] as bool? ?? false,
      message: map["message"] as String? ?? "",
      sessionId: map["sessionId"] as String?,
      configId: map["configId"] as String?,
      appId: map["appId"] as String?,
      resolvedHostAddress: map["resolvedHostAddress"] as String?,
      usedEmbeddedDeveloperPairing: map["usedEmbeddedDeveloperPairing"] as bool? ?? false,
      discoverySource: map["discoverySource"] as String?,
    );
  }
}

class AnsightToolDescriptor {
  const AnsightToolDescriptor({
    required this.id,
    required this.name,
    this.description = "",
    this.category = "Diagnostics",
    this.scope = "Read",
    this.keywords = "",
  });

  final String id;
  final String name;
  final String description;
  final String category;
  final String scope;
  final String keywords;

  Map<String, Object?> toMap() => {
        "id": id,
        "name": name,
        "description": description,
        "category": category,
        "scope": scope,
        "keywords": keywords,
      };
}

class AnsightDebugSnapshot {
  const AnsightDebugSnapshot({
    required this.initialized,
    required this.active,
    required this.sessionOpen,
    required this.metricsRecorded,
    required this.eventsRecorded,
    required this.registeredTools,
    required this.executableTools,
    required this.toolDiscoveryEnabled,
    required this.toolExecutionEnabled,
    required this.embeddedDeveloperPairingAvailable,
    required this.detectedBundledTools,
    this.sessionMessage,
    this.lastPairingConfigId,
    this.resolvedHostAddress,
    this.lastMetric,
    this.lastEvent,
  });

  final bool initialized;
  final bool active;
  final bool sessionOpen;
  final int metricsRecorded;
  final int eventsRecorded;
  final int registeredTools;
  final int executableTools;
  final bool toolDiscoveryEnabled;
  final bool toolExecutionEnabled;
  final bool embeddedDeveloperPairingAvailable;
  final List<String> detectedBundledTools;
  final String? sessionMessage;
  final String? lastPairingConfigId;
  final String? resolvedHostAddress;
  final Map<Object?, Object?>? lastMetric;
  final Map<Object?, Object?>? lastEvent;

  factory AnsightDebugSnapshot.fromMap(Map<Object?, Object?> map) {
    return AnsightDebugSnapshot(
      initialized: map["initialized"] as bool? ?? false,
      active: map["active"] as bool? ?? false,
      sessionOpen: map["sessionOpen"] as bool? ?? false,
      metricsRecorded: map["metricsRecorded"] as int? ?? 0,
      eventsRecorded: map["eventsRecorded"] as int? ?? 0,
      registeredTools: map["registeredTools"] as int? ?? 0,
      executableTools: map["executableTools"] as int? ?? 0,
      toolDiscoveryEnabled: map["toolDiscoveryEnabled"] as bool? ?? false,
      toolExecutionEnabled: map["toolExecutionEnabled"] as bool? ?? false,
      embeddedDeveloperPairingAvailable: map["embeddedDeveloperPairingAvailable"] as bool? ?? false,
      detectedBundledTools: (map["detectedBundledTools"] as List<Object?>? ?? const [])
          .map((value) => value.toString())
          .toList(),
      sessionMessage: map["sessionMessage"] as String?,
      lastPairingConfigId: map["lastPairingConfigId"] as String?,
      resolvedHostAddress: map["resolvedHostAddress"] as String?,
      lastMetric: map["lastMetric"] as Map<Object?, Object?>?,
      lastEvent: map["lastEvent"] as Map<Object?, Object?>?,
    );
  }
}
