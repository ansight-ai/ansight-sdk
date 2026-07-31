import 'dart:io';

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

  var commandExitCode = 0;
  try {
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
  }
  exitCode = commandExitCode;
}

class _HarnessLaunchOptions {
  const _HarnessLaunchOptions({
    required this.deviceId,
    required this.release,
    required this.showHelp,
    required this.flutterArguments,
  });

  factory _HarnessLaunchOptions.parse(List<String> arguments) {
    String? deviceId;
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
      release: release,
      showHelp: showHelp,
      flutterArguments: flutterArguments,
    );
  }

  final String? deviceId;
  final bool release;
  final bool showHelp;
  final List<String> flutterArguments;
}

const String _usage = '''
Launch the Ansight Flutter harness.

Usage:
  dart run tool/run_harness.dart [options] [-- <flutter-run arguments>]

Options:
  -d, --device <id>  Flutter device id.
      --release      Run a release build.
  -h, --help         Show this help.

Simulators and emulators enroll at runtime when Studio is open and signed in.
Use the harness QR action for a physical device's first enrollment.
''';
