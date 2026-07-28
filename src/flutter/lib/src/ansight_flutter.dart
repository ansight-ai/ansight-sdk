import 'dart:async';
import 'dart:ui';

import 'package:flutter/foundation.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/widgets.dart';

import 'ansight_models.dart';
import 'ansight_runtime.dart';
import 'ansight_tooling.dart';

/// Installs Flutter-specific lifecycle, navigation, frame, error, and widget
/// inspection support on top of the native Ansight runtime.
class AnsightFlutterInstrumentation with WidgetsBindingObserver {
  AnsightFlutterInstrumentation._();

  static final AnsightFlutterInstrumentation instance =
      AnsightFlutterInstrumentation._();

  final List<String> _navigationStack = <String>[];
  final Map<String, Element> _elements = <String, Element>{};
  final Expando<String> _elementIds = Expando<String>('ansightWidgetId');

  bool _installed = false;
  bool _captureFrames = true;
  bool _captureErrors = true;
  int _nextElementId = 1;
  FlutterExceptionHandler? _previousFlutterErrorHandler;
  bool Function(Object, StackTrace)? _previousPlatformErrorHandler;

  bool get isInstalled => _installed;

  List<String> get navigationStack =>
      List<String>.unmodifiable(_navigationStack);

  /// Installs all Flutter-specific instrumentation. Repeated calls are safe.
  Future<void> install({
    bool captureFrames = true,
    bool captureErrors = true,
    bool registerWidgetTools = true,
  }) async {
    _captureFrames = captureFrames;
    _captureErrors = captureErrors;
    if (!_installed) {
      WidgetsBinding.instance.addObserver(this);
      WidgetsBinding.instance.addTimingsCallback(_onFrameTimings);
      _installErrorHooks();
      _installed = true;
    }

    await _registerFlutterChannels();
    if (registerWidgetTools) {
      await _registerWidgetTools();
      await Ansight.instance.enableFlutterVisualTreeProvider();
    }
    await _recordCurrentLifecycle();
  }

  /// Removes local observers and restores error handlers where possible.
  void uninstall() {
    if (!_installed) {
      return;
    }
    WidgetsBinding.instance.removeObserver(this);
    WidgetsBinding.instance.removeTimingsCallback(_onFrameTimings);
    if (FlutterError.onError == _handleFlutterError) {
      FlutterError.onError = _previousFlutterErrorHandler;
    }
    _restorePlatformErrorHandler();
    Ansight.instance
      ..removeLocalToolHandler(_visualTreeHandlerId)
      ..removeLocalToolHandler(_inspectNodeHandlerId);
    _elements.clear();
    _installed = false;
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final mapped = state == AppLifecycleState.resumed
        ? AnsightLifecycleState.foreground
        : AnsightLifecycleState.background;
    _ignore(Ansight.instance.setAppLifecycleState(mapped));
  }

  void recordRoutePush(Route<dynamic> route) {
    final name = _routeName(route);
    _navigationStack.add(name);
    if (!_installed) {
      return;
    }
    _ignore(
      Ansight.instance.screenViewed(
        name,
        details: <String, String>{
          'operation': 'push',
          'depth': _navigationStack.length.toString(),
          'framework': 'flutter',
        },
      ),
    );
  }

  void recordRouteReplace(Route<dynamic>? oldRoute, Route<dynamic>? newRoute) {
    if (_navigationStack.isNotEmpty) {
      _navigationStack.removeLast();
    }
    if (newRoute != null) {
      final name = _routeName(newRoute);
      _navigationStack.add(name);
      if (!_installed) {
        return;
      }
      _ignore(
        Ansight.instance.screenViewed(
          name,
          details: <String, String>{
            'operation': 'replace',
            'depth': _navigationStack.length.toString(),
            'framework': 'flutter',
          },
        ),
      );
    }
  }

  void recordRoutePop(Route<dynamic> route, Route<dynamic>? previousRoute) {
    if (_navigationStack.isNotEmpty) {
      _navigationStack.removeLast();
    }
    if (previousRoute != null) {
      final name = _routeName(previousRoute);
      if (!_installed) {
        return;
      }
      _ignore(
        Ansight.instance.screenViewed(
          name,
          details: <String, String>{
            'operation': 'pop',
            'depth': _navigationStack.length.toString(),
            'framework': 'flutter',
          },
        ),
      );
    }
  }

  Future<void> _recordCurrentLifecycle() async {
    final state = WidgetsBinding.instance.lifecycleState;
    if (state != null) {
      didChangeAppLifecycleState(state);
    }
  }

  Future<void> _registerFlutterChannels() async {
    const channels = <AnsightChannel>[
      AnsightChannel(
        id: 40,
        name: 'Flutter frame build',
        unit: 'ms',
        type: 'timing',
        source: 'flutter',
        group: 'rendering',
        kind: 'frame_build',
      ),
      AnsightChannel(
        id: 41,
        name: 'Flutter frame raster',
        unit: 'ms',
        type: 'timing',
        source: 'flutter',
        group: 'rendering',
        kind: 'frame_raster',
      ),
      AnsightChannel(
        id: 42,
        name: 'Flutter frame total',
        unit: 'ms',
        type: 'timing',
        source: 'flutter',
        group: 'rendering',
        kind: 'frame_total',
      ),
      AnsightChannel(
        id: 43,
        name: 'Flutter frame count',
        unit: 'count',
        type: 'counter',
        source: 'flutter',
        group: 'rendering',
        kind: 'frame_count',
      ),
    ];
    for (final channel in channels) {
      await Ansight.instance.registerMetricChannel(channel);
    }
  }

  Future<void> _registerWidgetTools() async {
    Ansight.instance
      ..registerLocalToolHandler(_visualTreeHandlerId, _getWidgetTree)
      ..registerLocalToolHandler(_inspectNodeHandlerId, _inspectWidget);

    await _registerOrReplaceTool(
      const AnsightToolDefinition(
        id: 'flutter.get_widget_tree',
        name: 'Get Flutter Widget Tree',
        description:
            'Returns the mounted Flutter element and render-object hierarchy.',
        category: 'flutter',
        keywords: <String>['flutter', 'widget', 'element', 'tree', 'layout'],
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.medium,
          summary: 'Reads the current Flutter widget hierarchy.',
          implications: <String>['ui_metadata_disclosure'],
        ),
      ),
      _getWidgetTree,
    );
    await _registerOrReplaceTool(
      const AnsightToolDefinition(
        id: 'flutter.inspect_widget',
        name: 'Inspect Flutter Widget',
        description: 'Returns diagnostics for one widget-tree node.',
        category: 'flutter',
        keywords: <String>['flutter', 'widget', 'inspect', 'properties'],
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.medium,
          summary: 'Reads diagnostic properties for a mounted widget.',
          implications: <String>['ui_metadata_disclosure'],
        ),
      ),
      _inspectWidget,
    );
    await _registerOrReplaceTool(
      const AnsightToolDefinition(
        id: 'flutter.find_widgets',
        name: 'Find Flutter Widgets',
        description: 'Finds mounted widgets by type, key, or text.',
        category: 'flutter',
        keywords: <String>['flutter', 'widget', 'find', 'search', 'text'],
        security: AnsightToolSecurity(
          level: AnsightToolSecurityLevel.medium,
          summary: 'Searches the current Flutter widget hierarchy.',
          implications: <String>['ui_metadata_disclosure'],
        ),
      ),
      _findWidgets,
    );
    await _registerOrReplaceTool(
      const AnsightToolDefinition(
        id: 'flutter.get_navigation_state',
        name: 'Get Flutter Navigation State',
        description: 'Returns routes observed by AnsightNavigatorObserver.',
        category: 'flutter',
        keywords: <String>['flutter', 'navigation', 'route', 'stack'],
      ),
      (Map<String, String> _, AnsightToolContext __) async =>
          AnsightToolResult.success(
        message: 'Flutter navigation state captured.',
        result: <String, Object?>{
          'routes': navigationStack,
          'depth': _navigationStack.length,
          'currentRoute':
              _navigationStack.isEmpty ? null : _navigationStack.last,
        },
      ),
    );
  }

  Future<void> _registerOrReplaceTool(
    AnsightToolDefinition definition,
    AnsightToolHandler handler,
  ) async {
    if (Ansight.instance.registeredToolIds.contains(definition.id)) {
      await Ansight.instance.unregisterTool(definition.id);
    }
    await Ansight.instance.registerTool(definition, handler);
  }

  void _onFrameTimings(List<FrameTiming> timings) {
    if (!_captureFrames) {
      return;
    }
    for (final timing in timings) {
      final buildMs = timing.buildDuration.inMicroseconds / 1000;
      final rasterMs = timing.rasterDuration.inMicroseconds / 1000;
      final totalMs = timing.totalSpan.inMicroseconds / 1000;
      _ignore(Ansight.instance.metric(buildMs, channel: 40));
      _ignore(Ansight.instance.metric(rasterMs, channel: 41));
      _ignore(Ansight.instance.metric(totalMs, channel: 42));
      _ignore(Ansight.instance.metric(1, channel: 43));
      if (totalMs >= 32) {
        _ignore(
          Ansight.instance.event(
            'Flutter slow frame',
            type: AnsightEventType.warning,
            details: 'build=${buildMs.toStringAsFixed(2)}ms, '
                'raster=${rasterMs.toStringAsFixed(2)}ms, '
                'total=${totalMs.toStringAsFixed(2)}ms',
            channel: 42,
          ),
        );
      }
    }
  }

  void _installErrorHooks() {
    _previousFlutterErrorHandler = FlutterError.onError;
    FlutterError.onError = _handleFlutterError;
    // PlatformDispatcher.onError was added after the oldest supported Flutter
    // release. Dynamic access preserves asynchronous error capture on newer
    // engines without making Flutter 3.0 fail at compile time.
    try {
      final dynamic dispatcher = PlatformDispatcher.instance;
      final dynamic previous = dispatcher.onError;
      if (previous is bool Function(Object, StackTrace)) {
        _previousPlatformErrorHandler = previous;
      }
      dispatcher.onError = _handlePlatformError;
    } on NoSuchMethodError {
      _previousPlatformErrorHandler = null;
    }
  }

  void _restorePlatformErrorHandler() {
    try {
      final dynamic dispatcher = PlatformDispatcher.instance;
      if (dispatcher.onError == _handlePlatformError) {
        dispatcher.onError = _previousPlatformErrorHandler;
      }
    } on NoSuchMethodError {
      // Flutter 3.0 does not expose PlatformDispatcher.onError.
    }
  }

  void _handleFlutterError(FlutterErrorDetails details) {
    if (_captureErrors) {
      _ignore(
        Ansight.instance.event(
          details.exceptionAsString(),
          type: AnsightEventType.exception,
          details: details.stack?.toString(),
        ),
      );
    }
    final previous = _previousFlutterErrorHandler;
    if (previous != null && previous != _handleFlutterError) {
      previous(details);
    } else {
      FlutterError.presentError(details);
    }
  }

  bool _handlePlatformError(Object error, StackTrace stack) {
    if (_captureErrors) {
      _ignore(
        Ansight.instance.event(
          error.toString(),
          type: AnsightEventType.exception,
          details: stack.toString(),
        ),
      );
    }
    final previous = _previousPlatformErrorHandler;
    return previous != null && previous != _handlePlatformError
        ? previous(error, stack)
        : false;
  }

  Future<AnsightToolResult> _getWidgetTree(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final maxDepth =
        int.tryParse(arguments['maxDepth'] ?? '')?.clamp(1, 100).toInt() ?? 40;
    final maxNodes =
        int.tryParse(arguments['maxNodes'] ?? '')?.clamp(1, 10000).toInt() ??
            2000;
    final roots = <Object?>[];
    final nodes = <Object?>[];
    _elements.clear();

    void capture(Element element, int depth, String? parentId) {
      if (depth > maxDepth || nodes.length >= maxNodes) {
        return;
      }
      final node = _describeElement(element, parentId: parentId, depth: depth);
      nodes.add(node);
      if (parentId == null) {
        roots.add(node['id']);
      }
      final id = node['id']! as String;
      element.visitChildren((Element child) => capture(child, depth + 1, id));
    }

    // renderViewElement is the pre-Flutter-3.35 name for rootElement and
    // remains as a deprecated compatibility alias in current Flutter.
    // ignore: deprecated_member_use
    final root = WidgetsBinding.instance.renderViewElement;
    if (root != null) {
      capture(root, 0, null);
    }
    return AnsightToolResult.success(
      message: 'Flutter widget tree captured.',
      result: <String, Object?>{
        'source': 'flutter',
        'displayName': 'Flutter',
        'capturedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'rootIds': roots,
        'nodes': nodes,
        'nodeCount': nodes.length,
        'truncated': nodes.length >= maxNodes,
      },
    );
  }

  Future<AnsightToolResult> _inspectWidget(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final id = arguments['id'] ?? arguments['nodeId'];
    if (id == null || id.isEmpty) {
      return const AnsightToolResult.failure(
        message: 'Widget node id is required.',
        errorCode: 'node_id_required',
      );
    }
    var element = _elements[id];
    if (element == null || element.owner == null) {
      await _getWidgetTree(const <String, String>{}, context);
      element = _elements[id];
    }
    if (element == null || element.owner == null) {
      return AnsightToolResult.failure(
        message: "Flutter widget node '$id' was not found.",
        errorCode: 'node_not_found',
      );
    }
    final propertyBuilder = DiagnosticPropertiesBuilder();
    element.widget.debugFillProperties(propertyBuilder);
    final properties = propertyBuilder.properties
        .map(
          (DiagnosticsNode property) => <String, Object?>{
            'name': property.name,
            'description': property.toDescription(),
            'level': property.level.name,
          },
        )
        .toList(growable: false);
    final diagnostics = element.toDiagnosticsNode().toStringDeep(
          minLevel: DiagnosticLevel.debug,
        );
    return AnsightToolResult.success(
      message: 'Flutter widget inspected.',
      result: <String, Object?>{
        ..._describeElement(element, depth: 0),
        'diagnostics': diagnostics,
        'properties': properties,
      },
    );
  }

  Future<AnsightToolResult> _findWidgets(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final query =
        (arguments['query'] ?? arguments['text'] ?? arguments['type'] ?? '')
            .trim()
            .toLowerCase();
    if (query.isEmpty) {
      return const AnsightToolResult.failure(
        message: 'A widget search query is required.',
        errorCode: 'query_required',
      );
    }
    final tree = await _getWidgetTree(
      <String, String>{
        if (arguments['maxDepth'] != null) 'maxDepth': arguments['maxDepth']!,
        if (arguments['maxNodes'] != null) 'maxNodes': arguments['maxNodes']!,
      },
      context,
    );
    final payload = tree.result! as AnsightJson;
    final nodes = (payload['nodes']! as List<Object?>)
        .whereType<AnsightJson>()
        .where((AnsightJson node) => node.values.any(
              (Object? value) => value.toString().toLowerCase().contains(query),
            ))
        .take(100)
        .toList(growable: false);
    return AnsightToolResult.success(
      message: 'Flutter widget search completed.',
      result: <String, Object?>{
        'query': query,
        'matches': nodes,
        'matchCount': nodes.length,
      },
    );
  }

  AnsightJson _describeElement(
    Element element, {
    String? parentId,
    required int depth,
  }) {
    final id = _elementIds[element] ?? 'flutter-${_nextElementId++}';
    _elementIds[element] = id;
    _elements[id] = element;
    final renderObject = element.renderObject;
    final bounds = renderObject is RenderBox && renderObject.hasSize
        ? _globalBounds(renderObject)
        : null;
    final widget = element.widget;
    return <String, Object?>{
      'id': id,
      if (parentId != null) 'parentId': parentId,
      'depth': depth,
      'type': widget.runtimeType.toString(),
      'widget': widget.toStringShort(),
      if (widget.key != null) 'key': widget.key.toString(),
      'mounted': element.owner != null,
      'dirty': element.dirty,
      if (renderObject != null)
        'renderObjectType': renderObject.runtimeType.toString(),
      if (bounds != null)
        'bounds': <String, Object?>{
          'x': bounds.left,
          'y': bounds.top,
          'width': bounds.width,
          'height': bounds.height,
        },
      'children': _childIds(element),
    };
  }

  List<String> _childIds(Element element) {
    final ids = <String>[];
    element.visitChildren((Element child) {
      final id = _elementIds[child] ?? 'flutter-${_nextElementId++}';
      _elementIds[child] = id;
      ids.add(id);
    });
    return ids;
  }

  Rect? _globalBounds(RenderBox box) {
    try {
      final offset = box.localToGlobal(Offset.zero);
      return offset & box.size;
    } catch (_) {
      return null;
    }
  }

  void _ignore(Future<Object?> operation) {
    unawaited(_guardOperation(operation));
  }

  Future<void> _guardOperation(Future<Object?> operation) async {
    try {
      await operation;
    } catch (error) {
      debugPrint('Ansight Flutter instrumentation skipped an event: $error');
    }
  }

  static String _routeName(Route<dynamic> route) =>
      route.settings.name ??
      route.settings.arguments?.runtimeType.toString() ??
      route.runtimeType.toString();

  static const String _visualTreeHandlerId = '__ansight_flutter.visual_tree';
  static const String _inspectNodeHandlerId = '__ansight_flutter.inspect_node';
}

/// A navigator observer that records route changes and screen views.
class AnsightNavigatorObserver extends NavigatorObserver {
  AnsightFlutterInstrumentation get _instrumentation =>
      AnsightFlutterInstrumentation.instance;

  @override
  void didPush(Route<dynamic> route, Route<dynamic>? previousRoute) {
    _instrumentation.recordRoutePush(route);
    super.didPush(route, previousRoute);
  }

  @override
  void didReplace({Route<dynamic>? newRoute, Route<dynamic>? oldRoute}) {
    _instrumentation.recordRouteReplace(oldRoute, newRoute);
    super.didReplace(newRoute: newRoute, oldRoute: oldRoute);
  }

  @override
  void didPop(Route<dynamic> route, Route<dynamic>? previousRoute) {
    _instrumentation.recordRoutePop(route, previousRoute);
    super.didPop(route, previousRoute);
  }
}
