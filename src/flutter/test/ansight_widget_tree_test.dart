import 'dart:async';
import 'dart:typed_data';

import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class WidgetTreeTransport implements AnsightNativeTransport {
  @override
  AnsightNativeEventCallback? eventCallback;
  @override
  AnsightNativeToolCallCallback? toolCallCallback;

  Completer<AnsightJson>? pending;

  @override
  Future<AnsightJson> invoke(String method, [AnsightJson? arguments]) async {
    if (method == 'resolveToolCall') {
      pending!.complete(Map<String, Object?>.from(arguments!['result'] as Map));
    }
    return <String, Object?>{'success': true, 'message': 'ok'};
  }

  Future<AnsightJson> call(String toolId, [AnsightJson? arguments]) {
    pending = Completer<AnsightJson>();
    toolCallCallback!(<String, Object?>{
      'requestId': 'test',
      'toolId': toolId,
      'platform': 'flutter',
      'arguments': arguments ?? <String, Object?>{},
    });
    return pending!.future;
  }

  @override
  Future<AnsightJson> queueBinaryTransfer({
    required String requestId,
    required Uint8List data,
    int chunkBytes = 65536,
  }) async =>
      <String, Object?>{'success': true};

  @override
  Future<AnsightJson> recordNetworkRequest(
          AnsightNetworkRequest request) async =>
      <String, Object?>{'success': true};
}

class StructuralWrapper extends StatelessWidget {
  const StructuralWrapper({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) => child;
}

class _PrivateTapControl extends GestureDetector {
  _PrivateTapControl({super.key, super.onTap, super.child});
}

class _KeyedStructuralWrapper extends StatelessWidget {
  const _KeyedStructuralWrapper({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) => child;
}

Widget deeplyWrapped(Widget child) {
  for (var index = 0; index < 150; index++) {
    child = StructuralWrapper(child: child);
  }
  return child;
}

List<AnsightJson> treeNodes(AnsightJson tree) {
  final nodes = <AnsightJson>[];
  void visit(Map node) {
    nodes.add(Map<String, Object?>.from(node));
    for (final child in node['children'] as List? ?? <Object?>[]) {
      visit(child as Map);
    }
  }

  visit(tree['root'] as Map);
  return nodes;
}

void main() {
  late WidgetTreeTransport transport;
  late AnsightFlutterInstrumentation instrumentation;

  setUp(() async {
    transport = WidgetTreeTransport();
    instrumentation = AnsightFlutterInstrumentation.withAnsight(
      Ansight.withTransport(transport),
    );
    await instrumentation.install(captureFrames: false, captureErrors: false);
  });

  tearDown(() => instrumentation.uninstall());

  Future<AnsightJson> capture(String toolId, [AnsightJson? arguments]) async {
    final response = await transport.call(toolId, arguments);
    expect(response['success'], isTrue,
        reason: response['message']?.toString());
    return Map<String, Object?>.from(response['result'] as Map);
  }

  testWidgets('both widget-tree entry points reach controls past deep wrappers',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: deeplyWrapped(Scaffold(
        body: Column(children: <Widget>[
          const Text('Visible screen', key: ValueKey('screen-title')),
          _PrivateTapControl(
            key: const ValueKey('private-control'),
            onTap: () {},
            child: const Text('Private control'),
          ),
          TextButton(
            key: const ValueKey('open-book'),
            onPressed: () {},
            child: const Text('Open book'),
          ),
        ]),
      )),
    ));
    await tester.pumpAndSettle();

    String? firstButtonId;
    for (final toolId in <String>[
      'flutter.get_widget_tree',
      '__ansight_flutter.visual_tree',
    ]) {
      final tree = await capture(toolId, <String, Object?>{
        'maxDepth': 32,
        'maxNodes': 1000,
      });
      final nodes = treeNodes(tree);
      expect(tree['truncated'], isFalse);
      expect(nodes.any((node) => node['text'] == 'Visible screen'), isTrue);
      final button = nodes.singleWhere(
        (node) => node['automationId'] == 'open-book',
      );
      final privateControl = nodes.singleWhere(
        (node) => node['automationId'] == 'private-control',
      );
      expect(privateControl['role'], 'button');
      expect(privateControl['supportedActions'], contains('tap'));
      expect(button['supportedActions'], contains('tap'));
      expect(button['bounds'], isNotNull);
      firstButtonId ??= button['id'] as String;
      expect(button['id'], firstButtonId);
      final inspected = await transport.call(
          'flutter.inspect_widget', <String, Object?>{'id': button['id']});
      expect(inspected['success'], isTrue);
    }
  });

  testWidgets('viewport uses logical RenderView dimensions, not content bounds',
      (tester) async {
    // Keep the test compatible with the package's Flutter 3.0 minimum.
    // ignore: deprecated_member_use
    final window = tester.binding.window;
    // ignore: deprecated_member_use
    window.devicePixelRatioTestValue = 3;
    // ignore: deprecated_member_use
    window.physicalSizeTestValue = const Size(1200, 2400);
    // ignore: deprecated_member_use
    addTearDown(window.clearDevicePixelRatioTestValue);
    // ignore: deprecated_member_use
    addTearDown(window.clearPhysicalSizeTestValue);
    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child: Center(child: Text('Small content')),
    ));
    final tree = await capture('flutter.get_widget_tree');
    expect(tree['coordinateSpace'], <String, Object?>{
      'x': 0.0,
      'y': 0.0,
      'width': 400.0,
      'height': 800.0,
    });
    final content = treeNodes(tree).firstWhere(
      (node) => node['text'] == 'Small content',
    );
    expect((content['bounds'] as Map)['width'], lessThan(400));
  });

  testWidgets('compact captures omit offstage branches and retain paint owners',
      (tester) async {
    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child: Stack(children: <Widget>[
        Offstage(offstage: true, child: Text('Hidden route')),
        Opacity(
          opacity: 0.4,
          child: ColoredBox(
              color: Color(0xFF123456), child: Text('Visible route')),
        ),
      ]),
    ));
    final nodes = treeNodes(await capture('__ansight_flutter.visual_tree'));
    expect(nodes.any((node) => node['text'] == 'Hidden route'), isFalse);
    expect(nodes.any((node) => node['text'] == 'Visible route'), isTrue);
    expect(
        nodes.any((node) => (node['visual'] as Map)['opacity'] == 0.4), isTrue);
    expect(
        nodes.any(
            (node) => (node['visual'] as Map)['background'] == '#FF123456'),
        isTrue);
  });

  testWidgets('covered routes are omitted while dialog backgrounds remain',
      (tester) async {
    final navigatorKey = GlobalKey<NavigatorState>();
    await tester.pumpWidget(MaterialApp(
      navigatorKey: navigatorKey,
      home: const Scaffold(body: Text('Covered route')),
    ));
    unawaited(navigatorKey.currentState!.push(MaterialPageRoute<void>(
      builder: (_) => const Scaffold(body: Text('Current route')),
    )));
    await tester.pumpAndSettle();
    var nodes = treeNodes(await capture('__ansight_flutter.visual_tree'));
    expect(nodes.any((node) => node['text'] == 'Covered route'), isFalse);
    expect(nodes.any((node) => node['text'] == 'Current route'), isTrue);

    unawaited(showDialog<void>(
      context: navigatorKey.currentContext!,
      builder: (_) => const AlertDialog(title: Text('Dialog title')),
    ));
    await tester.pumpAndSettle();
    nodes = treeNodes(await capture('__ansight_flutter.visual_tree'));
    expect(nodes.any((node) => node['text'] == 'Covered route'), isFalse);
    expect(nodes.any((node) => node['text'] == 'Current route'), isTrue);
    expect(nodes.any((node) => node['text'] == 'Dialog title'), isTrue);
  });

  testWidgets('structural wrappers do not consume the retained node budget',
      (tester) async {
    await tester.pumpWidget(Directionality(
      textDirection: TextDirection.ltr,
      child: deeplyWrapped(const Text('Deep label')),
    ));
    final tree = await capture('flutter.get_widget_tree', <String, Object?>{
      'maxDepth': 2,
      'maxNodes': 8,
    });
    expect(tree['truncated'], isFalse);
    expect(tree['nodeCount'] as int, lessThanOrEqualTo(8));
    expect(tree['visitedNodeCount'] as int, greaterThan(150));
    expect(treeNodes(tree).any((node) => node['text'] == 'Deep label'), isTrue);
  });

  testWidgets('framework keys and scroll observers do not hide screen content',
      (tester) async {
    Widget child = const _KeyedStructuralWrapper(
      key: ValueKey('meaningful-wrapper'),
      child: SingleChildScrollView(
        key: ValueKey('real-scroll'),
        child: Text('Screen content'),
      ),
    );
    final controllers = <ScrollController>[];
    addTearDown(() {
      for (final controller in controllers) {
        controller.dispose();
      }
    });
    for (var index = 0; index < 20; index++) {
      final controller = ScrollController();
      controllers.add(controller);
      child = ScrollConfiguration(
        behavior: const ScrollBehavior(),
        child: PrimaryScrollController(
          controller: controller,
          child: NotificationListener<ScrollNotification>(
            child: _KeyedStructuralWrapper(
              key: GlobalKey(debugLabel: 'structural-wrapper-$index'),
              child: child,
            ),
          ),
        ),
      );
    }
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: child)));
    final tree = await capture('flutter.get_widget_tree', <String, Object?>{
      'maxDepth': 32,
    });
    final nodes = treeNodes(tree);
    expect(tree['truncated'], isFalse);
    expect(nodes.any((node) => node['text'] == 'Screen content'), isTrue);
    expect(
      nodes.where((node) =>
          node['key']?.toString().contains('structural-wrapper-') == true),
      hasLength(20),
    );
    expect(nodes.any((node) => node['automationId'] == 'meaningful-wrapper'),
        isTrue);
    final scroll = nodes.singleWhere(
      (node) => node['automationId'] == 'real-scroll',
    );
    expect(scroll['role'], 'scrollview');
    expect(scroll['supportedActions'], contains('scroll'));
    final raw = await capture('flutter.get_widget_tree', <String, Object?>{
      'compactUnaryNodes': false,
      'maxDepth': 100,
    });
    final rawTypes = raw['types'] as List;
    for (final node in treeNodes(raw)) {
      final type = rawTypes[node['typeId'] as int];
      if (type == 'ScrollConfiguration' ||
          type == 'PrimaryScrollController' ||
          type.toString().startsWith('NotificationListener<')) {
        expect(node['role'], 'view');
        expect(node['interactable'], isFalse);
      }
    }
  });

  testWidgets('retained node limit is enforced across branching content',
      (tester) async {
    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child:
          Column(children: <Widget>[Text('One'), Text('Two'), Text('Three')]),
    ));
    final tree = await capture(
        'flutter.get_widget_tree', <String, Object?>{'maxNodes': 2});
    expect(tree['truncated'], isTrue);
    expect(tree['nodeCount'], 2);
    expect(treeNodes(tree), hasLength(2));
  });

  testWidgets(
      'raw hierarchy is optional and also supplies viewport coordinates',
      (tester) async {
    await tester.pumpWidget(Directionality(
      textDirection: TextDirection.ltr,
      child: deeplyWrapped(const Text('Deep label')),
    ));
    final tree = await capture('flutter.get_widget_tree', <String, Object?>{
      'compactUnaryNodes': false,
      'maxDepth': 32,
    });
    expect(tree['compactUnaryNodes'], isFalse);
    expect(tree['truncated'], isTrue);
    expect(tree['coordinateSpace'], isNotNull);
    expect((tree['root'] as Map)['bounds'], tree['coordinateSpace']);
  });
}
