import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

const _channel = MethodChannel('ai.ansight/pencil');
final _surfaces = <String, _SurfaceState>{};
final _metadata = Expando<Map<String, Object?>>('Ansight Pencil sample');
AnsightToolRegistration? _registration;
_Run? _active;
bool _handling = false;
int _nextPointer = -1900000000;

/// Extra properties (including roll) are available without changing Flutter's event classes.
extension AnsightPencilPointerData on PointerEvent {
  Map<String, Object?>? get ansightPencil => _metadata[original ?? this];
}

/// Wrap the existing drawing widget. Normal input and gesture handlers are unchanged.
class AnsightPencilSurface extends StatefulWidget {
  const AnsightPencilSurface({super.key, required this.id, required this.child});
  final String id;
  final Widget child;
  @override
  State<AnsightPencilSurface> createState() => _SurfaceState();
}

class _SurfaceState extends State<AnsightPencilSurface> {
  final marker = GlobalKey();
  final realPointers = <int>{};
  @override
  void initState() { super.initState(); _bind(); }
  void _bind() {
    if (widget.id.trim().isEmpty || _surfaces.containsKey(widget.id)) throw StateError('Pencil surface IDs must be unique and nonempty.');
    _surfaces[widget.id] = this;
  }
  @override
  void didUpdateWidget(AnsightPencilSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.id != widget.id) { _surfaces.remove(oldWidget.id); _cancelOwnRun(); _bind(); }
  }
  void _cancelOwnRun() {
    if (_active?.surface == this) unawaited(_finish().catchError((Object error) { debugPrint('Ansight Pencil cleanup failed: $error'); }));
  }
  @override
  void dispose() { _surfaces.remove(widget.id); _cancelOwnRun(); super.dispose(); }
  RenderBox get box {
    final object = marker.currentContext?.findRenderObject();
    if (!mounted || object is! RenderBox || !object.attached || !object.hasSize) throw StateError('Pencil surface is not laid out.');
    return object;
  }
  Map<String, double> geometry() {
    final b = box, view = View.of(context);
    final origin = b.localToGlobal(Offset.zero), end = b.localToGlobal(Offset(b.size.width, b.size.height));
    // Rotated/scaled wrappers need an explicit coordinate adapter; do not silently mis-map them.
    if ((end.dx - origin.dx - b.size.width).abs() > .01 || (end.dy - origin.dy - b.size.height).abs() > .01) throw StateError('Pencil surface must be untransformed.');
    return {'x': origin.dx, 'y': origin.dy, 'width': b.size.width, 'height': b.size.height,
      'viewportWidth': view.physicalSize.width / view.devicePixelRatio,
      'viewportHeight': view.physicalSize.height / view.devicePixelRatio, 'scale': view.devicePixelRatio};
  }
  bool hitTest(Offset point) {
    final result = HitTestResult();
    RendererBinding.instance.hitTestInView(result, point, View.of(context).viewId);
    return result.path.any((entry) => identical(entry.target, box));
  }
  @override
  Widget build(BuildContext context) => Listener(
    key: marker, behavior: HitTestBehavior.translucent,
    onPointerDown: (event) { if (event.ansightPencil == null) realPointers.add(event.pointer); },
    onPointerUp: (event) { realPointers.remove(event.pointer); },
    onPointerCancel: (event) { realPointers.remove(event.pointer); }, child: widget.child);
}

class _Run {
  _Run(this.id, this.surface, this.samples, this.rect) : pointer = _nextPointer--, viewId = View.of(surface.context).viewId;
  final String id;
  final _SurfaceState surface;
  final List<Map<String, Object?>> samples;
  final Map<String, double> rect;
  final int pointer, viewId;
  final clock = Stopwatch()..start();
  Timer? timer;
  int index = 0;
  Map<String, Object?>? last;
  bool touching = false, hovering = false, added = false;
  Offset previous = Offset.zero;
}

const _phases = ['down', 'move', 'up', 'cancel', 'hoverEnter', 'hoverMove', 'hoverExit'];
const _fields = ['phase', 'x', 'y', 'pressure', 'altitudeRadians', 'azimuthRadians', 'rollRadians', 'distance'];
Map<String, Object?> _sample(Object? value) {
  if (value is! Map || !_phases.contains(value['phase']) || value.keys.any((key) => !_fields.contains(key))) throw ArgumentError('Invalid Pencil sample.');
  final result = <String, Object?>{'pressure': 0.0, 'altitudeRadians': math.pi / 2, 'azimuthRadians': 0.0, 'rollRadians': 0.0, 'distance': 0.0, ...Map<String, Object?>.from(value)};
  for (final field in _fields.skip(1)) {
    final v = result[field];
    if (v is! num || !v.isFinite) throw ArgumentError('Pencil values must be finite.');
    result[field] = v.toDouble();
  }
  for (final field in ['x', 'y', 'pressure', 'distance']) {
    final v = result[field] as double;
    if (v < 0 || v > 1) throw ArgumentError('$field must be between 0 and 1.');
  }
  final altitude = result['altitudeRadians'] as double;
  if (altitude < 0 || altitude > math.pi / 2 || (!['down', 'move'].contains(result['phase']) && result['pressure'] != 0.0)) throw ArgumentError('Invalid Pencil pressure or altitude.');
  return {for (final key in _fields) key: result[key]};
}
List<Map<String, Object?>> _sequence(Object? value) {
  if (value is! List || value.length < 2 || value.length > 2000) throw ArgumentError('Supply 2..2000 samples.');
  final samples = value.map(_sample).toList();
  bool touching = false, hovering = false;
  for (final sample in samples) {
    final p = sample['phase'];
    if ((p == 'down' && touching) || (['move', 'up', 'cancel'].contains(p) && !touching) || (p == 'hoverEnter' && (touching || hovering)) || (['hoverMove', 'hoverExit'].contains(p) && (!hovering || touching))) throw ArgumentError('Invalid Pencil sequence.');
    if (p == 'down') { touching = true; hovering = false; }
    if (p == 'up') { touching = false; hovering = true; }
    if (p == 'hoverEnter') hovering = true;
    if (['cancel', 'hoverExit'].contains(p)) { touching = false; hovering = false; }
  }
  if (touching) throw ArgumentError('End contact with up or cancel.');
  return samples;
}
Offset _point(Map<String, Object?> sample, Map<String, double> rect) => Offset((sample['x'] as double) * rect['viewportWidth']!, (sample['y'] as double) * rect['viewportHeight']!);
bool _inside(Map<String, Object?> sample, Map<String, double> rect) => Rect.fromLTWH(rect['x']!, rect['y']!, rect['width']!, rect['height']!).contains(_point(sample, rect));
Future<void> _capture(_Run run, Map<String, Object?> sample) async {
  await _channel.invokeMethod('capture', jsonEncode({'sample': sample, 'geometry': run.rect, 'pointerId': run.pointer}));
}
void _dispatch(_Run run, Map<String, Object?> sample) {
  final point = _point(sample, run.rect), phase = sample['phase'];
  final PointerEvent base;
  switch (phase) {
    case 'down': base = const PointerDownEvent(); break;
    case 'move': base = const PointerMoveEvent(); break;
    case 'up': base = const PointerUpEvent(); break;
    case 'cancel': base = const PointerCancelEvent(); break;
    case 'hoverExit': base = const PointerRemovedEvent(); break;
    default: base = const PointerHoverEvent();
  }
  if (!run.added && phase != 'hoverExit') {
    GestureBinding.instance.handlePointerEvent(PointerAddedEvent(viewId: run.viewId, device: run.pointer, kind: PointerDeviceKind.stylus, position: point));
    run.added = true;
  }
  final event = base.copyWith(viewId: run.viewId, pointer: run.pointer, device: run.pointer, kind: PointerDeviceKind.stylus,
    timeStamp: run.clock.elapsed, position: point, delta: phase == 'move' || phase == 'hoverMove' ? point - run.previous : Offset.zero,
    pressure: sample['pressure'] as double, pressureMin: 0, pressureMax: 1,
    distance: sample['distance'] as double, distanceMax: 1,
    orientation: (sample['azimuthRadians'] as double) - math.pi / 2,
    tilt: math.pi / 2 - (sample['altitudeRadians'] as double));
  _metadata[event] = Map.unmodifiable({...sample, 'inputSource': 'simulator-sdk-framework'});
  GestureBinding.instance.handlePointerEvent(event);
  run.previous = point;
  if (phase == 'hoverExit') run.added = false;
}
Future<void> _finish() async {
  final run = _active;
  if (run == null) return;
  _active = null; run.timer?.cancel();
  try {
    if (run.last != null && (run.touching || run.hovering)) {
      final terminal = {...run.last!, 'phase': run.touching ? 'cancel' : 'hoverExit', 'pressure': 0.0};
      _dispatch(run, terminal); await _capture(run, terminal);
    }
  } finally {
    if (run.added) GestureBinding.instance.handlePointerEvent(PointerRemovedEvent(viewId: run.viewId, device: run.pointer, kind: PointerDeviceKind.stylus, position: run.previous));
  }
}

Future<AnsightToolResult> handlePencilSurfaceCommand(Map<String, String> args, AnsightToolContext context) async {
  if (_handling) return const AnsightToolResult.failure(message: 'Another Pencil command is executing.', errorCode: 'pen_busy');
  _handling = true;
  try {
    final command = jsonDecode(args['command'] ?? '') as Map<String, dynamic>;
    final id = command['runId'];
    if (id is! String || id.trim().isEmpty) throw ArgumentError('A run ID is required.');
    if (command['action'] == 'prepare') {
      if (_active != null) throw StateError('A Pencil run is already active.');
      final surface = _surfaces[command['target']];
      if (surface == null || surface.realPointers.isNotEmpty) throw StateError('Pencil surface is absent or has active real input.');
      final samples = _sequence(command['samples']);
      await _channel.invokeMethod('requirePlayback');
      if (_surfaces[command['target']] != surface || surface.realPointers.isNotEmpty) throw StateError('Surface detached or received real input during preparation.');
      final rect = surface.geometry();
      for (final sample in samples) {
        if (!_inside(sample, rect) || !surface.hitTest(_point(sample, rect))) throw StateError('A sample does not hit the selected surface.');
      }
      final run = _Run(id, surface, samples, rect);
      _active = run;
      run.timer = Timer(const Duration(seconds: 35), () { unawaited(_finish().catchError((Object error) { debugPrint('Ansight Pencil cleanup failed: $error'); })); });
      return const AnsightToolResult.success(result: {'protocolVersion': 1, 'delivery': 'framework'});
    }
    final run = _active;
    if (run == null && command['action'] == 'finish') return const AnsightToolResult.success();
    if (run == null || run.id != id) throw StateError('No matching Pencil run.');
    if (command['action'] == 'finish') { await _finish(); return const AnsightToolResult.success(); }
    if (command['action'] != 'sample') throw ArgumentError('Unknown Pencil action.');
    try {
      final sample = _sample(command['sample']);
      if (run.index >= run.samples.length || jsonEncode(sample) != jsonEncode(run.samples[run.index])) throw StateError('Sample differs from prepared sequence.');
      if (run.surface.realPointers.isNotEmpty || jsonEncode(run.surface.geometry()) != jsonEncode(run.rect) || !run.surface.hitTest(_point(sample, run.rect))) throw StateError('Surface moved, detached, became covered or received real input.');
      await _channel.invokeMethod('requirePlayback');
      if (_active != run || _surfaces[run.surface.widget.id] != run.surface || run.surface.realPointers.isNotEmpty || jsonEncode(run.surface.geometry()) != jsonEncode(run.rect) || !run.surface.hitTest(_point(sample, run.rect))) throw StateError('Pencil run ended or the surface changed during validation.');
      run.last = sample; run.touching = ['down', 'move'].contains(sample['phase']);
      run.hovering = ['up', 'hoverEnter', 'hoverMove'].contains(sample['phase']);
      _dispatch(run, sample); await _capture(run, sample);
      if (_active != run) throw StateError('Pencil run ended during delivery.');
      run.index++;
      return AnsightToolResult.success(result: {'delivered': true, 'sampleIndex': run.index - 1});
    } catch (_) { await _finish(); rethrow; }
  } catch (error) { return AnsightToolResult.failure(message: error.toString(), errorCode: 'pen_framework_failed'); }
  finally { _handling = false; }
}

Future<void> initializePencilSurfaces() async {
  _registration ??= await Ansight.instance.registerTool(const AnsightToolDefinition(
    id: 'ui.simulate_pen_framework', name: 'Play Pencil input on a registered Flutter surface', category: 'ui', policy: AnsightToolPolicy.write,
    argumentsSchema: {'type': 'object', 'required': ['command'], 'additionalProperties': false, 'properties': {'command': {'type': 'string'}}}), handlePencilSurfaceCommand);
}
