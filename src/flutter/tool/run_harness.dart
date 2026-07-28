import 'dart:convert';
import 'dart:io';

const String _pairingDocumentSchema = 'ansight.pairing-config-document.v1';
const String _pairingConfigSchema = 'ansight.pairing-config.v1';
const int _defaultDiscoveryPort = 45123;

Future<void> main(List<String> arguments) async {
  final options = _HarnessLaunchOptions.parse(arguments);
  if (options.showHelp) {
    stdout.write(_usage);
    return;
  }

  final packageRoot = File.fromUri(Platform.script).parent.parent.path;
  final exampleRoot = '$packageRoot${Platform.pathSeparator}example';
  final flutterArguments = <String>[
    'run',
    if (options.release) '--release',
    if (options.deviceId != null) ...<String>['-d', options.deviceId!],
  ];

  Directory? temporaryDirectory;
  var commandExitCode = 0;
  try {
    if (options.pairingConfigPath != null) {
      final pairingConfig = await _readPairingConfig(
        options.pairingConfigPath!,
      );
      final hostAddress =
          options.hostAddress ?? await _findDevelopmentHostAddress();
      final pairingDocument = buildHarnessPairingDocument(
        pairingConfig,
        hostAddress: hostAddress,
        discoveryPort: options.discoveryPort,
      );

      temporaryDirectory = await Directory.systemTemp.createTemp(
        'ansight-flutter-harness-',
      );
      final definesFile = File(
        '${temporaryDirectory.path}${Platform.pathSeparator}defines.json',
      );
      await definesFile.writeAsString(
        jsonEncode(<String, String>{
          'ANSIGHT_PAIRING_CONFIG_BASE64': base64Encode(
            utf8.encode(jsonEncode(pairingDocument)),
          ),
        }),
        flush: true,
      );
      flutterArguments.add('--dart-define-from-file=${definesFile.path}');

      stdout.writeln(
        'Using signed pairing config with discovery host '
        '$hostAddress:${options.discoveryPort}.',
      );
    }

    flutterArguments.addAll(options.flutterArguments);
    final process = await Process.start(
      'flutter',
      flutterArguments,
      workingDirectory: exampleRoot,
      mode: ProcessStartMode.inheritStdio,
    );
    commandExitCode = await process.exitCode;
    if (commandExitCode != 0) {
      stderr.writeln('flutter run exited with code $commandExitCode.');
    }
  } on FormatException catch (error) {
    stderr.writeln('error: ${error.message}');
    commandExitCode = 64;
  } on FileSystemException catch (error) {
    stderr.writeln('error: ${error.message}');
    commandExitCode = 66;
  } finally {
    if (temporaryDirectory != null && temporaryDirectory.existsSync()) {
      await temporaryDirectory.delete(recursive: true);
    }
  }
  exitCode = commandExitCode;
}

Future<Map<String, Object?>> _readPairingConfig(String path) async {
  final source = await File(path).readAsString();
  final decoded = jsonDecode(source);
  if (decoded is! Map<String, Object?>) {
    throw const FormatException('Pairing config must contain a JSON object.');
  }
  return decoded;
}

Map<String, Object?> buildHarnessPairingDocument(
  Map<String, Object?> source, {
  required String hostAddress,
  required int discoveryPort,
}) {
  final schema = source['schema'];
  final Map<String, Object?> document;
  if (schema == _pairingConfigSchema) {
    document = <String, Object?>{
      'schema': _pairingDocumentSchema,
      'config': source,
    };
  } else if (schema == _pairingDocumentSchema) {
    document = Map<String, Object?>.from(source);
    if (document['config'] is! Map<String, Object?>) {
      throw const FormatException(
        'Pairing document must contain a config object.',
      );
    }
  } else {
    throw FormatException('Unsupported pairing schema: $schema');
  }

  final existingDiscovery = document['discovery'];
  final discovery = existingDiscovery is Map<String, Object?>
      ? Map<String, Object?>.from(existingDiscovery)
      : <String, Object?>{};
  final existingAddresses = discovery['hostAddresses'];
  final hostAddresses = <String>[
    hostAddress,
    if (existingAddresses is List<Object?>)
      ...existingAddresses.whereType<String>().where(
            (address) => address != hostAddress,
          ),
  ];
  discovery
    ..['schema'] = 'ansight.discovery-hint.v1'
    ..['source'] = 'flutter-harness-launcher'
    ..['hostAddresses'] = hostAddresses
    ..['discoveryPort'] = discoveryPort
    ..['hostName'] = Platform.localHostname
    ..['capturedAt'] = DateTime.now().toUtc().toIso8601String();
  document['discovery'] = discovery;
  return document;
}

Future<String> _findDevelopmentHostAddress() async {
  final interfaces = await NetworkInterface.list(
    type: InternetAddressType.IPv4,
    includeLoopback: false,
    includeLinkLocal: false,
  );
  final candidates = <_HostAddressCandidate>[];
  for (final interface in interfaces) {
    for (final address in interface.addresses) {
      if (!address.isLoopback && !_isLinkLocal(address.address)) {
        candidates.add(
          _HostAddressCandidate(
            interfaceName: interface.name,
            address: address.address,
          ),
        );
      }
    }
  }
  if (candidates.isEmpty) {
    throw const FormatException(
      'No LAN IPv4 address was found. Pass --host-address explicitly.',
    );
  }
  candidates.sort(
    (left, right) => _interfacePriority(
      left.interfaceName,
    ).compareTo(_interfacePriority(right.interfaceName)),
  );
  return candidates.first.address;
}

bool _isLinkLocal(String address) => address.startsWith('169.254.');

int _interfacePriority(String name) {
  final normalized = name.toLowerCase();
  if (normalized == 'en0' ||
      normalized == 'wlan0' ||
      normalized.contains('wi-fi') ||
      normalized.contains('wifi')) {
    return 0;
  }
  if (normalized.startsWith('en')) {
    return 1;
  }
  return 2;
}

class _HostAddressCandidate {
  const _HostAddressCandidate({
    required this.interfaceName,
    required this.address,
  });

  final String interfaceName;
  final String address;
}

class _HarnessLaunchOptions {
  const _HarnessLaunchOptions({
    required this.deviceId,
    required this.pairingConfigPath,
    required this.hostAddress,
    required this.discoveryPort,
    required this.release,
    required this.showHelp,
    required this.flutterArguments,
  });

  factory _HarnessLaunchOptions.parse(List<String> arguments) {
    String? deviceId;
    String? pairingConfigPath;
    String? hostAddress;
    var discoveryPort = _defaultDiscoveryPort;
    var release = false;
    var showHelp = false;
    final flutterArguments = <String>[];

    for (var index = 0; index < arguments.length; index += 1) {
      final argument = arguments[index];
      String nextValue(String option) {
        if (index + 1 >= arguments.length) {
          throw FormatException('$option requires a value.');
        }
        index += 1;
        return arguments[index];
      }

      switch (argument) {
        case '--device':
        case '-d':
          deviceId = nextValue(argument);
          break;
        case '--pairing-config':
          pairingConfigPath = nextValue(argument);
          break;
        case '--host-address':
          hostAddress = nextValue(argument);
          break;
        case '--discovery-port':
          discoveryPort = int.tryParse(nextValue(argument)) ?? 0;
          if (discoveryPort < 1 || discoveryPort > 65535) {
            throw const FormatException(
              'Discovery port must be between 1 and 65535.',
            );
          }
          break;
        case '--release':
          release = true;
          break;
        case '--help':
        case '-h':
          showHelp = true;
          break;
        case '--':
          flutterArguments.addAll(arguments.skip(index + 1));
          index = arguments.length;
          break;
        default:
          throw FormatException('Unknown option: $argument');
      }
    }

    return _HarnessLaunchOptions(
      deviceId: deviceId,
      pairingConfigPath: pairingConfigPath,
      hostAddress: hostAddress,
      discoveryPort: discoveryPort,
      release: release,
      showHelp: showHelp,
      flutterArguments: flutterArguments,
    );
  }

  final String? deviceId;
  final String? pairingConfigPath;
  final String? hostAddress;
  final int discoveryPort;
  final bool release;
  final bool showHelp;
  final List<String> flutterArguments;
}

const String _usage = '''
Launch the Ansight Flutter harness with optional signed pairing.

Usage:
  dart run tool/run_harness.dart [options] [-- <flutter-run arguments>]

Options:
  -d, --device <id>              Flutter device id.
      --pairing-config <path>    Studio .ans.json pairing config.
      --host-address <address>   Studio host LAN address. Auto-detected.
      --discovery-port <port>    Pairing discovery port. Default: 45123.
      --release                  Run a release build.
  -h, --help                     Show this help.

The launcher wraps a signed public config in a pairing document with LAN
discovery metadata. It never modifies the signed config and removes the
temporary Dart define file after flutter run exits.
''';
