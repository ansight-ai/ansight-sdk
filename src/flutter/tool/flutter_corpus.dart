import 'dart:async';
import 'dart:convert';
import 'dart:io';

const String _validationTarget = 'lib/ansight_validation_main.dart';
const String _packageName = 'ansight_flutter';
const String _legacyFlutterVersion = '3.0.5';
const String _legacyKotlinVersion = '1.8.22';
const String _modernKotlinVersion = '2.3.20';
const String _legacyGradleVersion = '7.4';
const String _legacyAndroidGradlePluginVersion = '7.1.2';
const int _minimumAndroidSdk = 24;
const int _compileSdk = 34;
const int _minimumIosVersion = 15;

Future<void> main(List<String> arguments) async {
  final options = CorpusOptions.parse(arguments);
  if (options.showHelp) {
    _printUsage();
    return;
  }

  final matrix = File(options.matrixPath);
  if (!matrix.existsSync()) {
    stderr.writeln('Build matrix not found: ${matrix.path}');
    exitCode = 64;
    return;
  }

  final apps = _readApps(matrix, options);
  if (apps.isEmpty) {
    stderr.writeln('No Flutter apps matched the requested filters.');
    exitCode = 64;
    return;
  }

  stdout.writeln(
    '${options.command.label}: ${apps.length} Flutter app(s) from '
    '${matrix.path}',
  );

  if (options.command.integrates) {
    for (final app in apps) {
      _integrate(app, options);
      stdout.writeln('integrated  ${app.repo}');
    }
  }

  if (!options.command.validates) {
    return;
  }

  final results = <CorpusResult>[];
  for (var index = 0; index < apps.length; index += 1) {
    final app = apps[index];
    stdout.writeln(
      '[${index + 1}/${apps.length}] validating ${app.repo} '
      'with ${app.flutterLabel}',
    );
    results.add(await _validate(app, options));
  }

  _writeReports(results, options);
  final passed = results.where((result) => result.passed).length;
  stdout.writeln(
    'Corpus result: $passed/${results.length} passed. '
    'Reports: ${options.reportJsonPath}, ${options.reportMarkdownPath}',
  );
  if (passed != results.length) {
    exitCode = 1;
  }
}

enum CorpusCommand {
  integrate,
  validate,
  all;

  String get label {
    switch (this) {
      case CorpusCommand.integrate:
        return 'Integrating';
      case CorpusCommand.validate:
        return 'Validating';
      case CorpusCommand.all:
        return 'Integrating and validating';
    }
  }

  bool get integrates =>
      this == CorpusCommand.integrate || this == CorpusCommand.all;

  bool get validates =>
      this == CorpusCommand.validate || this == CorpusCommand.all;
}

class CorpusOptions {
  CorpusOptions({
    required this.command,
    required this.suiteRoot,
    required this.matrixPath,
    required this.sdkPath,
    required this.reportJsonPath,
    required this.reportMarkdownPath,
    required this.appFilters,
    required this.buildAndroid,
    required this.analyze,
    required this.commandTimeout,
    required this.showHelp,
  });

  factory CorpusOptions.parse(List<String> arguments) {
    final scriptDirectory = File.fromUri(Platform.script).parent;
    final packageDirectory = scriptDirectory.parent;
    final defaultSuiteRoot =
        '${packageDirectory.parent.parent.parent.path}/ansight-sdk-test-apps';

    var command = CorpusCommand.all;
    var suiteRoot = defaultSuiteRoot;
    String? matrixPath;
    String? sdkPath;
    String? reportJsonPath;
    String? reportMarkdownPath;
    var buildAndroid = true;
    var analyze = true;
    var commandTimeout = const Duration(minutes: 20);
    var showHelp = false;
    final appFilters = <String>[];

    for (final argument in arguments) {
      if (argument == 'integrate') {
        command = CorpusCommand.integrate;
      } else if (argument == 'validate') {
        command = CorpusCommand.validate;
      } else if (argument == 'all') {
        command = CorpusCommand.all;
      } else if (argument == '--no-build') {
        buildAndroid = false;
      } else if (argument == '--no-analyze') {
        analyze = false;
      } else if (argument == '--help' || argument == '-h') {
        showHelp = true;
      } else if (argument.startsWith('--suite-root=')) {
        suiteRoot = argument.substring('--suite-root='.length);
      } else if (argument.startsWith('--matrix=')) {
        matrixPath = argument.substring('--matrix='.length);
      } else if (argument.startsWith('--sdk-path=')) {
        sdkPath = argument.substring('--sdk-path='.length);
      } else if (argument.startsWith('--report-json=')) {
        reportJsonPath = argument.substring('--report-json='.length);
      } else if (argument.startsWith('--report-markdown=')) {
        reportMarkdownPath = argument.substring('--report-markdown='.length);
      } else if (argument.startsWith('--app=')) {
        appFilters.add(argument.substring('--app='.length));
      } else if (argument.startsWith('--timeout-minutes=')) {
        commandTimeout = Duration(
          minutes: int.parse(argument.substring('--timeout-minutes='.length)),
        );
      } else {
        throw FormatException('Unknown argument: $argument');
      }
    }

    final normalizedSuiteRoot =
        Directory(suiteRoot).absolute.resolveSymbolicLinksSync();
    final normalizedSdkPath = Directory(sdkPath ?? packageDirectory.path)
        .absolute
        .resolveSymbolicLinksSync();
    final reportDirectory = Directory('$normalizedSdkPath/validation');

    return CorpusOptions(
      command: command,
      suiteRoot: normalizedSuiteRoot,
      matrixPath: matrixPath ?? '$normalizedSuiteRoot/build-setup-matrix.json',
      sdkPath: normalizedSdkPath,
      reportJsonPath: reportJsonPath ??
          '${reportDirectory.path}/flutter-corpus-results.json',
      reportMarkdownPath: reportMarkdownPath ??
          '${reportDirectory.path}/flutter-corpus-results.md',
      appFilters: appFilters,
      buildAndroid: buildAndroid,
      analyze: analyze,
      commandTimeout: commandTimeout,
      showHelp: showHelp,
    );
  }

  final CorpusCommand command;
  final String suiteRoot;
  final String matrixPath;
  final String sdkPath;
  final String reportJsonPath;
  final String reportMarkdownPath;
  final List<String> appFilters;
  final bool buildAndroid;
  final bool analyze;
  final Duration commandTimeout;
  final bool showHelp;
}

class CorpusApp {
  CorpusApp({
    required this.repo,
    required this.root,
    required this.sdkConstraint,
    required this.fvmVersion,
    required this.flutterExecutable,
    required this.flutterLabel,
    required this.mainAcceptsArguments,
    required this.legacy,
  });

  final String repo;
  final String root;
  final String sdkConstraint;
  final String fvmVersion;
  final String flutterExecutable;
  final String flutterLabel;
  final bool mainAcceptsArguments;
  final bool legacy;

  bool get usesLegacyAndroidToolchain =>
      flutterLabel != 'stable' && _compareVersions(flutterLabel, '3.29.0') < 0;
}

class CommandResult {
  CommandResult({
    required this.name,
    required this.command,
    required this.exitCode,
    required this.duration,
    required this.logPath,
  });

  final String name;
  final List<String> command;
  final int exitCode;
  final Duration duration;
  final String logPath;

  bool get passed => exitCode == 0;

  Map<String, Object?> toJson() => <String, Object?>{
        'name': name,
        'command': command,
        'exitCode': exitCode,
        'durationMilliseconds': duration.inMilliseconds,
        'logPath': logPath,
      };
}

class CorpusResult {
  CorpusResult({
    required this.app,
    required this.startedAt,
    required this.finishedAt,
    required this.commands,
  });

  final CorpusApp app;
  final DateTime startedAt;
  final DateTime finishedAt;
  final List<CommandResult> commands;

  bool get passed => commands.every((command) => command.passed);

  CommandResult? get failedCommand {
    for (final command in commands) {
      if (!command.passed) {
        return command;
      }
    }
    return null;
  }

  Map<String, Object?> toJson() => <String, Object?>{
        'repo': app.repo,
        'appRoot': app.root,
        'flutter': app.flutterLabel,
        'sdkConstraint': app.sdkConstraint,
        'legacy': app.legacy,
        'target': _validationTarget,
        'passed': passed,
        'startedAt': startedAt.toUtc().toIso8601String(),
        'finishedAt': finishedAt.toUtc().toIso8601String(),
        'commands': commands.map((command) => command.toJson()).toList(),
      };
}

List<CorpusApp> _readApps(File matrix, CorpusOptions options) {
  final document =
      jsonDecode(matrix.readAsStringSync()) as Map<String, Object?>;
  final appsByPlatform = document['apps']! as Map<String, Object?>;
  final records = appsByPlatform['flutter']! as List<Object?>;
  final apps = <CorpusApp>[];

  for (final value in records) {
    final record = value! as Map<String, Object?>;
    final repo = record['repo']! as String;
    if (options.appFilters.isNotEmpty &&
        !options.appFilters.any(
          (filter) =>
              repo.toLowerCase().contains(filter.toLowerCase()) ||
              (record['appRoot']! as String)
                  .toLowerCase()
                  .contains(filter.toLowerCase()),
        )) {
      continue;
    }

    final relativeRoot = record['appRoot']! as String;
    final root = Directory('${options.suiteRoot}/$relativeRoot')
        .absolute
        .resolveSymbolicLinksSync();
    final sdkConstraint = (record['sdkConstraint'] as String?) ?? '';
    final fvmVersion = (record['fvmVersion'] as String?) ?? '';
    final legacy =
        sdkConstraint.contains('<3.0.0') || sdkConstraint.trim().isEmpty;
    final selectedVersion = _selectFlutterVersion(
      repo: repo,
      legacy: legacy,
      fvmVersion: fvmVersion,
    );
    final flutterExecutable = _flutterExecutable(selectedVersion);
    final mainFile = File('$root/lib/main.dart');
    final source = mainFile.readAsStringSync();

    apps.add(
      CorpusApp(
        repo: repo,
        root: root,
        sdkConstraint: sdkConstraint,
        fvmVersion: fvmVersion,
        flutterExecutable: flutterExecutable,
        flutterLabel: selectedVersion,
        mainAcceptsArguments: RegExp(
          r'main\s*\(\s*(?:List\s*<\s*String\s*>|List)\s+',
        ).hasMatch(source),
        legacy: legacy,
      ),
    );
  }

  return apps;
}

String _selectFlutterVersion({
  required String repo,
  required bool legacy,
  required String fvmVersion,
}) {
  if (legacy) {
    return _legacyFlutterVersion;
  }
  // The repository pin for flutter_catalog predates its current Dart >=3.11
  // constraint, so current stable is the only internally consistent choice.
  if (repo == 'X-Wei/flutter_catalog') {
    return 'stable';
  }
  if (repo == 'Anxcye/anx-reader') {
    return '3.38.8';
  }
  if (repo == 'abuanwar072/Welcome-Login-Signup-Page-Flutter') {
    return '3.19.6';
  }
  if (repo == 'mhmzdev/the-holy-quran-app') {
    return '3.38.8';
  }
  if (fvmVersion.isNotEmpty) {
    return fvmVersion;
  }
  return 'stable';
}

String _flutterExecutable(String version) {
  final home = Platform.environment['HOME'];
  if (home == null || home.isEmpty) {
    throw StateError('HOME is required to locate the installed FVM SDKs.');
  }
  final executable = version == 'stable'
      ? '$home/fvm/versions/stable/bin/flutter'
      : '$home/fvm/versions/$version/bin/flutter';
  if (!File(executable).existsSync()) {
    throw StateError('Flutter $version is not installed at $executable');
  }
  return executable;
}

void _integrate(CorpusApp app, CorpusOptions options) {
  final pubspec = File('${app.root}/pubspec.yaml');
  final relativeSdkPath = _relativePath(app.root, options.sdkPath);
  _addOrUpdatePathDependency(pubspec, relativeSdkPath);
  _modernizeGitUrls(pubspec);
  _applyKnownPackageCompatibilityFixes(app, pubspec);

  final target = File('${app.root}/$_validationTarget');
  target.writeAsStringSync(
    _validationSource(app.repo, app.mainAcceptsArguments),
  );

  _raiseAndroidMinimums(app);
  _raiseIosMinimums(app);
}

String _validationSource(String repo, bool mainAcceptsArguments) {
  final applicationCall = mainAcceptsArguments
      ? 'application.main(<String>[]);'
      : 'application.main();';
  return '''// Generated by ansight_flutter/tool/flutter_corpus.dart.
// This entry point preserves the upstream app and initializes the real native
// Ansight plugin before invoking its original main function.
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/widgets.dart';

import 'main.dart' as application;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Ansight.instance.initializeAndActivate();
  await AnsightFlutterInstrumentation.instance.install();
  await Ansight.instance.event(
    'Flutter corpus integration started',
    details: '$repo',
  );
  $applicationCall
}
''';
}

void _addOrUpdatePathDependency(File pubspec, String sdkPath) {
  var source = pubspec.readAsStringSync();
  final existing = RegExp(
    r'^([ \t]+)ansight_flutter:\s*\n'
    r'\1[ \t]+path:\s*[^\n]+\n?',
    multiLine: true,
  );
  if (existing.hasMatch(source)) {
    source = source.replaceFirst(existing, '');
  }

  final dependencies = RegExp(r'^dependencies:\s*$', multiLine: true);
  final match = dependencies.firstMatch(source);
  if (match == null) {
    throw StateError('No dependencies section in ${pubspec.path}');
  }
  final afterHeader = source.substring(match.end);
  final firstChild = RegExp(r'^\n([ \t]+)\S').firstMatch(afterHeader);
  final indent = firstChild?.group(1) ?? '  ';
  final dependency = '$indent$_packageName:\n$indent  path: $sdkPath\n';
  source = source.replaceRange(
    match.end,
    match.end,
    '\n${dependency.trimRight()}',
  );
  pubspec.writeAsStringSync(source);
}

void _setSdkConstraint(File pubspec, String constraint) {
  var source = pubspec.readAsStringSync();
  final environment = RegExp(r'^environment:\s*$', multiLine: true);
  final environmentMatch = environment.firstMatch(source);
  if (environmentMatch == null) {
    final dependencies =
        RegExp(r'^dependencies:\s*$', multiLine: true).firstMatch(source);
    if (dependencies == null) {
      throw StateError('No dependencies section in ${pubspec.path}');
    }
    source = source.replaceRange(
      dependencies.start,
      dependencies.start,
      "environment:\n  sdk: '$constraint'\n\n",
    );
  } else {
    final tail = source.substring(environmentMatch.end);
    final sdk = RegExp(
      r'''^([ \t]+)sdk:\s*['"]?[^'"\n]+['"]?\s*$''',
      multiLine: true,
    ).firstMatch(tail);
    if (sdk == null) {
      source = source.replaceRange(
        environmentMatch.end,
        environmentMatch.end,
        "\n  sdk: '$constraint'",
      );
    } else {
      source = source.replaceRange(
        environmentMatch.end + sdk.start,
        environmentMatch.end + sdk.end,
        "${sdk.group(1)}sdk: '$constraint'",
      );
    }
  }
  pubspec.writeAsStringSync(source);
}

void _modernizeGitUrls(File pubspec) {
  final source = pubspec.readAsStringSync();
  pubspec.writeAsStringSync(
    source.replaceAll('git://github.com/', 'https://github.com/'),
  );
}

void _applyKnownPackageCompatibilityFixes(CorpusApp app, File pubspec) {
  var source = pubspec.readAsStringSync();
  if (app.repo == 'gskinnerTeam/flutter-folio') {
    // 0.1.5 avoids the dart:ffi/dart:ui Size collision on Dart 2.17 while
    // retaining win32 <5 compatibility with this app's path_provider.
    source = source.replaceFirst(
      RegExp(r'^  bitsdojo_window:\s*[^\n]+$', multiLine: true),
      '  bitsdojo_window: 0.1.5',
    );
  }
  if (app.repo == 'yubo725/flutter-osc') {
    source = source
        .replaceFirst(
          RegExp(r'^  barcode_scan(?:2)?:\s*[^\n]+$', multiLine: true),
          '  barcode_scan2: 4.2.4',
        )
        .replaceFirst(
          RegExp(r'^  shared_preferences:\s*[^\n]+$', multiLine: true),
          '  shared_preferences: 2.0.6',
        )
        .replaceFirst(
          RegExp(r'^  image_picker:\s*[^\n]+$', multiLine: true),
          '  image_picker: 0.8.5+3',
        )
        .replaceFirst(
          RegExp(r'^  http:\s*[^\n]+$', multiLine: true),
          '  http: 0.13.5',
        );
  }
  if (app.repo == 'X-Wei/flutter_catalog') {
    source = source.replaceFirst(
      RegExp(r'^  hive(?:_ce)?_generator:\s*[^\n]+$', multiLine: true),
      '  hive_ce_generator: ^1.4.0',
    );
  }
  if (app.repo == 'designDo/flutter-checkio') {
    source = source.replaceAll(
      RegExp(r'^  platform:\s*[^\n]+$\n?', multiLine: true),
      '',
    );
    source = source
        .replaceFirst(
          RegExp(r'^  bloc:\s*[^\n]+$', multiLine: true),
          '  bloc: 8.1.0',
        )
        .replaceFirst(
          RegExp(r'^  flutter_bloc:\s*[^\n]+$', multiLine: true),
          '  flutter_bloc: 8.1.1\n  platform: 3.1.0',
        )
        .replaceFirst(
          RegExp(r'^  modal_bottom_sheet:\s*[^\n]+$', multiLine: true),
          '  modal_bottom_sheet: 2.1.2',
        );
  }
  if (app.repo == 'LonelyCpp/flutter_weather') {
    source = source
        .replaceFirst(
          RegExp(r'^  charts_flutter:\s*[^\n]+$', multiLine: true),
          '  charts_flutter: ^0.12.0',
        )
        .replaceFirst(
          RegExp(r'^  http:\s*[^\n]+$', multiLine: true),
          '  http: ^0.13.3\n  platform: 3.1.0',
        );
  }
  if (app.repo == 'darkmoonight/Rain') {
    // 0.9.1 declares androidx.glance with the open-ended `1.+` selector,
    // which now resolves to a release requiring unreleased Android API/AGP
    // levels. 0.9.3 pins Glance to 1.1.1.
    source = source.replaceFirst(
      RegExp(r'^  home_widget:\s*[^\n]+$', multiLine: true),
      '  home_widget: 0.9.3',
    );
  }
  if (app.repo == 'aaronoe/FlutterCinematic') {
    source = source
        .replaceFirst(
          RegExp(r'^  scoped_model:\s*[^\n]+$', multiLine: true),
          '  scoped_model: ^2.0.0',
        )
        .replaceFirst(
          RegExp(r'^  shared_preferences:\s*[^\n]+$', multiLine: true),
          '  shared_preferences: 2.0.6',
        )
        .replaceFirst(
          RegExp(r'^  url_launcher:\s*[^\n]+$', multiLine: true),
          '  url_launcher: 6.1.5',
        )
        .replaceFirst(
          RegExp(r'^  rxdart:\s*[^\n]+$', multiLine: true),
          '  rxdart: 0.27.7',
        )
        .replaceFirst(
          RegExp(r'^  intl:\s*[^\n]+$', multiLine: true),
          '  intl: 0.17.0',
        );
  }
  if (app.repo == 'KarimElghamry/chillify') {
    source = source
        .replaceFirst(
          RegExp(r'^  provider:\s*[^\n]+$', multiLine: true),
          '  provider: 4.3.2+2',
        )
        .replaceFirst(
          RegExp(r'^  permission_handler:\s*[^\n]+$', multiLine: true),
          '  permission_handler: ^9.2.0',
        )
        .replaceFirst(
          RegExp(r'^  shared_preferences:\s*[^\n]+$', multiLine: true),
          '  shared_preferences: 2.0.6',
        );
  }
  if (app.repo == 'CoderMikeHe/flutter_wechat') {
    source = source.replaceAll(
      RegExp(r'^  decimal:\s*[^\n]+$\n?', multiLine: true),
      '',
    );
    source = source
        .replaceFirst(
          RegExp(r'^  shared_preferences:\s*[^\n]+$', multiLine: true),
          '  shared_preferences: 2.0.6',
        )
        .replaceFirst(
          RegExp(r'^  flustars:\s*[^\n]+$', multiLine: true),
          '  flustars: 0.3.3\n  decimal: 1.5.0',
        )
        .replaceFirst(
          RegExp(r'^  flutter_svg:\s*[^\n]+$', multiLine: true),
          '  flutter_svg: 0.22.0',
        )
        .replaceFirst(
          RegExp(r'^  flutter_slidable:\s*[^\n]+$', multiLine: true),
          '  flutter_slidable: 0.6.0',
        )
        .replaceFirst(
          RegExp(r'^  provider:\s*[^\n]+$', multiLine: true),
          '  provider: 4.3.2+2',
        );
  }
  if (app.repo == 'redsolver/noteless') {
    source = source.replaceFirst(
      RegExp(r'^  receive_sharing_intent:\s*[^\n]+$', multiLine: true),
      '  receive_sharing_intent: 1.4.5',
    );
  }
  if (app.repo == 'mhmzdev/the-holy-quran-app') {
    source = source
        .replaceFirst(
          RegExp(r'^  freezed_annotation:\s*[^\n]+$', multiLine: true),
          '  freezed_annotation: 2.4.4',
        )
        .replaceFirst(
          RegExp(r'^  build_runner:\s*[^\n]+$', multiLine: true),
          '  build_runner: 2.4.13',
        )
        .replaceFirst(
          RegExp(r'^  freezed:\s*[^\n]+$', multiLine: true),
          '  freezed: 2.5.7',
        )
        .replaceFirst(
          RegExp(r'^  json_serializable:\s*[^\n]+$', multiLine: true),
          '  json_serializable: 6.9.0',
        );
  }
  if (app.repo == 'guozhigq/flutter_v2ex') {
    source = source.replaceFirst(
      RegExp(
        r'^\s{4}appscheme:\s*\n'
        r'\s{8}git:\s*\n'
        r'\s{12}url:\s*[^\n]+\n'
        r'\s{12}ref:\s*[^\n]+\n',
        multiLine: true,
      ),
      '    app_links: 7.0.0\n',
    );
    if (!RegExp(
      r'^\s+sqflite_android:',
      multiLine: true,
    ).hasMatch(source)) {
      source = source.replaceFirst(
        '    app_links: 7.0.0',
        '    app_links: 7.0.0\n'
            '    sqflite_android: 2.4.1',
      );
    }
    source = source.replaceFirst(
      RegExp(r'^\s{4}extended_text_field:\s*[^\n]+$', multiLine: true),
      '    extended_text_field: 17.0.0',
    );
  }
  pubspec.writeAsStringSync(source);
  if (app.repo == 'aaronoe/FlutterCinematic') {
    _setSdkConstraint(pubspec, '>=2.7.0 <3.0.0');
    _repairFlutterCinematic(app);
  }
  if (app.repo == 'SAGARSURI/MyMovies') {
    _setSdkConstraint(pubspec, '>=2.7.0 <3.0.0');
  }
  if (app.repo == 'mhmzdev/the-holy-quran-app') {
    _setSdkConstraint(pubspec, '>=3.8.1 <4.0.0');
  }
  if (app.repo == 'KarimElghamry/chillify') {
    _repairChillify(app);
  }
  if (app.repo == 'yubo725/flutter-osc') {
    _repairFlutterOsc(app);
  }
  if (app.repo == 'CoderMikeHe/flutter_wechat') {
    _repairFlutterWechat(app);
  }
  if (app.repo == 'marchellodev/sharik') {
    _repairSharikLocalization(app);
  }
  if (app.repo == 'guozhigq/flutter_v2ex') {
    _repairV2exAppLinks(app);
  }
  if (app.repo == 'asjqkkkk/flutter-todos') {
    final picker = File('${app.root}/lib/widgets/custom_time_picker.dart');
    var pickerSource = picker.readAsStringSync();
    pickerSource =
        pickerSource.replaceAll('OutlineButton(', 'FlatButton(').replaceFirst(
              RegExp(
                r'\s*borderSide:\s*BorderSide\('
                r'color: Theme\.of\(context\)\.primaryColor\),',
              ),
              '',
            );
    picker.writeAsStringSync(pickerSource);
  }
  if (app.repo == 'bizz84/movie_app_state_management_flutter') {
    File('${app.root}/../../packages/core/.env').writeAsStringSync(
      'TMDB_KEY=ansight-validation-placeholder\n',
    );
  }
  if (app.repo == 'LonelyCpp/flutter_weather') {
    final apiKeys = File('${app.root}/lib/src/api/api_keys.dart')
      ..parent.createSync(recursive: true);
    apiKeys.writeAsStringSync('''
// Validation placeholder. The corpus build does not issue network requests.
class ApiKey {
  static const String OPEN_WEATHER_MAP = 'ansight-validation-placeholder';
}
''');
  }
}

void _repairFlutterOsc(CorpusApp app) {
  final main = File('${app.root}/lib/main.dart');
  var source = main.readAsStringSync();
  source = source.replaceAllMapped(
    RegExp(r'title:\s*getTabTitle\((\d)\)'),
    (match) => 'label: appBarTitles[${match.group(1)}]',
  );
  main.writeAsStringSync(source);

  final discovery = File('${app.root}/lib/pages/DiscoveryPage.dart');
  var discoverySource = discovery.readAsStringSync();
  discoverySource = discoverySource
      .replaceAll(
        "import 'package:barcode_scan/barcode_scan.dart';",
        "import 'package:barcode_scan2/barcode_scan2.dart';",
      )
      .replaceFirst(
        'String barcode = await BarcodeScanner.scan();',
        'String barcode = (await BarcodeScanner.scan()).rawContent;',
      );
  discovery.writeAsStringSync(discoverySource);

  final publishTweet = File('${app.root}/lib/pages/PublishTweetPage.dart');
  var publishTweetSource = publishTweet.readAsStringSync();
  publishTweetSource = publishTweetSource.replaceFirst(
    'ImagePicker.pickImage(source: source)',
    'ImagePicker().pickImage(source: source).then((image) => File(image.path))',
  );
  publishTweet.writeAsStringSync(publishTweetSource);

  final netUtils = File('${app.root}/lib/util/NetUtils.dart');
  var netUtilsSource = netUtils.readAsStringSync();
  netUtilsSource = netUtilsSource
      .replaceAll('http.get(url,', 'http.get(Uri.parse(url),')
      .replaceAll('http.post(url,', 'http.post(Uri.parse(url),');
  netUtils.writeAsStringSync(netUtilsSource);
}

void _repairChillify(CorpusApp app) {
  final permissions = File(
    '${app.root}/lib/src/blocs/permissions.dart',
  );
  var permissionSource = permissions.readAsStringSync();
  permissionSource = permissionSource.replaceFirst(
    RegExp(
      r'Map<PermissionGroup, PermissionStatus> _permission =\s*'
      r'await PermissionHandler\(\)\.requestPermissions\(\s*'
      r'\[\s*PermissionGroup\.storage,\s*\],\s*\);\s*'
      r'final PermissionStatus _state = _permission\.values\.toList\(\)\[0\];',
    ),
    'final PermissionStatus _state = await Permission.storage.request();',
  );
  permissions.writeAsStringSync(permissionSource);

  final searchScreen = File(
    '${app.root}/lib/src/ui/search/search_screen.dart',
  );
  var searchScreenSource = searchScreen.readAsStringSync();
  searchScreenSource = searchScreenSource.replaceAll(
    'resizeToAvoidBottomPadding:',
    'resizeToAvoidBottomInset:',
  );
  searchScreen.writeAsStringSync(searchScreenSource);

  final root = File('${app.root}/lib/src/root.dart');
  var rootSource = root.readAsStringSync();
  rootSource = rootSource.replaceFirst(
    '      builder: (BuildContext context) {',
    '      create: (BuildContext context) {',
  );
  root.writeAsStringSync(rootSource);
}

void _repairFlutterWechat(CorpusApp app) {
  final applet = File('${app.root}/lib/widgets/mainframe/applet.dart');
  applet.writeAsStringSync(
    applet.readAsStringSync().replaceFirst("import 'dart:wasm';\n\n", ''),
  );

  for (final relativePath in <String>[
    'lib/main.dart',
    'lib/routers/routers.dart',
  ]) {
    final file = File('${app.root}/$relativePath');
    file.writeAsStringSync(
      file.readAsStringSync().replaceFirst(
            "import 'package:flutter/material.dart';",
            "import 'package:flutter/material.dart' hide Router;",
          ),
    );
  }

  final main = File('${app.root}/lib/main.dart');
  var mainSource = main.readAsStringSync();
  mainSource = mainSource.replaceFirst(
    RegExp(
      r'final _RestartWidgetState state =\s*'
      r'context\.ancestorStateOfType\('
      r'const TypeMatcher<_RestartWidgetState>\(\)\);',
    ),
    'final _RestartWidgetState state =\n'
    '        context.findAncestorStateOfType<_RestartWidgetState>();',
  );
  main.writeAsStringSync(mainSource);

  final home = File('${app.root}/lib/views/home/home_page.dart');
  var homeSource = home.readAsStringSync();
  homeSource = homeSource.replaceFirst(
    RegExp(
      r'title:\s*Text\(\s*item\.title,\s*'
      r'textScaleFactor:\s*1\.0,\s*'
      r'//[^\n]*\n\s*'
      r'style:\s*TextStyle\(\s*fontSize:\s*10\.0,\s*\),\s*\),',
    ),
    'label: item.title,',
  );
  home.writeAsStringSync(homeSource);

  for (final entity in Directory('${app.root}/lib')
      .listSync(recursive: true, followLinks: false)) {
    if (entity is! File || !entity.path.endsWith('.dart')) {
      continue;
    }
    var source = entity.readAsStringSync();
    source = source
        .replaceAll('overflow: Overflow.visible,', 'clipBehavior: Clip.none,')
        .replaceAll(
          'Theme.of(context, shadowThemeOnly: true)',
          'Theme.of(context)',
        )
        .replaceAll(
          'Scaffold.of(context, nullOk: true)',
          'Scaffold.maybeOf(context)',
        );
    entity.writeAsStringSync(source);
  }
}

void _repairFlutterCinematic(CorpusApp app) {
  final constants = File('${app.root}/lib/util/constants.dart');
  var constantsSource = constants.readAsStringSync();
  constantsSource = constantsSource.replaceFirst(
    'const String API_KEY = <your-api-key>;',
    "const String API_KEY = 'ansight-validation-placeholder';",
  );
  constants.writeAsStringSync(constantsSource);

  for (final entity in Directory('${app.root}/lib')
      .listSync(recursive: true, followLinks: false)) {
    if (entity is! File || !entity.path.endsWith('.dart')) {
      continue;
    }
    var dartSource = entity.readAsStringSync();
    dartSource = dartSource
        .replaceAllMapped(
          RegExp(r"title:\s*Text\('([^']+)'\)"),
          (match) => "label: '${match.group(1)}'",
        )
        .replaceAll('.textTheme.subhead', '.textTheme.titleMedium')
        .replaceAll('.textTheme.body1', '.textTheme.bodyMedium')
        .replaceAll('.primaryTextTheme.title', '.primaryTextTheme.titleLarge')
        .replaceAll('Observable.fromFuture(', 'Stream.fromFuture(');
    entity.writeAsStringSync(dartSource);
  }
}

void _repairV2exAppLinks(CorpusApp app) {
  final appScheme = File('${app.root}/lib/utils/app_scheme.dart');
  appScheme.writeAsStringSync('''
import 'package:app_links/app_links.dart';
import 'package:get/get.dart';
import 'package:flutter_v2ex/utils/logger.dart';

class VvexScheme {
  static final AppLinks appLinks = AppLinks();

  static Future<void> init() async {
    final Uri? initialLink = await appLinks.getInitialLink();
    if (initialLink != null) {
      logDebug('Initial app link: \${initialLink.host}');
    }

    appLinks.uriLinkStream.listen((Uri event) {
      logDebug('App link host: \${event.host}');
      logDebug('App link path: \${event.path}');
      logDebug('App link query: \${event.queryParameters}');
      if (event.path.isNotEmpty) {
        Get.toNamed(event.path, arguments: null);
      }
    });
  }
}
''');

  final interceptor = File('${app.root}/lib/http/interceptor.dart');
  var interceptorSource = interceptor.readAsStringSync();
  if (!interceptorSource.contains('DioExceptionType.transformTimeout:')) {
    interceptorSource = interceptorSource.replaceFirst(
      '      case DioExceptionType.receiveTimeout:',
      '      case DioExceptionType.transformTimeout:\n'
          '        return "响应转换超时，请稍后重试！";\n'
          '      case DioExceptionType.receiveTimeout:',
    );
  }
  interceptor.writeAsStringSync(interceptorSource);
}

void _repairSharikLocalization(CorpusApp app) {
  final generatedLanguages = File('${app.root}/lib/gen/languages.dart');
  generatedLanguages.writeAsStringSync(
    '''// Repaired by ansight_flutter/tool/flutter_corpus.dart.
import 'package:flutter/material.dart';
import 'package:flutter_gen/gen_l10n/app_localizations_en.dart';

import '../logic/language.dart';

List<Language> get languageListGen => <Language>[
      Language(
        name: 'english',
        nameLocal: 'English',
        locale: const Locale('en'),
        localizations: AppLocalizationsEn(),
      ),
    ];
''',
  );

  final picker = File('${app.root}/lib/screens/languages.dart');
  var source = picker.readAsStringSync();
  source = source.replaceAll(
    r'${_language.localizations.s_flag}',
    r'${_language.locale.languageCode}',
  );
  picker.writeAsStringSync(source);
}

void _raiseAndroidMinimums(CorpusApp app) {
  final android = Directory('${app.root}/android');
  if (!android.existsSync()) {
    return;
  }
  _applyKnownAndroidCompatibilityFixes(app, android);

  final rootGroovy = File('${android.path}/build.gradle');
  final rootKotlin = File('${android.path}/build.gradle.kts');
  final settingsGroovy = File('${android.path}/settings.gradle');
  final settingsKotlin = File('${android.path}/settings.gradle.kts');
  for (final file in <File>[
    rootGroovy,
    rootKotlin,
    settingsGroovy,
    settingsKotlin,
  ]) {
    if (file.existsSync()) {
      _raiseKotlinVersion(
        file,
        app.usesLegacyAndroidToolchain
            ? _legacyKotlinVersion
            : _modernKotlinVersion,
      );
    }
  }
  if (app.usesLegacyAndroidToolchain) {
    _raiseLegacyAndroidGradlePlugin(rootGroovy);
    _raiseLegacyAndroidGradlePlugin(rootKotlin);
    _raiseLegacyGradleWrapper(
      File('${android.path}/gradle/wrapper/gradle-wrapper.properties'),
    );
    _modernizeLegacyGradleProperties(
      File('${android.path}/gradle.properties'),
    );
  }

  final appGroovy = File('${android.path}/app/build.gradle');
  final appKotlin = File('${android.path}/app/build.gradle.kts');
  if (appGroovy.existsSync()) {
    _raiseMinSdk(appGroovy, kotlinDsl: false);
    if (app.usesLegacyAndroidToolchain) {
      _raiseCompileSdk(appGroovy, kotlinDsl: false);
    }
    if (!app.usesLegacyAndroidToolchain) {
      _modernizeKotlinCompilerOptions(appGroovy);
    }
  } else if (appKotlin.existsSync()) {
    _raiseMinSdk(appKotlin, kotlinDsl: true);
    if (app.usesLegacyAndroidToolchain) {
      _raiseCompileSdk(appKotlin, kotlinDsl: true);
    }
    if (!app.usesLegacyAndroidToolchain) {
      _modernizeKotlinCompilerOptions(appKotlin);
    }
  }

  final manifest = File('${android.path}/app/src/main/AndroidManifest.xml');
  if (manifest.existsSync()) {
    var source = manifest.readAsStringSync();
    if (!source.contains('android.permission.INTERNET')) {
      final manifestTag = RegExp(r'<manifest\b[^>]*>');
      final match = manifestTag.firstMatch(source);
      if (match != null) {
        source = source.replaceRange(
          match.end,
          match.end,
          '\n    <uses-permission '
          'android:name="android.permission.INTERNET" />',
        );
        manifest.writeAsStringSync(source);
      }
    }
    _ensureLauncherActivityExported(manifest);
    _migrateAndroidV1Embedding(android, manifest);
  }
}

void _modernizeLegacyGradleProperties(File file) {
  if (!file.existsSync()) {
    return;
  }
  final lines = file
      .readAsLinesSync()
      .where((line) => !line.startsWith('android.enableAapt2='))
      .map(
    (line) {
      if (line.startsWith('android.useAndroidX=')) {
        return 'android.useAndroidX=true';
      }
      if (line.startsWith('android.enableJetifier=')) {
        return 'android.enableJetifier=true';
      }
      if (line.startsWith('org.gradle.jvmargs=')) {
        return 'org.gradle.jvmargs=-Xmx4G '
            '-XX:MaxMetaspaceSize=1G '
            '-XX:+HeapDumpOnOutOfMemoryError';
      }
      return line;
    },
  ).toList();
  if (!lines.any((line) => line.startsWith('org.gradle.jvmargs='))) {
    lines.add(
      'org.gradle.jvmargs=-Xmx4G '
      '-XX:MaxMetaspaceSize=1G '
      '-XX:+HeapDumpOnOutOfMemoryError',
    );
  }
  if (!lines.any((line) => line.startsWith('android.useAndroidX='))) {
    lines.add('android.useAndroidX=true');
  }
  if (!lines.any((line) => line.startsWith('android.enableJetifier='))) {
    lines.add('android.enableJetifier=true');
  }
  file.writeAsStringSync('${lines.join('\n')}\n');
}

void _ensureLauncherActivityExported(File manifest) {
  var source = manifest.readAsStringSync();
  source = source.replaceAllMapped(
    RegExp(
      r'<activity\b([^>]*)>'
      r'([\s\S]*?<intent-filter>[\s\S]*?'
      r'android\.intent\.action\.MAIN[\s\S]*?</activity>)',
    ),
    (match) {
      final attributes = match.group(1)!;
      if (attributes.contains('android:exported=')) {
        return match.group(0)!;
      }
      return '<activity$attributes\n'
          '            android:exported="true">'
          '${match.group(2)}';
    },
  );
  manifest.writeAsStringSync(source);
}

void _migrateAndroidV1Embedding(
  Directory android,
  File manifest,
) {
  var manifestSource = manifest.readAsStringSync();
  final usesV1Application =
      manifestSource.contains('io.flutter.app.FlutterApplication');
  final sourceFiles = <File>[];
  final sourceRoot = Directory('${android.path}/app/src/main');
  if (sourceRoot.existsSync()) {
    for (final entity
        in sourceRoot.listSync(recursive: true, followLinks: false)) {
      if (entity is File &&
          (entity.path.endsWith('.java') || entity.path.endsWith('.kt'))) {
        sourceFiles.add(entity);
      }
    }
  }
  final usesV1Activity = sourceFiles.any(
    (file) =>
        file.readAsStringSync().contains('io.flutter.app.FlutterActivity') ||
        file
            .readAsStringSync()
            .contains('GeneratedPluginRegistrant.registerWith'),
  );
  if (!usesV1Application && !usesV1Activity) {
    return;
  }

  for (final file in sourceFiles) {
    var source = file.readAsStringSync();
    source = source
        .replaceAll(
          RegExp(
            r'\s*override\s+fun\s+configureFlutterEngine'
            r'\s*\([^)]*\)\s*\{\s*'
            r'GeneratedPluginRegistrant\.registerWith\([^)]*\);?\s*\}',
          ),
          '\n',
        )
        .replaceAll(
          'io.flutter.app.FlutterActivity',
          'io.flutter.embedding.android.FlutterActivity',
        )
        .replaceAll(
          RegExp(
            r'^import io\.flutter\.plugins\.GeneratedPluginRegistrant;?\s*$\n?',
            multiLine: true,
          ),
          '',
        )
        .replaceAll(
          RegExp(
            r'^\s*GeneratedPluginRegistrant\.registerWith\([^)]*\);?\s*$\n?',
            multiLine: true,
          ),
          '',
        );
    file.writeAsStringSync(source);
  }

  manifestSource = manifestSource.replaceAll(
    'android:name="io.flutter.app.FlutterApplication"',
    r'android:name="${applicationName}"',
  );
  manifestSource = manifestSource.replaceAllMapped(
    RegExp(
      r'<activity\b(?=[^>]*android:name="\.MainActivity")([^>]*)>',
      multiLine: true,
    ),
    (match) {
      final attributes = match.group(1)!;
      if (attributes.contains('android:exported=')) {
        return match.group(0)!;
      }
      return '<activity$attributes\n'
          '            android:exported="true">';
    },
  );
  manifestSource = manifestSource.replaceAllMapped(
    RegExp(
      r'<activity\b([^>]*)>'
      r'([\s\S]*?<intent-filter>[\s\S]*?'
      r'android\.intent\.action\.MAIN[\s\S]*?</activity>)',
    ),
    (match) {
      final attributes = match.group(1)!;
      if (attributes.contains('android:exported=')) {
        return match.group(0)!;
      }
      return '<activity$attributes\n'
          '            android:exported="true">'
          '${match.group(2)}';
    },
  );
  if (!manifestSource.contains('android:name="flutterEmbedding"')) {
    final applicationEnd = manifestSource.lastIndexOf('</application>');
    if (applicationEnd >= 0) {
      manifestSource = manifestSource.replaceRange(
        applicationEnd,
        applicationEnd,
        '        <meta-data\n'
        '            android:name="flutterEmbedding"\n'
        '            android:value="2" />\n',
      );
    }
  }
  manifest.writeAsStringSync(manifestSource);
}

void _applyKnownAndroidCompatibilityFixes(
  CorpusApp app,
  Directory android,
) {
  if (app.repo == 'mkobuolys/flutter-design-patterns') {
    _repairDesignPatternsAndroid(android);
  }
  if (app.repo == 'guozhigq/flutter_v2ex') {
    _repairV2exAndroid(android);
  }
  if (app.repo == 'designDo/flutter-checkio') {
    final buildFile = File('${android.path}/build.gradle');
    var source = buildFile.readAsStringSync();
    source = source
        .replaceAll(
          RegExp(r'//\s*google\(\)'),
          'google()',
        )
        .replaceAll(
          RegExp(r'//\s*jcenter\(\)'),
          'mavenCentral()',
        )
        .replaceAll(
          'repositories {',
          'repositories {\n        google()\n        mavenCentral()',
        );
    buildFile.writeAsStringSync(source);
  }
  if (app.repo == 'redsolver/noteless') {
    final buildFile = File('${android.path}/app/build.gradle');
    var source = buildFile.readAsStringSync();
    source = source.replaceFirst(
      "            storeFile file(keystoreProperties['storeFile'])",
      "            if (keystoreProperties['storeFile'] != null) {\n"
          "                storeFile file(keystoreProperties['storeFile'])\n"
          '            }',
    );
    buildFile.writeAsStringSync(source);
  }
  if (app.repo == 'darkmoonight/Zest' || app.repo == 'darkmoonight/Rain') {
    final buildFile = File('${android.path}/app/build.gradle');
    var source = buildFile.readAsStringSync();
    source = source
        .replaceFirst(
          RegExp(r'\s*\$1false\s*shrinkResources\s*=\s*false\s*\}'),
          '\n        debug {\n'
          '            minifyEnabled = false\n'
          '            shrinkResources = false\n'
          '        }',
        )
        .replaceFirstMapped(
          RegExp(r'(debug\s*\{[\s\S]*?minifyEnabled\s*=\s*)true'),
          (match) => '${match.group(1)}false',
        );
    buildFile.writeAsStringSync(source);
  }
  if (app.repo == 'X-Wei/flutter_catalog') {
    _mergeMlKitInstallTimeDependencies(android);
  }
  if (app.repo != 'gskinnerTeam/flutter-folio') {
    return;
  }
  final buildFile = File('${android.path}/build.gradle');
  if (!buildFile.existsSync()) {
    return;
  }
  var source = buildFile.readAsStringSync();
  const mirror = 'https://maven.aliyun.com/repository/jcenter';
  if (!source.contains(mirror)) {
    source = '${source.trimRight()}\n\n'
        '// FishBun 0.11.2 was published only to the retired JCenter service.\n'
        'allprojects {\n'
        '    repositories {\n'
        '        maven { url "$mirror" }\n'
        '    }\n'
        '}\n';
    buildFile.writeAsStringSync(source);
  }
}

void _mergeMlKitInstallTimeDependencies(Directory android) {
  final manifest = File('${android.path}/app/src/main/AndroidManifest.xml');
  var source = manifest.readAsStringSync();
  source = source.replaceAll(
    '"" tools:replace="android:value"',
    '" tools:replace="android:value"',
  );
  if (!source.contains('xmlns:tools=')) {
    source = source.replaceFirst(
      '<manifest ',
      '<manifest xmlns:tools="http://schemas.android.com/tools" ',
    );
  }
  source = source.replaceFirstMapped(
    RegExp(
      r'(<meta-data\s+'
      r'android:name="com\.google\.mlkit\.vision\.DEPENDENCIES"\s+'
      r'android:value=")([^"]+)("\s*)(/>)',
    ),
    (match) {
      final modules = match
          .group(2)!
          .split(',')
          .map((module) => module.trim())
          .where((module) => module.isNotEmpty)
          .toSet()
        ..add('barcode_ui');
      final suffix = match.group(3)!.contains('tools:replace=')
          ? match.group(3)!
          : '${match.group(3)}tools:replace="android:value" ';
      return '${match.group(1)}${modules.join(',')}$suffix${match.group(4)}';
    },
  );
  manifest.writeAsStringSync(source);
}

void _repairDesignPatternsAndroid(Directory android) {
  File('${android.path}/settings.gradle').writeAsStringSync('''
pluginManagement {
    def flutterSdkPath = {
        def properties = new Properties()
        file("local.properties").withInputStream { properties.load(it) }
        def flutterSdkPath = properties.getProperty("flutter.sdk")
        assert flutterSdkPath != null, "flutter.sdk not set in local.properties"
        return flutterSdkPath
    }()

    includeBuild("\$flutterSdkPath/packages/flutter_tools/gradle")

    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

plugins {
    id "dev.flutter.flutter-plugin-loader" version "1.0.0"
    id "com.android.application" version "9.0.1" apply false
    id "org.jetbrains.kotlin.android" version "$_modernKotlinVersion" apply false
}

include ":app"
''');
  File('${android.path}/build.gradle').writeAsStringSync('''
allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.buildDir = "../build"
subprojects {
    project.buildDir = "\${rootProject.buildDir}/\${project.name}"
}
subprojects {
    project.evaluationDependsOn(":app")
}

tasks.register("clean", Delete) {
    delete rootProject.layout.buildDirectory
}
''');
  File('${android.path}/app/build.gradle').writeAsStringSync('''
plugins {
    id "com.android.application"
    id "dev.flutter.flutter-gradle-plugin"
}

android {
    namespace "com.mangirdaskazlauskas.flutter_design_patterns"
    compileSdk flutter.compileSdkVersion
    ndkVersion flutter.ndkVersion

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_17
        targetCompatibility JavaVersion.VERSION_17
    }

    defaultConfig {
        applicationId "com.mangirdaskazlauskas.flutter_design_patterns"
        minSdkVersion $_minimumAndroidSdk
        targetSdkVersion flutter.targetSdkVersion
        versionCode flutter.versionCode
        versionName flutter.versionName
    }

    buildTypes {
        release {
            signingConfig signingConfigs.debug
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source "../.."
}
''');
  final wrapper =
      File('${android.path}/gradle/wrapper/gradle-wrapper.properties');
  if (wrapper.existsSync()) {
    var source = wrapper.readAsStringSync();
    source = source.replaceAll(
      RegExp(r'gradle-[0-9.]+-(?:all|bin)\.zip'),
      'gradle-9.1.0-all.zip',
    );
    wrapper.writeAsStringSync(source);
  }
  final properties = File('${android.path}/gradle.properties');
  if (properties.existsSync()) {
    var source = properties.readAsStringSync();
    source = source
        .replaceFirst(
          RegExp(r'^org\.gradle\.jvmargs=.*$', multiLine: true),
          'org.gradle.jvmargs=-Xmx4G -XX:MaxMetaspaceSize=2G',
        )
        .replaceFirst(
          RegExp(r'^android\.enableJetifier=.*$', multiLine: true),
          'android.enableJetifier=false',
        )
        .replaceFirst(
          RegExp(r'^android\.builtInKotlin=.*$', multiLine: true),
          'android.builtInKotlin=false',
        );
    properties.writeAsStringSync(source);
  }
}

void _repairV2exAndroid(Directory android) {
  final settings = File('${android.path}/settings.gradle');
  var settingsSource = settings.readAsStringSync();
  settingsSource = settingsSource.replaceFirstMapped(
    RegExp(
      r'(id "com\.android\.application" version ")[^"]+(" apply false)',
    ),
    (match) => '${match.group(1)}8.9.1${match.group(2)}',
  );
  settings.writeAsStringSync(settingsSource);

  final rootBuild = File('${android.path}/build.gradle');
  var rootSource = rootBuild.readAsStringSync();
  rootSource = rootSource
      .replaceAll(
        RegExp(r'compileSdkVersion\s+\d+'),
        'compileSdkVersion 36',
      )
      .replaceAll(
        RegExp(r'^\s*buildToolsVersion\s+"[^"]+"\s*$\n?', multiLine: true),
        '',
      );
  rootBuild.writeAsStringSync(rootSource);

  final appBuild = File('${android.path}/app/build.gradle');
  var appSource = appBuild.readAsStringSync();
  if (!appSource.contains('id "org.jetbrains.kotlin.android"')) {
    appSource = appSource.replaceFirst(
      '    id "com.android.application"',
      '    id "com.android.application"\n'
          '    id "org.jetbrains.kotlin.android"',
    );
  }
  appSource = appSource
      .replaceFirst(
        RegExp(r'^\s*compileSdk\s+[^\n]+$', multiLine: true),
        '    compileSdk 36',
      )
      .replaceFirst(
        RegExp(r'^\s*ndkVersion\s*=\s*"[^"]+"$', multiLine: true),
        '    ndkVersion = "28.2.13676358"',
      )
      .replaceAll(
        'JavaVersion.VERSION_1_8',
        'JavaVersion.VERSION_17',
      )
      .replaceFirst(
        RegExp(
          r'^\s*implementation '
          r'"org\.jetbrains\.kotlin:kotlin-stdlib-jdk7:[^"]+"\s*$\n?',
          multiLine: true,
        ),
        '',
      )
      .replaceAll('JvmTarget.JVM_1_8', 'JvmTarget.JVM_17');
  appBuild.writeAsStringSync(appSource);

  final wrapper =
      File('${android.path}/gradle/wrapper/gradle-wrapper.properties');
  var wrapperSource = wrapper.readAsStringSync();
  wrapperSource = wrapperSource.replaceAll(
    RegExp(r'gradle-[0-9.]+-(?:all|bin)\.zip'),
    'gradle-8.12-all.zip',
  );
  wrapper.writeAsStringSync(wrapperSource);

  final properties = File('${android.path}/gradle.properties');
  var propertiesSource = properties.readAsStringSync();
  propertiesSource = propertiesSource.replaceFirst(
    RegExp(r'^android\.builtInKotlin=.*$', multiLine: true),
    'android.builtInKotlin=false',
  );
  properties.writeAsStringSync(propertiesSource);
}

void _raiseLegacyAndroidGradlePlugin(File file) {
  if (!file.existsSync()) {
    return;
  }
  var source = file.readAsStringSync();
  final pattern = RegExp(
    r'(com\.android\.tools\.build:gradle:)([0-9.]+)',
  );
  source = source.replaceAllMapped(pattern, (match) {
    final current = match.group(2)!;
    if (_compareVersions(current, _legacyAndroidGradlePluginVersion) >= 0) {
      return match.group(0)!;
    }
    return '${match.group(1)}$_legacyAndroidGradlePluginVersion';
  });
  file.writeAsStringSync(source);
}

void _raiseLegacyGradleWrapper(File file) {
  if (!file.existsSync()) {
    return;
  }
  var source = file.readAsStringSync();
  final pattern = RegExp(r'gradle-([0-9.]+)-(?:all|bin)\.zip');
  source = source.replaceAllMapped(pattern, (match) {
    final current = match.group(1)!;
    if (_compareVersions(current, _legacyGradleVersion) >= 0) {
      return match.group(0)!;
    }
    return 'gradle-$_legacyGradleVersion-all.zip';
  });
  file.writeAsStringSync(source);
}

void _raiseCompileSdk(File file, {required bool kotlinDsl}) {
  var source = file.readAsStringSync();
  final pattern = kotlinDsl
      ? RegExp(r'^(\s*)compileSdk\s*=\s*([^\n]+)$', multiLine: true)
      : RegExp(
          r'^(\s*)compileSdkVersion(?:\s*=\s*|\s+)([^\n]+)$',
          multiLine: true,
        );
  var found = false;
  source = source.replaceAllMapped(pattern, (match) {
    found = true;
    final current = int.tryParse(
      RegExp(r'\d+').firstMatch(match.group(2)!)?.group(0) ?? '',
    );
    if (current != null && current >= _compileSdk) {
      return match.group(0)!;
    }
    return kotlinDsl
        ? '${match.group(1)}compileSdk = $_compileSdk'
        : '${match.group(1)}compileSdkVersion $_compileSdk';
  });
  if (!found) {
    source = '$source\n\nandroid {\n'
        '${kotlinDsl ? '    compileSdk = $_compileSdk' : '    compileSdkVersion $_compileSdk'}\n'
        '}\n';
  }
  file.writeAsStringSync(source);
}

void _raiseKotlinVersion(File file, String requiredVersion) {
  var source = file.readAsStringSync();
  final patterns = <RegExp>[
    RegExp(
      r'''(ext\.kotlin_version\s*=\s*['"])([^'"]+)(['"])''',
    ),
    RegExp(
      r'''(kotlin_version\s*=\s*['"])([^'"]+)(['"])''',
    ),
    RegExp(
      r'''(org\.jetbrains\.kotlin(?:\.android)?['"]?\)?\s+version\s+['"])([^'"]+)(['"])''',
    ),
  ];
  for (final pattern in patterns) {
    source = source.replaceAllMapped(pattern, (match) {
      final current = match.group(2)!;
      if (requiredVersion != _legacyKotlinVersion &&
          _compareVersions(current, requiredVersion) >= 0) {
        return match.group(0)!;
      }
      return '${match.group(1)}$requiredVersion${match.group(3)}';
    });
  }
  file.writeAsStringSync(source);
}

void _raiseMinSdk(File file, {required bool kotlinDsl}) {
  var source = file.readAsStringSync();
  final pattern = kotlinDsl
      ? RegExp(r'^(\s*)minSdk\s*=\s*([^\n]+)$', multiLine: true)
      : RegExp(
          r'^(\s*)minSdkVersion(?:\s*=\s*|\s+)([^\n]+)$',
          multiLine: true,
        );
  var found = false;
  source = source.replaceAllMapped(pattern, (match) {
    found = true;
    final current = int.tryParse(
      RegExp(r'\d+').firstMatch(match.group(2)!)?.group(0) ?? '',
    );
    if (current != null && current >= _minimumAndroidSdk) {
      return match.group(0)!;
    }
    return kotlinDsl
        ? '${match.group(1)}minSdk = $_minimumAndroidSdk'
        : '${match.group(1)}minSdkVersion $_minimumAndroidSdk';
  });

  if (!found) {
    source = '$source\n\nandroid {\n'
        '    defaultConfig {\n'
        '${kotlinDsl ? '        minSdk = $_minimumAndroidSdk' : '        minSdkVersion $_minimumAndroidSdk'}\n'
        '    }\n'
        '}\n';
  }
  file.writeAsStringSync(source);
}

void _modernizeKotlinCompilerOptions(File file) {
  var source = file.readAsStringSync();
  if (source.contains('compilerOptions {')) {
    return;
  }
  final legacyOptions = RegExp(
    r'\n[ \t]*kotlinOptions\s*\{\s*'
    r'jvmTarget\s*=\s*([^\n]+)\s*'
    r'\}',
    multiLine: true,
  );
  final match = legacyOptions.firstMatch(source);
  if (match == null) {
    return;
  }
  final expression = match.group(1)!;
  final target = expression.contains('17')
      ? 'JVM_17'
      : expression.contains('11')
          ? 'JVM_11'
          : 'JVM_1_8';
  source = source.replaceRange(match.start, match.end, '');
  source = '${source.trimRight()}\n\n'
      'kotlin {\n'
      '    compilerOptions {\n'
      '        jvmTarget = '
      'org.jetbrains.kotlin.gradle.dsl.JvmTarget.$target\n'
      '    }\n'
      '}\n';
  file.writeAsStringSync(source);
}

void _raiseIosMinimums(CorpusApp app) {
  final ios = Directory('${app.root}/ios');
  if (!ios.existsSync()) {
    return;
  }

  final podfile = File('${ios.path}/Podfile');
  if (podfile.existsSync()) {
    var source = podfile.readAsStringSync();
    final platform = RegExp(
      r'''^\s*#?\s*platform\s+:ios,\s*['"]([0-9.]+)['"]\s*$''',
      multiLine: true,
    );
    if (platform.hasMatch(source)) {
      source = source.replaceFirstMapped(
        platform,
        (match) => "platform :ios, '$_minimumIosVersion.0'",
      );
    } else {
      source = "platform :ios, '$_minimumIosVersion.0'\n$source";
    }
    if (app.legacy && !source.contains('define_singleton_method(:exists?)')) {
      source = '::File.define_singleton_method(:exists?) '
          '{ |path| ::File.exist?(path) }\n$source';
    }
    podfile.writeAsStringSync(source);
  }

  for (final entity in ios.listSync(recursive: true, followLinks: false)) {
    if (entity is! File || !entity.path.endsWith('project.pbxproj')) {
      continue;
    }
    var source = entity.readAsStringSync();
    source = source.replaceAllMapped(
      RegExp(r'IPHONEOS_DEPLOYMENT_TARGET = ([0-9.]+);'),
      (match) {
        final current = double.tryParse(match.group(1)!) ?? 0;
        return current >= _minimumIosVersion
            ? match.group(0)!
            : 'IPHONEOS_DEPLOYMENT_TARGET = $_minimumIosVersion.0;';
      },
    );
    entity.writeAsStringSync(source);
  }

  final infoPlist = File('${ios.path}/Runner/Info.plist');
  if (infoPlist.existsSync()) {
    var source = infoPlist.readAsStringSync();
    if (!source.contains('<key>NSLocalNetworkUsageDescription</key>')) {
      final closingDictionary = source.lastIndexOf('</dict>');
      if (closingDictionary >= 0) {
        source = source.replaceRange(
          closingDictionary,
          closingDictionary,
          '\t<key>NSLocalNetworkUsageDescription</key>\n'
          '\t<string>Ansight connects to the developer host on the local '
          'network.</string>\n',
        );
        infoPlist.writeAsStringSync(source);
      }
    }
  }
}

Future<CorpusResult> _validate(
  CorpusApp app,
  CorpusOptions options,
) async {
  final startedAt = DateTime.now();
  final commands = <CommandResult>[];
  final logDirectory = Directory(
    '${File(options.reportJsonPath).parent.path}/logs/'
    '${_safeName(app.repo)}',
  )..createSync(recursive: true);

  commands.add(
    await _run(
      app: app,
      name: 'pub-get',
      arguments: <String>['pub', 'get'],
      logPath: '${logDirectory.path}/pub-get.log',
      timeout: options.commandTimeout,
    ),
  );

  if (commands.last.passed &&
      app.repo == 'bizz84/movie_app_state_management_flutter') {
    final coreDirectory = Directory(
      Directory('${app.root}/../../packages/core').resolveSymbolicLinksSync(),
    );
    commands.add(
      await _run(
        app: app,
        name: 'workspace-pub-get',
        arguments: <String>['pub', 'get'],
        logPath: '${logDirectory.path}/workspace-pub-get.log',
        timeout: options.commandTimeout,
        workingDirectory: coreDirectory,
      ),
    );
    if (commands.last.passed) {
      commands.add(
        await _run(
          app: app,
          name: 'workspace-code-generation',
          arguments: <String>[
            'pub',
            'run',
            'build_runner',
            'build',
            '--delete-conflicting-outputs',
          ],
          logPath: '${logDirectory.path}/workspace-code-generation.log',
          timeout: options.commandTimeout,
          workingDirectory: coreDirectory,
        ),
      );
    }
  }

  if (commands.last.passed && _requiresCodeGeneration(app)) {
    commands.add(
      await _run(
        app: app,
        name: 'code-generation',
        arguments: <String>[
          'pub',
          'run',
          'build_runner',
          'build',
          '--delete-conflicting-outputs',
        ],
        logPath: '${logDirectory.path}/code-generation.log',
        timeout: options.commandTimeout,
      ),
    );
  }

  if (commands.last.passed && options.analyze) {
    commands.add(
      await _run(
        app: app,
        name: 'analyze-target',
        arguments: <String>[
          'analyze',
          '--no-fatal-infos',
          '--no-fatal-warnings',
          _validationTarget,
        ],
        logPath: '${logDirectory.path}/analyze-target.log',
        timeout: options.commandTimeout,
      ),
    );
  }

  if (commands.every((command) => command.passed) && options.buildAndroid) {
    _removePreviousApkArtifacts(app);
    commands.add(
      await _run(
        app: app,
        name: 'android-debug-apk',
        arguments: _androidBuildArguments(app),
        logPath: '${logDirectory.path}/android-debug-apk.log',
        timeout: options.commandTimeout,
      ),
    );
    if (commands.last.passed) {
      commands.add(_verifyApkEvidence(app, logDirectory));
    }
  }

  final result = CorpusResult(
    app: app,
    startedAt: startedAt,
    finishedAt: DateTime.now(),
    commands: commands,
  );
  final failure = result.failedCommand;
  stdout.writeln(
    '${result.passed ? 'PASS' : 'FAIL'}        ${app.repo}'
    '${failure == null ? '' : ' (${failure.name}, see ${failure.logPath})'}',
  );
  return result;
}

List<String> _androidBuildArguments(CorpusApp app) => <String>[
      'build',
      'apk',
      '--debug',
      '--target=$_validationTarget',
      '--target-platform=android-arm64',
      if (app.repo == 'RIP-Comm/sossoldi') '--flavor=default',
    ];

void _removePreviousApkArtifacts(CorpusApp app) {
  final outputDirectory = Directory(
    '${app.root}/build/app/outputs/flutter-apk',
  );
  if (!outputDirectory.existsSync()) {
    return;
  }
  for (final entity
      in outputDirectory.listSync(recursive: true, followLinks: false)) {
    if (entity is File && entity.path.endsWith('.apk')) {
      entity.deleteSync();
    }
  }
}

CommandResult _verifyApkEvidence(
  CorpusApp app,
  Directory logDirectory,
) {
  final stopwatch = Stopwatch()..start();
  final outputDirectory = Directory(
    '${app.root}/build/app/outputs/flutter-apk',
  );
  final apks = outputDirectory.existsSync()
      ? outputDirectory
          .listSync(recursive: true, followLinks: false)
          .whereType<File>()
          .where((file) => file.path.endsWith('.apk') && file.lengthSync() > 0)
          .toList()
      : <File>[];
  stopwatch.stop();

  final log = File('${logDirectory.path}/apk-evidence.log');
  log.writeAsStringSync(
    apks.isEmpty
        ? 'No non-empty APK was produced in ${outputDirectory.path}.\n'
        : '${apks.map(
              (apk) => '${apk.path}\t${apk.lengthSync()} bytes',
            ).join('\n')}\n',
  );
  return CommandResult(
    name: 'apk-evidence',
    command: const <String>['verify-fresh-apk-artifact'],
    exitCode: apks.isEmpty ? 1 : 0,
    duration: stopwatch.elapsed,
    logPath: log.path,
  );
}

Future<CommandResult> _run({
  required CorpusApp app,
  required String name,
  required List<String> arguments,
  required String logPath,
  required Duration timeout,
  Directory? workingDirectory,
}) async {
  final stopwatch = Stopwatch()..start();
  final process = await Process.start(
    app.flutterExecutable,
    arguments,
    workingDirectory: workingDirectory?.path ?? app.root,
    environment: <String, String>{
      ...Platform.environment,
      'CI': 'true',
    },
  );
  final stdoutFuture = process.stdout.transform(utf8.decoder).join();
  final stderrFuture = process.stderr.transform(utf8.decoder).join();
  var timedOut = false;
  final processExitCode = await process.exitCode.timeout(
    timeout,
    onTimeout: () async {
      timedOut = true;
      await Process.run('pkill', <String>[
        '-TERM',
        '-P',
        process.pid.toString(),
      ]);
      process.kill(ProcessSignal.sigterm);
      return 124;
    },
  );
  final processStdout = await stdoutFuture;
  final processStderr = await stderrFuture;
  stopwatch.stop();

  final log = File(logPath)..parent.createSync(recursive: true);
  log.writeAsStringSync(
    '\$ cd ${workingDirectory?.path ?? app.root}\n'
    '\$ ${app.flutterExecutable} ${arguments.join(' ')}\n\n'
    '${timedOut ? 'Timed out after ${timeout.inMinutes} minutes.\\n\\n' : ''}'
    '$processStdout$processStderr',
  );
  return CommandResult(
    name: name,
    command: <String>[app.flutterExecutable, ...arguments],
    exitCode: processExitCode,
    duration: stopwatch.elapsed,
    logPath: log.path,
  );
}

bool _requiresCodeGeneration(CorpusApp app) {
  final libraryDirectory = Directory('${app.root}/lib');
  if (!libraryDirectory.existsSync()) {
    return false;
  }
  final partDirective = RegExp(
    r'''part\s+['"]([^'"]+\.(?:g|freezed)\.dart)['"]\s*;''',
  );
  for (final entity
      in libraryDirectory.listSync(recursive: true, followLinks: false)) {
    if (entity is! File || !entity.path.endsWith('.dart')) {
      continue;
    }
    final source = entity.readAsStringSync();
    for (final match in partDirective.allMatches(source)) {
      final generated = File('${entity.parent.path}/${match.group(1)}');
      if (!generated.existsSync()) {
        return true;
      }
    }
  }
  return false;
}

void _writeReports(
  List<CorpusResult> results,
  CorpusOptions options,
) {
  final finishedAt = DateTime.now().toUtc();
  final passed = results.where((result) => result.passed).length;
  final jsonFile = File(options.reportJsonPath)
    ..parent.createSync(recursive: true);
  jsonFile.writeAsStringSync(
    const JsonEncoder.withIndent('  ').convert(<String, Object?>{
      'schema': 'ai.ansight.flutter.corpus-results.v1',
      'generatedAt': finishedAt.toIso8601String(),
      'sdkPath': options.sdkPath,
      'suiteRoot': options.suiteRoot,
      'target': _validationTarget,
      'summary': <String, Object?>{
        'total': results.length,
        'passed': passed,
        'failed': results.length - passed,
      },
      'apps': results.map((result) => result.toJson()).toList(),
    }),
  );

  final markdown = StringBuffer()
    ..writeln('# Flutter open-source corpus validation')
    ..writeln()
    ..writeln('Generated: `${finishedAt.toIso8601String()}`')
    ..writeln()
    ..writeln(
      '**$passed/${results.length} apps passed** dependency resolution, '
      'target analysis, and Android debug compilation.',
    )
    ..writeln()
    ..writeln('| App | Flutter | Result | Evidence |')
    ..writeln('| --- | --- | --- | --- |');
  for (final result in results) {
    final failure = result.failedCommand;
    final evidence = failure == null
        ? result.commands
            .map((command) => '${command.name}: ${command.duration.inSeconds}s')
            .join(', ')
        : '${failure.name} failed; `${failure.logPath}`';
    markdown.writeln(
      '| ${result.app.repo} | ${result.app.flutterLabel} | '
      '${result.passed ? 'PASS' : 'FAIL'} | $evidence |',
    );
  }
  markdown
    ..writeln()
    ..writeln(
      'Each app contains `$_validationTarget`, which initializes and '
      'activates the native Ansight runtime, installs Flutter '
      'instrumentation, records an integration event, and then invokes the '
      'upstream application entry point.',
    );
  final markdownFile = File(options.reportMarkdownPath)
    ..parent.createSync(recursive: true);
  markdownFile.writeAsStringSync(markdown.toString());
}

String _relativePath(String fromDirectory, String toDirectory) {
  final from = Directory(fromDirectory).resolveSymbolicLinksSync().split(
        Platform.pathSeparator,
      );
  final to = Directory(toDirectory).resolveSymbolicLinksSync().split(
        Platform.pathSeparator,
      );
  var common = 0;
  while (common < from.length &&
      common < to.length &&
      from[common] == to[common]) {
    common += 1;
  }
  final segments = <String>[
    ...List<String>.filled(from.length - common, '..'),
    ...to.skip(common),
  ];
  return segments.isEmpty ? '.' : segments.join('/');
}

int _compareVersions(String left, String right) {
  final leftParts = left
      .split(RegExp(r'[^0-9]+'))
      .where((part) => part.isNotEmpty)
      .map(int.parse)
      .toList();
  final rightParts = right
      .split(RegExp(r'[^0-9]+'))
      .where((part) => part.isNotEmpty)
      .map(int.parse)
      .toList();
  final length = leftParts.length > rightParts.length
      ? leftParts.length
      : rightParts.length;
  for (var index = 0; index < length; index += 1) {
    final leftPart = index < leftParts.length ? leftParts[index] : 0;
    final rightPart = index < rightParts.length ? rightParts[index] : 0;
    if (leftPart != rightPart) {
      return leftPart.compareTo(rightPart);
    }
  }
  return 0;
}

String _safeName(String value) =>
    value.replaceAll(RegExp(r'[^A-Za-z0-9._-]+'), '__');

void _printUsage() {
  stdout.writeln('''
Validate ansight_flutter against the 22-app open-source Flutter corpus.

Usage:
  dart run tool/flutter_corpus.dart [integrate|validate|all] [options]

Options:
  --suite-root=<path>       Corpus root containing build-setup-matrix.json
  --matrix=<path>           Override the build matrix
  --sdk-path=<path>         ansight_flutter package path
  --app=<substring>         Select an app; may be repeated
  --no-analyze              Skip target analysis
  --no-build                Skip Android debug APK compilation
  --timeout-minutes=<n>     Per-command timeout (default: 20)
  --report-json=<path>      JSON evidence output
  --report-markdown=<path>  Markdown summary output
''');
}
