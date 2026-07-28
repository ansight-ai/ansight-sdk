import 'dart:io';

Future<void> main() async {
  final packageRoot = File.fromUri(Platform.script).parent.parent.path;
  final generation = await Process.run(
    Platform.resolvedExecutable,
    <String>[
      'run',
      'pigeon',
      '--input',
      'pigeons/ansight_messages.dart',
    ],
    workingDirectory: packageRoot,
  );
  stdout.write(generation.stdout);
  stderr.write(generation.stderr);
  if (generation.exitCode != 0) {
    exitCode = generation.exitCode;
    return;
  }

  final dartOutput =
      File('$packageRoot/lib/src/generated/ansight_messages.g.dart');
  var dartSource = dartOutput.readAsStringSync();
  dartSource = dartSource
      .replaceFirst(
        '// ignore_for_file: unused_import, unused_shown_name',
        '// ignore_for_file: unnecessary_import, unused_import, '
            'unused_shown_name',
      )
      .replaceFirst(
        "import 'dart:typed_data' show Float64List, Int32List, Int64List;",
        "import 'dart:typed_data' "
            'show Float64List, Int32List, Int64List, Uint8List;',
      )
      .replaceFirst(
        "import 'package:flutter/services.dart';",
        "import 'package:flutter/foundation.dart' "
            "show ReadBuffer, WriteBuffer;\n"
            "import 'package:flutter/services.dart';",
      )
      .replaceAll(
        'return replyList.firstOrNull;',
        'return replyList.isEmpty ? null : replyList.first;',
      );
  dartOutput.writeAsStringSync(dartSource);

  final kotlinOutput = File(
    '$packageRoot/android/src/main/kotlin/ai/ansight/flutter/'
    'AnsightMessages.g.kt',
  );
  var kotlinSource = kotlinOutput.readAsStringSync();
  kotlinSource = kotlinSource
      .replaceAll(
        'import java.io.ByteArrayOutputStream\n'
            'import java.nio.ByteBuffer\n',
        '',
      )
      .replaceAll(
        'private open class AnsightMessagesPigeonCodec : '
            'StandardMessageCodec() {\n'
            '  override fun readValueOfType(type: Byte, '
            'buffer: ByteBuffer): Any? {\n'
            '    return     super.readValueOfType(type, buffer)\n'
            '  }\n'
            '  override fun writeValue(stream: ByteArrayOutputStream, '
            'value: Any?)   {\n'
            '    super.writeValue(stream, value)\n'
            '  }\n'
            '}\n\n\n',
        '',
      )
      .replaceAll(
        'AnsightMessagesPigeonCodec()',
        'StandardMessageCodec.INSTANCE as MessageCodec<Any?>',
      );
  kotlinOutput.writeAsStringSync(kotlinSource);

  final formatting = await Process.run(
    Platform.resolvedExecutable,
    <String>['format', dartOutput.path],
    workingDirectory: packageRoot,
  );
  stdout.write(formatting.stdout);
  stderr.write(formatting.stderr);
  if (formatting.exitCode != 0) {
    exitCode = formatting.exitCode;
    return;
  }

  stdout.writeln('Generated Flutter, Kotlin, and Swift Pigeon transports.');
}
