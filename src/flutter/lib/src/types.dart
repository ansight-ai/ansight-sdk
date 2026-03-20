class AnsightOptions {
  const AnsightOptions({
    this.sampleFrequencyMilliseconds = 500,
    this.retentionPeriodSeconds = 600,
    this.enableFramesPerSecond = true,
    this.additionalChannels = const [],
  });

  final int sampleFrequencyMilliseconds;
  final int retentionPeriodSeconds;
  final bool enableFramesPerSecond;
  final List<AnsightChannel> additionalChannels;

  Map<String, Object?> toMap() {
    return {
      "sampleFrequencyMilliseconds": sampleFrequencyMilliseconds,
      "retentionPeriodSeconds": retentionPeriodSeconds,
      "enableFramesPerSecond": enableFramesPerSecond,
      "additionalChannels": additionalChannels.map((channel) => channel.toMap()).toList(),
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
  });

  final String clientName;
  final String manualHostAddress;
  final String? expectedAppId;
  final Map<String, String> profileOverride;

  Map<String, Object?> toMap() => {
        "clientName": clientName,
        "manualHostAddress": manualHostAddress,
        "expectedAppId": expectedAppId,
        "profileOverride": profileOverride,
      };
}

class OpenSessionResult {
  const OpenSessionResult({
    required this.success,
    required this.message,
    this.sessionId,
  });

  final bool success;
  final String message;
  final String? sessionId;

  factory OpenSessionResult.fromMap(Map<Object?, Object?> map) {
    return OpenSessionResult(
      success: map["success"] as bool? ?? false,
      message: map["message"] as String? ?? "",
      sessionId: map["sessionId"] as String?,
    );
  }
}

class AnsightToolDescriptor {
  const AnsightToolDescriptor({
    required this.id,
    required this.name,
    this.scope = "Read",
  });

  final String id;
  final String name;
  final String scope;

  Map<String, Object?> toMap() => {
        "id": id,
        "name": name,
        "scope": scope,
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
    this.sessionMessage,
    this.lastMetric,
    this.lastEvent,
  });

  final bool initialized;
  final bool active;
  final bool sessionOpen;
  final int metricsRecorded;
  final int eventsRecorded;
  final int registeredTools;
  final String? sessionMessage;
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
      sessionMessage: map["sessionMessage"] as String?,
      lastMetric: map["lastMetric"] as Map<Object?, Object?>?,
      lastEvent: map["lastEvent"] as Map<Object?, Object?>?,
    );
  }
}
