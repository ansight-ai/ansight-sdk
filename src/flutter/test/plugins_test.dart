import 'dart:async';
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter_test/flutter_test.dart';
import 'ansight_test.dart' show FakeNativeTransport;

class TestPlugin implements AnsightPlugin {
  TestPlugin(this.id, this.callback);
  @override
  final String id;
  final Future<void> Function() callback;
  @override
  Future<void> initialize() => callback();
}

class InitializationTransport extends FakeNativeTransport {
  bool failInitialization = false;
  @override
  Future<AnsightJson> invoke(String method, [AnsightJson? arguments]) async {
    if (failInitialization && method == 'initialize') {
      throw StateError('native failed');
    }
    if (method == 'snapshot') return {'registeredTools': 1};
    return super.invoke(method, arguments);
  }
}

void main() {
  test(
      'builder snapshots are passive, preserve plugins, and omit them from JSON',
      () {
    var calls = 0;
    final plugin = TestPlugin('optional', () async {
      calls++;
    });
    final builder = createOptionsBuilder()
        .withPlugin(plugin)
        .withAnsightDefaults()
        .withPlugin(plugin);
    final first = builder.build();
    final second =
        createOptionsBuilder(first).withReadWriteToolAccess().build();
    expect(second.plugins, [plugin]);
    expect(first.plugins, isNot(same(second.plugins)));
    expect(() => first.plugins.clear(), throwsUnsupportedError);
    expect(second.toJson().containsKey('plugins'), isFalse);
    expect(second.toJson()['toolGuard'], 'readWrite');
    expect(calls, 0);
  });
  for (final activate in [false, true]) {
    test(
        'initialize activate=$activate waits for plugins and returns updated snapshot',
        () async {
      final transport = InitializationTransport();
      final sdk = Ansight.withTransport(transport);
      final gate = Completer<void>();
      final entered = Completer<void>();
      final options = createOptionsBuilder()
          .withReadWriteToolAccess()
          .withPlugin(TestPlugin('optional', () async {
            expect(transport.calls.first.method,
                activate ? 'initializeAndActivate' : 'initialize');
            entered.complete();
            await gate.future;
          }))
          .build();
      var complete = false;
      final pending = (activate
              ? sdk.initializeAndActivate(options)
              : sdk.initialize(options))
          .then((value) {
        complete = true;
        return value;
      });
      await entered.future;
      expect(complete, isFalse);
      expect(transport.calls.first.arguments!.containsKey('plugins'), isFalse);
      expect(transport.calls.first.arguments!['toolGuard'], 'readWrite');
      gate.complete();
      expect((await pending).registeredTools, 1);
    });
  }
  test('native failure skips plugins', () async {
    final transport = InitializationTransport()..failInitialization = true;
    var called = false;
    final options = createOptionsBuilder()
        .withPlugin(TestPlugin('optional', () async {
          called = true;
        }))
        .build();
    await expectLater(
        Ansight.withTransport(transport).initialize(options), throwsStateError);
    expect(called, isFalse);
  });
  test('plugin failure rejects initialization and skips remaining plugins',
      () async {
    var called = false;
    final options = createOptionsBuilder()
        .withPlugin(TestPlugin('first', () async {
          throw StateError('plugin failed');
        }))
        .withPlugin(TestPlugin('second', () async {
          called = true;
        }))
        .build();
    await expectLater(
        Ansight.withTransport(InitializationTransport()).initialize(options),
        throwsStateError);
    expect(called, isFalse);
  });
}
