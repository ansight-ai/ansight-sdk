import 'dart:io';

import 'package:flutter/foundation.dart';

import 'ansight_models.dart';

const String ansightFlutterSdkVersion = '1.3.0-preview.12';
const String _flutterVersion = String.fromEnvironment('FLUTTER_VERSION');

const String flutterPropertyGroup = 'flutter';
const String localizationPropertyGroup = 'localization';

String _buildMode() {
  if (kReleaseMode) {
    return 'release';
  }
  if (kProfileMode) {
    return 'profile';
  }
  return 'debug';
}

Map<String, Map<String, String>> createAutomaticSessionProperties() {
  final locale = PlatformDispatcher.instance.locale;
  final now = DateTime.now();
  final dartVersion = Platform.version.split(RegExp(r'\s+')).first;
  final flutter = <String, String>{
    'sdkVersion': ansightFlutterSdkVersion,
    'dartVersion': dartVersion,
    'platform': Platform.operatingSystem,
    'runtimeLanguage': 'dart',
    'buildMode': _buildMode(),
    'runtimeMode': kDebugMode ? 'jit' : 'aot',
    'developmentMode': kDebugMode ? 'true' : 'false',
    if (_flutterVersion.trim().isNotEmpty)
      'flutterVersion': _flutterVersion.trim(),
  };
  final localization = <String, String>{
    'locale': locale.toLanguageTag(),
    'language': locale.languageCode,
    if (locale.countryCode?.isNotEmpty == true) 'region': locale.countryCode!,
    'timeZone': now.timeZoneName,
    'utcOffsetMinutes': now.timeZoneOffset.inMinutes.toString(),
  };
  return <String, Map<String, String>>{
    flutterPropertyGroup: flutter,
    localizationPropertyGroup: localization,
  };
}

Map<String, Map<String, String>> _stringProperties(Object? rawProperties) {
  if (rawProperties is! Map) {
    return <String, Map<String, String>>{};
  }
  return rawProperties.map(
    (Object? rawGroup, Object? rawValues) => MapEntry(
      rawGroup.toString(),
      rawValues is Map
          ? rawValues.map(
              (Object? rawKey, Object? rawValue) =>
                  MapEntry(rawKey.toString(), rawValue.toString()),
            )
          : <String, String>{},
    ),
  );
}

Map<String, Map<String, String>> mergeSessionProperties(
  Map<String, Map<String, String>> automaticProperties,
  Object? customProperties,
) {
  final merged = automaticProperties.map(
    (String group, Map<String, String> properties) =>
        MapEntry(group, Map<String, String>.from(properties)),
  );
  for (final entry in _stringProperties(customProperties).entries) {
    merged.putIfAbsent(entry.key, () => <String, String>{}).addAll(entry.value);
  }
  return merged;
}

AnsightJson withAutomaticSessionProperties(AnsightJson options) {
  final result = Map<String, Object?>.from(options);
  result['customProperties'] = mergeSessionProperties(
    createAutomaticSessionProperties(),
    options['customProperties'],
  );
  return result;
}

String? automaticSessionPropertyValue(String group, String key) {
  final properties = createAutomaticSessionProperties()[group];
  return properties == null ? null : properties[key];
}
