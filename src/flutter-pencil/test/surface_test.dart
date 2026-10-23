import 'dart:convert';
import 'dart:async';
import 'package:ansight_flutter/ansight.dart';
import 'package:ansight_pencil/src/surface.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final captured = <Map<String,dynamic>>[];
  const context = AnsightToolContext(requestId:'test',toolId:'ui.simulate_pen_framework',platform:'ios');
  const down = {'phase':'down','x':.25,'y':.5,'pressure':.8,'altitudeRadians':.4,'azimuthRadians':.7,'rollRadians':1.2};
  const up = {'phase':'up','x':.25,'y':.5};
  Future<AnsightToolResult> command(Map<String,Object?> value) => handlePencilSurfaceCommand({'command':jsonEncode({'runId':'test',...value})},context);
  setUp(() {
    captured.clear();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(const MethodChannel('ai.ansight/pencil'), (call) async {
      if(call.method=='capture') captured.add(jsonDecode(call.arguments as String) as Map<String,dynamic>);
      return true;
    });
  });
  tearDown(() async { await command({'action':'finish'}); });
  testWidgets('existing Listener receives pressure, tilt and roll; samples are captured', (tester) async {
    final received=<PointerEvent>[];
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:AnsightPencilSurface(id:'canvas',child:Listener(
      behavior:HitTestBehavior.opaque,onPointerDown:received.add,onPointerUp:received.add,onPointerHover:received.add,child:const SizedBox.expand()))));
    expect((await command({'action':'prepare','target':'canvas','samples':[down,up]})).success,isTrue);
    expect((await command({'action':'sample','sample':down})).success,isTrue);
    expect((await command({'action':'sample','sample':up})).success,isTrue);
    expect(received.first.kind,PointerDeviceKind.stylus);
    expect(received.first.pressure,.8);
    expect(received.first.ansightPencil?['rollRadians'],1.2);
    expect(captured.first['sample']['pressure'],.8);
  });
  testWidgets('recording disabled still delivers contact and cleanup to existing handlers', (tester) async {
    final received=<PointerEvent>[];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(const MethodChannel('ai.ansight/pencil'), (call) async => true);
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:AnsightPencilSurface(id:'canvas',child:Listener(
      behavior:HitTestBehavior.opaque,onPointerDown:received.add,onPointerUp:received.add,onPointerHover:received.add,child:const SizedBox.expand()))));
    expect((await command({'action':'prepare','target':'canvas','samples':[down,up]})).success,isTrue);
    expect((await command({'action':'sample','sample':down})).success,isTrue);
    expect((await command({'action':'sample','sample':up})).success,isTrue);
    expect((await command({'action':'finish'})).success,isTrue);
    expect(received.whereType<PointerDownEvent>().single.pressure,.8);
    expect(received.whereType<PointerUpEvent>(),hasLength(1));
    expect(captured,isEmpty);
  });
  testWidgets('covering the surface prevents delivery', (tester) async {
    var calls=0;
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:Stack(children:[
      AnsightPencilSurface(id:'canvas',child:Listener(behavior:HitTestBehavior.opaque,onPointerDown:(_)=>calls++,child:const SizedBox.expand())),
      const Positioned.fill(child:AbsorbPointer(child:ColoredBox(color:Color(0xff000000)))),
    ])));
    expect((await command({'action':'prepare','target':'canvas','samples':[down,up]})).success,isFalse);
    expect(calls,0);expect(captured,isEmpty);
  });
  testWidgets('hover enters and exits the existing MouseRegion', (tester) async {
    var enters=0, exits=0;
    final hover=<PointerEvent>[];
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:AnsightPencilSurface(id:'canvas',child:MouseRegion(
      onEnter:(_)=>enters++,onExit:(_)=>exits++,onHover:hover.add,child:const SizedBox.expand()))));
    final samples=[
      {'phase':'hoverEnter','x':.25,'y':.5,'distance':.8},
      {'phase':'hoverMove','x':.3,'y':.5,'distance':.3},
      {'phase':'hoverExit','x':.3,'y':.5,'distance':0},
    ];
    expect((await command({'action':'prepare','target':'canvas','samples':samples})).success,isTrue);
    for(final sample in samples) {
      expect((await command({'action':'sample','sample':sample})).success,isTrue);
      await tester.pump();
    }
    expect(enters,1);expect(exits,1);
    expect(hover.any((event)=>event.distance==.3),isTrue);
  });
  testWidgets('real input is preserved and blocks a concurrent simulated stroke', (tester) async {
    var calls=0;
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:AnsightPencilSurface(id:'canvas',child:Listener(behavior:HitTestBehavior.opaque,onPointerDown:(_)=>calls++,child:const SizedBox.expand()))));
    final gesture=await tester.startGesture(const Offset(200,300));
    expect(calls,1);
    expect((await command({'action':'prepare','target':'canvas','samples':[down,up]})).success,isFalse);
    await gesture.up();expect(captured,isEmpty);
  });
  testWidgets('unmount while native validation awaits does not dispatch a late pointer', (tester) async {
    var calls=0;
    await tester.pumpWidget(Directionality(textDirection:TextDirection.ltr,child:AnsightPencilSurface(id:'canvas',child:Listener(behavior:HitTestBehavior.opaque,onPointerDown:(_)=>calls++,child:const SizedBox.expand()))));
    expect((await command({'action':'prepare','target':'canvas','samples':[down,up]})).success,isTrue);
    final waiting=Completer<void>(), release=Completer<void>();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(const MethodChannel('ai.ansight/pencil'), (call) async {
      if(call.method=='requirePlayback') { waiting.complete(); await release.future; }
      return true;
    });
    final pending=command({'action':'sample','sample':down});
    await waiting.future;
    await tester.pumpWidget(const SizedBox());
    release.complete();
    expect((await pending).success,isFalse);expect(calls,0);
  });
}
