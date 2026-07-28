import 'ansight_models.dart';

class AnsightDefaultMemoryChannels {
  const AnsightDefaultMemoryChannels({
    this.managedHeap = true,
    this.physicalFootprint = true,
    this.residentSetSize = true,
    this.javaHeap = true,
    this.nativeHeap = true,
    this.rss = true,
  });

  final bool managedHeap;
  final bool physicalFootprint;
  final bool residentSetSize;
  final bool javaHeap;
  final bool nativeHeap;
  final bool rss;

  AnsightJson toJson() => <String, Object?>{
        'managedHeap': managedHeap,
        'physicalFootprint': physicalFootprint,
        'residentSetSize': residentSetSize,
        'javaHeap': javaHeap,
        'nativeHeap': nativeHeap,
        'rss': rss,
      };
}

class AnsightSessionJpegCaptureOptions {
  const AnsightSessionJpegCaptureOptions({
    this.intervalMilliseconds = 2000,
    this.quality = 60,
    this.maxWidth = 480,
    this.captureGpuBackedSurfaces = true,
  });

  final int intervalMilliseconds;
  final int quality;
  final int? maxWidth;
  final bool captureGpuBackedSurfaces;

  AnsightJson toJson() => <String, Object?>{
        'intervalMilliseconds': intervalMilliseconds,
        'quality': quality,
        'maxWidth': maxWidth,
        'captureGpuBackedSurfaces': captureGpuBackedSurfaces,
      };
}

class AnsightTouchCaptureOptions {
  const AnsightTouchCaptureOptions({
    this.captureMoveEvents = true,
    this.captureCancelEvents = true,
    this.moveCaptureDistanceThreshold = 8,
    this.moveCaptureFramesPerSecond = 20,
  });

  final bool captureMoveEvents;
  final bool captureCancelEvents;
  final double moveCaptureDistanceThreshold;
  final int moveCaptureFramesPerSecond;

  AnsightJson toJson() => <String, Object?>{
        'captureMoveEvents': captureMoveEvents,
        'captureCancelEvents': captureCancelEvents,
        'moveCaptureDistanceThreshold': moveCaptureDistanceThreshold,
        'moveCaptureFramesPerSecond': moveCaptureFramesPerSecond,
      };
}

class AnsightLifecycleCaptureOptions {
  const AnsightLifecycleCaptureOptions({
    this.enabled = true,
    this.captureAppLifecycle = true,
    this.captureScreenViews = true,
    this.minimumScreenViewIntervalMilliseconds = 250,
  });

  final bool enabled;
  final bool captureAppLifecycle;
  final bool captureScreenViews;
  final int minimumScreenViewIntervalMilliseconds;

  AnsightJson toJson() => <String, Object?>{
        'enabled': enabled,
        'captureAppLifecycle': captureAppLifecycle,
        'captureScreenViews': captureScreenViews,
        'minimumScreenViewIntervalMilliseconds':
            minimumScreenViewIntervalMilliseconds,
      };
}

class AnsightHostAutoProbeOptions {
  const AnsightHostAutoProbeOptions({
    this.enabled = true,
    this.initialDelayMilliseconds = 1000,
    this.probeIntervalMilliseconds = 5000,
    this.reconnectDelayMilliseconds = 10000,
    this.clientName,
  });

  final bool enabled;
  final int initialDelayMilliseconds;
  final int probeIntervalMilliseconds;
  final int reconnectDelayMilliseconds;
  final String? clientName;

  AnsightJson toJson() => <String, Object?>{
        'enabled': enabled,
        'initialDelayMilliseconds': initialDelayMilliseconds,
        'probeIntervalMilliseconds': probeIntervalMilliseconds,
        'reconnectDelayMilliseconds': reconnectDelayMilliseconds,
        if (clientName != null) 'clientName': clientName,
      };
}

class AnsightHostConnectionOptions {
  const AnsightHostConnectionOptions({
    this.savedConfigKey,
    this.bundledConfigJson,
    this.bundledDeveloperConfigJson,
    this.discoveryPort,
    this.connectionProfileRetentionSeconds,
  });

  final String? savedConfigKey;
  final String? bundledConfigJson;
  final String? bundledDeveloperConfigJson;
  final int? discoveryPort;
  final int? connectionProfileRetentionSeconds;

  AnsightJson toJson() => <String, Object?>{
        if (savedConfigKey != null) 'savedConfigKey': savedConfigKey,
        if (bundledConfigJson != null) 'bundledConfigJson': bundledConfigJson,
        if (bundledDeveloperConfigJson != null)
          'bundledDeveloperConfigJson': bundledDeveloperConfigJson,
        if (discoveryPort != null) 'discoveryPort': discoveryPort,
        if (connectionProfileRetentionSeconds != null)
          'connectionProfileRetentionSeconds':
              connectionProfileRetentionSeconds,
      };
}

class AnsightNativeToolRoot {
  const AnsightNativeToolRoot({required this.alias, required this.path});

  final String alias;
  final String path;

  AnsightJson toJson() => <String, Object?>{'alias': alias, 'path': path};
}

class AnsightFileSystemToolsOptions {
  const AnsightFileSystemToolsOptions({
    this.additionalRoots = const <AnsightNativeToolRoot>[],
  });

  final List<AnsightNativeToolRoot> additionalRoots;

  AnsightJson toJson() => <String, Object?>{
        'additionalRoots': additionalRoots
            .map((AnsightNativeToolRoot root) => root.toJson())
            .toList(growable: false),
      };
}

class AnsightDatabaseToolsOptions {
  const AnsightDatabaseToolsOptions({
    this.additionalRoots = const <AnsightNativeToolRoot>[],
    this.includePlatformRoots = true,
  });

  final List<AnsightNativeToolRoot> additionalRoots;
  final bool includePlatformRoots;

  AnsightJson toJson() => <String, Object?>{
        'additionalRoots': additionalRoots
            .map((AnsightNativeToolRoot root) => root.toJson())
            .toList(growable: false),
        'includePlatformRoots': includePlatformRoots,
      };
}

class AnsightPreferencesToolsOptions {
  const AnsightPreferencesToolsOptions({
    this.defaultStore,
    this.allowedStores = const <String>[],
    this.allowedKeys = const <String>[],
    this.allowedKeyPrefixes = const <String>[],
  });

  final String? defaultStore;
  final List<String> allowedStores;
  final List<String> allowedKeys;
  final List<String> allowedKeyPrefixes;

  AnsightJson toJson() => <String, Object?>{
        if (defaultStore != null) 'defaultStore': defaultStore,
        'allowedStores': allowedStores,
        'allowedKeys': allowedKeys,
        'allowedKeyPrefixes': allowedKeyPrefixes,
      };
}

class AnsightReflectionToolsOptions {
  const AnsightReflectionToolsOptions({
    this.includeBuiltInRoots = true,
    this.allowedRootIds = const <String>[],
    this.allowedTypePrefixes = const <String>[],
  });

  final bool includeBuiltInRoots;
  final List<String> allowedRootIds;
  final List<String> allowedTypePrefixes;

  AnsightJson toJson() => <String, Object?>{
        'includeBuiltInRoots': includeBuiltInRoots,
        'allowedRootIds': allowedRootIds,
        'allowedTypePrefixes': allowedTypePrefixes,
      };
}

class AnsightSecureStorageToolsOptions {
  const AnsightSecureStorageToolsOptions({
    this.appleService,
    this.preferencesName,
    this.allowedKeys = const <String>[],
    this.allowedKeyPrefixes = const <String>[],
  });

  final String? appleService;
  final String? preferencesName;
  final List<String> allowedKeys;
  final List<String> allowedKeyPrefixes;

  AnsightJson toJson() => <String, Object?>{
        if (appleService != null) 'appleService': appleService,
        if (preferencesName != null) 'preferencesName': preferencesName,
        'allowedKeys': allowedKeys,
        'allowedKeyPrefixes': allowedKeyPrefixes,
      };
}

class AnsightRemoteToolsOptions {
  const AnsightRemoteToolsOptions({
    this.visualTree,
    this.fileSystem,
    this.database,
    this.preferences,
    this.reflection,
    this.secureStorage,
  });

  final bool? visualTree;
  final AnsightFileSystemToolsOptions? fileSystem;
  final AnsightDatabaseToolsOptions? database;
  final AnsightPreferencesToolsOptions? preferences;
  final AnsightReflectionToolsOptions? reflection;
  final AnsightSecureStorageToolsOptions? secureStorage;

  AnsightJson toJson() => <String, Object?>{
        if (visualTree != null) 'visualTree': visualTree,
        if (fileSystem != null) 'fileSystem': fileSystem!.toJson(),
        if (database != null) 'database': database!.toJson(),
        if (preferences != null) 'preferences': preferences!.toJson(),
        if (reflection != null) 'reflection': reflection!.toJson(),
        if (secureStorage != null) 'secureStorage': secureStorage!.toJson(),
      };
}

class AnsightOptions {
  const AnsightOptions({
    this.useNativeAllInOneDefaults = false,
    this.pairingConfigJson,
    this.clientName,
    this.sampleFrequencyMilliseconds,
    this.retentionPeriodSeconds,
    this.enableFramesPerSecond,
    this.enableBatteryLevel,
    this.defaultMemoryChannels,
    this.sessionJpegCapture,
    this.sessionJpegCaptureEnabled,
    this.touchCapture,
    this.touchCaptureEnabled,
    this.lifecycleCapture,
    this.toolGuard,
    this.customProperties = const <String, Map<String, String>>{},
    this.hostAutoProbe,
    this.hostConnection,
    this.remoteTools,
    this.additionalChannels = const <AnsightChannel>[],
  }) : _rawJson = null;

  AnsightOptions._fromBuilder(AnsightJson json)
      : _rawJson = Map<String, Object?>.unmodifiable(json),
        useNativeAllInOneDefaults = false,
        pairingConfigJson = null,
        clientName = null,
        sampleFrequencyMilliseconds = null,
        retentionPeriodSeconds = null,
        enableFramesPerSecond = null,
        enableBatteryLevel = null,
        defaultMemoryChannels = null,
        sessionJpegCapture = null,
        sessionJpegCaptureEnabled = null,
        touchCapture = null,
        touchCaptureEnabled = null,
        lifecycleCapture = null,
        toolGuard = null,
        customProperties = const <String, Map<String, String>>{},
        hostAutoProbe = null,
        hostConnection = null,
        remoteTools = null,
        additionalChannels = const <AnsightChannel>[];

  factory AnsightOptions.developer({
    String? pairingConfigJson,
    String? clientName,
    AnsightToolGuard toolGuard = AnsightToolGuard.readOnly,
  }) =>
      AnsightOptions(
        useNativeAllInOneDefaults: true,
        pairingConfigJson: pairingConfigJson,
        clientName: clientName,
        sampleFrequencyMilliseconds: 400,
        retentionPeriodSeconds: 120,
        enableFramesPerSecond: true,
        enableBatteryLevel: false,
        sessionJpegCapture: const AnsightSessionJpegCaptureOptions(),
        sessionJpegCaptureEnabled: true,
        touchCapture: const AnsightTouchCaptureOptions(),
        touchCaptureEnabled: true,
        toolGuard: toolGuard,
        hostAutoProbe: AnsightHostAutoProbeOptions(clientName: clientName),
        hostConnection: AnsightHostConnectionOptions(
          bundledDeveloperConfigJson: pairingConfigJson,
        ),
        remoteTools: const AnsightRemoteToolsOptions(visualTree: true),
      );

  final bool useNativeAllInOneDefaults;
  final String? pairingConfigJson;
  final String? clientName;
  final int? sampleFrequencyMilliseconds;
  final int? retentionPeriodSeconds;
  final bool? enableFramesPerSecond;
  final bool? enableBatteryLevel;
  final AnsightDefaultMemoryChannels? defaultMemoryChannels;
  final AnsightSessionJpegCaptureOptions? sessionJpegCapture;
  final bool? sessionJpegCaptureEnabled;
  final AnsightTouchCaptureOptions? touchCapture;
  final bool? touchCaptureEnabled;
  final AnsightLifecycleCaptureOptions? lifecycleCapture;
  final AnsightToolGuard? toolGuard;
  final Map<String, Map<String, String>> customProperties;
  final AnsightHostAutoProbeOptions? hostAutoProbe;
  final AnsightHostConnectionOptions? hostConnection;
  final AnsightRemoteToolsOptions? remoteTools;
  final List<AnsightChannel> additionalChannels;
  final AnsightJson? _rawJson;

  AnsightJson toJson() {
    final rawJson = _rawJson;
    if (rawJson != null) {
      return Map<String, Object?>.from(rawJson);
    }
    return <String, Object?>{
      'useNativeAllInOneDefaults': useNativeAllInOneDefaults,
      if (pairingConfigJson != null) 'pairingConfigJson': pairingConfigJson,
      if (clientName != null) 'clientName': clientName,
      if (sampleFrequencyMilliseconds != null)
        'sampleFrequencyMilliseconds': sampleFrequencyMilliseconds,
      if (retentionPeriodSeconds != null)
        'retentionPeriodSeconds': retentionPeriodSeconds,
      if (enableFramesPerSecond != null)
        'enableFramesPerSecond': enableFramesPerSecond,
      if (enableBatteryLevel != null) 'enableBatteryLevel': enableBatteryLevel,
      if (defaultMemoryChannels != null)
        'defaultMemoryChannels': defaultMemoryChannels!.toJson(),
      if (sessionJpegCaptureEnabled == false)
        'sessionJpegCapture': false
      else if (sessionJpegCapture != null)
        'sessionJpegCapture': sessionJpegCapture!.toJson(),
      if (touchCaptureEnabled == false)
        'touchCapture': false
      else if (touchCapture != null)
        'touchCapture': touchCapture!.toJson(),
      if (lifecycleCapture != null)
        'lifecycleCapture': lifecycleCapture!.toJson(),
      if (toolGuard != null) 'toolGuard': toolGuard!.wireName,
      if (customProperties.isNotEmpty) 'customProperties': customProperties,
      if (hostAutoProbe != null) 'hostAutoProbe': hostAutoProbe!.toJson(),
      if (hostConnection != null) 'hostConnection': hostConnection!.toJson(),
      if (remoteTools != null) 'remoteTools': remoteTools!.toJson(),
      if (additionalChannels.isNotEmpty)
        'additionalChannels': additionalChannels
            .map((AnsightChannel channel) => channel.toJson())
            .toList(growable: false),
    };
  }
}

class AnsightOptionsBuilder {
  AnsightOptionsBuilder([AnsightOptions options = const AnsightOptions()])
      : _json = Map<String, Object?>.from(options.toJson());

  AnsightJson _json;

  AnsightOptionsBuilder withAnsightDefaults() {
    _json = AnsightOptions.developer().toJson();
    return this;
  }

  AnsightOptionsBuilder withAnsightSdk() {
    withAnsightDefaults();
    return withToolGuard(AnsightToolGuard.fullAccess);
  }

  AnsightOptionsBuilder withToolGuard(AnsightToolGuard guard) {
    _json['toolGuard'] = guard.wireName;
    return this;
  }

  AnsightOptionsBuilder withReadOnlyToolAccess() =>
      withToolGuard(AnsightToolGuard.readOnly);

  AnsightOptionsBuilder withReadWriteToolAccess() =>
      withToolGuard(AnsightToolGuard.readWrite);

  AnsightOptionsBuilder withAllToolAccess() =>
      withToolGuard(AnsightToolGuard.fullAccess);

  AnsightOptionsBuilder withToolsDisabled() =>
      withToolGuard(AnsightToolGuard.disabled);

  AnsightOptionsBuilder withSessionJpegCapture([
    AnsightSessionJpegCaptureOptions options =
        const AnsightSessionJpegCaptureOptions(),
  ]) {
    _json['sessionJpegCapture'] = options.toJson();
    return this;
  }

  AnsightOptionsBuilder withoutSessionJpegCapture() {
    _json['sessionJpegCapture'] = false;
    return this;
  }

  AnsightOptionsBuilder withTouchCapture([
    AnsightTouchCaptureOptions options = const AnsightTouchCaptureOptions(),
  ]) {
    _json['touchCapture'] = options.toJson();
    return this;
  }

  AnsightOptionsBuilder withoutTouchCapture() {
    _json['touchCapture'] = false;
    return this;
  }

  AnsightOptionsBuilder withHostAutoProbe([
    AnsightHostAutoProbeOptions options = const AnsightHostAutoProbeOptions(),
  ]) {
    _json['hostAutoProbe'] = options.toJson();
    return this;
  }

  AnsightOptionsBuilder withoutHostAutoProbe() {
    _json['hostAutoProbe'] = const <String, Object?>{'enabled': false};
    return this;
  }

  AnsightOptionsBuilder withHostConnection(
    AnsightHostConnectionOptions options,
  ) {
    _json['hostConnection'] = options.toJson();
    return this;
  }

  AnsightOptionsBuilder withRemoteTools(AnsightRemoteToolsOptions options) {
    _json['remoteTools'] = options.toJson();
    return this;
  }

  AnsightOptionsBuilder registerCustomProperty(
    String group,
    String key,
    Object? value,
  ) {
    final rawGroups = _json['customProperties'];
    final groups = rawGroups is Map
        ? rawGroups.map(
            (Object? rawGroup, Object? rawProperties) => MapEntry(
              rawGroup.toString(),
              rawProperties is Map
                  ? rawProperties.map(
                      (Object? rawKey, Object? rawValue) =>
                          MapEntry(rawKey.toString(), rawValue.toString()),
                    )
                  : <String, String>{},
            ),
          )
        : <String, Map<String, String>>{};
    groups.putIfAbsent(group, () => <String, String>{})[key] =
        value?.toString() ?? '';
    _json['customProperties'] = groups;
    return this;
  }

  AnsightOptions build() => AnsightOptions._fromBuilder(_json);
}

AnsightOptionsBuilder createOptionsBuilder([
  AnsightOptions options = const AnsightOptions(),
]) =>
    AnsightOptionsBuilder(options);
