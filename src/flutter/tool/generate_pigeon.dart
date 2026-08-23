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
      )
      .replaceFirst(
        'return a.length == b.length &&\n'
            '        a.indexed\n'
            '            .every(((int, dynamic) item) => '
            '_deepEquals(item.\$2, b[item.\$1]));',
        'if (a.length != b.length) {\n'
            '      return false;\n'
            '    }\n'
            '    for (int index = 0; index < a.length; index += 1) {\n'
            '      if (!_deepEquals(a[index], b[index])) {\n'
            '        return false;\n'
            '      }\n'
            '    }\n'
            '    return true;',
      );
  dartOutput.writeAsStringSync(dartSource);

  final kotlinOutput = File(
    '$packageRoot/android/src/main/kotlin/ai/ansight/flutter/'
    'AnsightMessages.g.kt',
  );
  var kotlinSource = kotlinOutput.readAsStringSync();
  // Older Pigeon output emitted an unused pass-through codec when the bridge
  // had no custom model types. Keep the generated codec (and its Java imports)
  // now that network request models cross the runtime boundary through it.
  if (kotlinSource.contains(
    'return     super.readValueOfType(type, buffer)',
  )) {
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
  }
  kotlinSource = kotlinSource.replaceAll(
    RegExp(r'[ \t]+$', multiLine: true),
    '',
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
