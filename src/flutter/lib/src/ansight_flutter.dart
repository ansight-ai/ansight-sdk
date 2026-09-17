import 'dart:async';
import 'dart:ui';

import 'package:flutter/foundation.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/widgets.dart';

import 'ansight_models.dart';
import 'ansight_options.dart';
import 'ansight_runtime.dart';
import 'ansight_tooling.dart';

/// Owns a repaint boundary that can submit Flutter-rendered session frames.
class AnsightFlutterCaptureController {
  final GlobalKey _boundaryKey = GlobalKey(
    debugLabel: 'ansight-flutter-capture-boundary',
  );

  /// Captures the wrapped Flutter UI and its widget tree into the live session.
  Future<AnsightOperationResult> capture({
    int quality = 60,
    int maxWidth = 960,
    bool includeVisualTree = true,
  }) async {
    if (defaultTargetPlatform != TargetPlatform.macOS) {
      return Ansight.instance.captureScreenFrame(
        options: AnsightSessionJpegCaptureOptions(
          quality: quality,
          maxWidth: maxWidth,
          mode: includeVisualTree
              ? AnsightSessionJpegCaptureMode.screenshotAndVisualTree
              : AnsightSessionJpegCaptureMode.screenshotOnly,
        ),
      );
    }

    var boundary = _boundaryKey.currentContext?.findRenderObject();
    if (boundary is RenderRepaintBoundary && boundary.debugNeedsPaint) {
      WidgetsBinding.instance.scheduleFrame();
      try {
        await WidgetsBinding.instance.endOfFrame.timeout(
          const Duration(seconds: 1),
        );
      } on TimeoutException {
        return const AnsightOperationResult(
          success: false,
          message: 'Flutter capture boundary is awaiting its next frame.',
        );
      }
      boundary = _boundaryKey.currentContext?.findRenderObject();
    }
    if (boundary is! RenderRepaintBoundary || boundary.debugNeedsPaint) {
      return const AnsightOperationResult(
        success: false,
        message: 'Flutter capture boundary is not ready.',
      );
    }

    final logicalWidth = boundary.size.width;
    if (logicalWidth <= 0 || boundary.size.height <= 0) {
      return const AnsightOperationResult(
        success: false,
        message: 'Flutter capture boundary has no visible size.',
      );
    }
    final devicePixelRatio =
        View.of(_boundaryKey.currentContext!).devicePixelRatio;
    final boundedPixelRatio = maxWidth > 0
        ? (maxWidth / logicalWidth).clamp(0.1, devicePixelRatio).toDouble()
        : devicePixelRatio;
    final image = await boundary.toImage(pixelRatio: boundedPixelRatio);
    try {
      final bytes = await image.toByteData(format: ImageByteFormat.png);
      if (bytes == null) {
        return const AnsightOperationResult(
          success: false,
          message: 'Flutter frame could not be encoded as PNG.',
        );
      }

      final visualTrees = <AnsightJson>[];
      if (includeVisualTree) {
        final tree = await AnsightFlutterInstrumentation.instance
            ._captureWidgetTreeForSession(
          rootElement: _boundaryKey.currentContext is Element
              ? _boundaryKey.currentContext! as Element
              : null,
        );
        if (tree != null) {
          visualTrees.add(tree);
        }
      }
      return Ansight.instance.submitFlutterScreenFrame(
        pngBytes: bytes.buffer.asUint8List(
          bytes.offsetInBytes,
          bytes.lengthInBytes,
        ),
        width: image.width,
        height: image.height,
        quality: quality,
        visualTrees: visualTrees,
      );
    } finally {
      image.dispose();
    }
  }

  Future<void> _submitPointer(PointerEvent event, String action) async {
    if (defaultTargetPlatform != TargetPlatform.macOS) {
      return;
    }
    final boundary = _boundaryKey.currentContext?.findRenderObject();
    if (boundary is! RenderBox || !boundary.hasSize) {
      return;
    }
    final result = await Ansight.instance.submitFlutterPointerEvent(
      action: action,
      pointerId: event.pointer,
      x: event.localPosition.dx,
      y: event.localPosition.dy,
      surfaceWidth: boundary.size.width,
      surfaceHeight: boundary.size.height,
      surfaceScale: View.of(_boundaryKey.currentContext!).devicePixelRatio,
    );
    if (!result.success) {
      debugPrint('Ansight Flutter touch capture skipped an event: '
          '${result.message}');
    }
  }
}

/// Marks Flutter content that can be captured into an Ansight desktop session.
class AnsightFlutterCaptureBoundary extends StatefulWidget {
  const AnsightFlutterCaptureBoundary({
    super.key,
    required this.controller,
    required this.child,
    this.automaticCaptureOptions = const AnsightSessionJpegCaptureOptions(),
  });

  final AnsightFlutterCaptureController controller;
  final Widget child;

  /// Periodically captures the Flutter compositor on macOS.
  ///
  /// Pass null to keep this boundary manual-only. Other platforms continue to
  /// use their native session capture implementation.
  final AnsightSessionJpegCaptureOptions? automaticCaptureOptions;

  @override
  State<AnsightFlutterCaptureBoundary> createState() =>
      _AnsightFlutterCaptureBoundaryState();
}

class _AnsightFlutterCaptureBoundaryState
    extends State<AnsightFlutterCaptureBoundary> {
  Timer? automaticCaptureTimer;
  bool automaticCaptureInProgress = false;

  @override
  void initState() {
    super.initState();
    _scheduleAutomaticCapture(const Duration(milliseconds: 750));
  }

  @override
  void didUpdateWidget(AnsightFlutterCaptureBoundary oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.automaticCaptureOptions != widget.automaticCaptureOptions ||
        oldWidget.controller != widget.controller) {
      automaticCaptureTimer?.cancel();
      _scheduleAutomaticCapture(const Duration(milliseconds: 250));
    }
  }

  @override
  void dispose() {
    automaticCaptureTimer?.cancel();
    super.dispose();
  }

  void _scheduleAutomaticCapture(Duration delay) {
    if (defaultTargetPlatform != TargetPlatform.macOS ||
        widget.automaticCaptureOptions == null ||
        !mounted) {
      return;
    }
    automaticCaptureTimer?.cancel();
    automaticCaptureTimer = Timer(delay, () {
      unawaited(_captureAutomatically());
    });
  }

  Future<void> _captureAutomatically() async {
    final options = widget.automaticCaptureOptions;
    if (options == null || automaticCaptureInProgress || !mounted) {
      return;
    }

    automaticCaptureInProgress = true;
    try {
      await widget.controller.capture(
        quality: options.quality,
        maxWidth: options.maxWidth ?? 0,
        includeVisualTree: options.mode ==
            AnsightSessionJpegCaptureMode.screenshotAndVisualTree,
      );
    } on Object catch (error) {
      debugPrint('Ansight Flutter automatic capture skipped a frame: $error');
    } finally {
      automaticCaptureInProgress = false;
      if (mounted) {
        _scheduleAutomaticCapture(
          Duration(
            milliseconds: options.intervalMilliseconds.clamp(250, 60000),
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final boundary = RepaintBoundary(
      key: widget.controller._boundaryKey,
      child: widget.child,
    );
    if (defaultTargetPlatform != TargetPlatform.macOS) {
      return boundary;
    }
    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: (PointerDownEvent event) =>
          unawaited(widget.controller._submitPointer(event, 'down')),
      onPointerMove: (PointerMoveEvent event) =>
          unawaited(widget.controller._submitPointer(event, 'move')),
      onPointerUp: (PointerUpEvent event) =>
          unawaited(widget.controller._submitPointer(event, 'up')),
      onPointerCancel: (PointerCancelEvent event) =>
          unawaited(widget.controller._submitPointer(event, 'cancel')),
      child: boundary,
    );
  }
}

/// Installs Flutter-specific lifecycle, navigation, frame, error, and widget
/// inspection support on top of the native Ansight runtime.
class AnsightFlutterInstrumentation with WidgetsBindingObserver {
  AnsightFlutterInstrumentation._();

  static final AnsightFlutterInstrumentation instance =
      AnsightFlutterInstrumentation._();

  final List<String> _navigationStack = <String>[];
  final Map<String, Element> _elements = <String, Element>{};
  final Expando<String> _elementIds = Expando<String>('ansightWidgetId');
  final Stopwatch _desktopFrameClock = Stopwatch();

  bool _installed = false;
  bool _captureFrames = true;
  bool _captureErrors = true;
  int _nextElementId = 1;
  int _desktopFrameCount = 0;
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
      _desktopFrameClock
        ..reset()
        ..start();
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
      ..removeLocalToolHandler(_inspectNodeHandlerId)
      ..removeLocalToolHandler(_performActionHandlerId);
    _elements.clear();
    _desktopFrameClock
      ..stop()
      ..reset();
    _desktopFrameCount = 0;
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
      ..registerLocalToolHandler(_inspectNodeHandlerId, _inspectWidget)
      ..registerLocalToolHandler(
        _performActionHandlerId,
        _performWidgetAction,
      );

    await _registerOrReplaceTool(
      const AnsightToolDefinition(
        id: 'flutter.get_widget_tree',
        name: 'Get Flutter Widget Tree',
        description:
            'Returns the mounted Flutter element and render-object hierarchy.',
        category: 'flutter',
        keywords: <String>['flutter', 'widget', 'element', 'tree', 'layout'],
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
    _recordDesktopFramesPerSecond(timings.length);
  }

  void _recordDesktopFramesPerSecond(int frameCount) {
    if (defaultTargetPlatform != TargetPlatform.macOS || frameCount <= 0) {
      return;
    }
    _desktopFrameCount += frameCount;
    final elapsedMicroseconds = _desktopFrameClock.elapsedMicroseconds;
    if (elapsedMicroseconds < 500000) {
      return;
    }
    final framesPerSecond = (_desktopFrameCount *
            Duration.microsecondsPerSecond /
            elapsedMicroseconds)
        .round();
    _desktopFrameCount = 0;
    _desktopFrameClock.reset();
    _ignore(Ansight.instance.metric(framesPerSecond, channel: 3));
  }

  Future<AnsightJson?> _captureWidgetTreeForSession(
      {Element? rootElement}) async {
    final result = await _getWidgetTree(
      const <String, String>{
        'includeBounds': 'true',
        'includeComputedStyles': 'true',
        'maxDepth': '100',
        'maxNodes': '10000',
      },
      const AnsightToolContext(
        requestId: 'flutter.session-capture',
        toolId: 'flutter.get_widget_tree',
        platform: 'flutter',
      ),
      rootElement:
          rootElement == null ? null : _findSessionContentRoot(rootElement),
      compactUnaryNodes: true,
    );
    final value = result.result;
    return value is Map ? Map<String, Object?>.from(value) : null;
  }

  Element _findSessionContentRoot(Element fallback) {
    Element? contentRoot;
    void visit(Element element) {
      if (contentRoot != null) {
        return;
      }
      final type = element.widget.runtimeType.toString();
      if (type == 'Scaffold' || type == 'CupertinoPageScaffold') {
        contentRoot = element;
        return;
      }
      element.visitChildren(visit);
    }

    fallback.visitChildren(visit);
    return contentRoot ?? fallback;
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
        Ansight.instance.recordCrashCandidate(
          kind: 'flutter_framework_error',
          message: details.exceptionAsString(),
          stack: details.stack?.toString(),
          fatal: false,
          metadata: <String, String>{
            'library': details.library ?? '',
            'silent': details.silent.toString(),
          },
        ),
      );
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
        Ansight.instance.recordCrashCandidate(
          kind: 'flutter_platform_error',
          message: error.toString(),
          stack: stack.toString(),
          fatal: false,
        ),
      );
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
    AnsightToolContext context, {
    Element? rootElement,
    bool compactUnaryNodes = false,
  }) async {
    final maxDepth =
        int.tryParse(arguments['maxDepth'] ?? '')?.clamp(1, 100).toInt() ?? 40;
    final maxNodes =
        int.tryParse(arguments['maxNodes'] ?? '')?.clamp(1, 10000).toInt() ??
            2000;
    final types = <String>[];
    final typeIdsByName = <String, int>{};
    var visitedNodeCount = 0;
    var retainedNodeCount = 0;
    var truncated = false;
    _elements.clear();

    int registerType(String typeName) {
      final existingTypeId = typeIdsByName[typeName];
      if (existingTypeId != null) {
        return existingTypeId;
      }

      final typeId = types.length;
      types.add(typeName);
      typeIdsByName[typeName] = typeId;
      return typeId;
    }

    AnsightJson? capture(Element element, int depth) {
      if (compactUnaryNodes &&
          element.widget is Offstage &&
          (element.widget as Offstage).offstage) {
        return null;
      }
      if (depth > maxDepth || visitedNodeCount >= maxNodes) {
        truncated = true;
        return null;
      }

      visitedNodeCount++;
      final node = _describeElement(element, depth: depth);
      final childElements = <Element>[];
      element.visitChildren(childElements.add);
      if (compactUnaryNodes &&
          childElements.length == 1 &&
          node['interactable'] != true &&
          node['key'] == null) {
        return capture(childElements.single, depth);
      }

      final type = node.remove('type')?.toString() ?? 'FlutterWidget';
      node
        ..remove('parentId')
        ..remove('depth')
        ..remove('children')
        ..['typeId'] = registerType(type);

      final children = <Object?>[];
      for (final child in childElements) {
        final capturedChild = capture(child, depth + 1);
        if (capturedChild != null) {
          children.add(capturedChild);
        }
      }
      node
        ..['children'] = children
        ..['childCount'] = children.length;
      retainedNodeCount++;
      return node;
    }

    AnsightJson createSyntheticRoot(List<Object?> children) {
      return <String, Object?>{
        'id': 'flutter.roots',
        'typeId': registerType('FlutterRoots'),
        'role': 'group',
        'supportedActions': const <String>[],
        'interactable': false,
        'visible': true,
        'enabled': true,
        'focusable': false,
        'visual': const <String, Object?>{'opacity': 1.0},
        'children': children,
        'childCount': children.length,
      };
    }

    final capturedRoots = <Object?>[];
    Element? captureRoot = rootElement;
    // Keep the deprecated compatibility lookup on its own line so older
    // Flutter releases can compile the package.
    // ignore: prefer_conditional_assignment
    if (captureRoot == null) {
      // renderViewElement is the pre-Flutter-3.35 name for rootElement and
      // remains as a deprecated compatibility alias in current Flutter.
      // ignore: deprecated_member_use
      captureRoot = WidgetsBinding.instance.renderViewElement;
    }
    if (captureRoot != null) {
      final capturedRoot = capture(captureRoot, 0);
      if (capturedRoot != null) {
        capturedRoots.add(capturedRoot);
      }
    }
    final treeRoot = capturedRoots.length == 1
        ? capturedRoots.single
        : createSyntheticRoot(capturedRoots);
    return AnsightToolResult.success(
      message: 'Flutter widget tree captured.',
      result: <String, Object?>{
        'format': 'ansight.flutter.visual-tree.compact.v2',
        'platform': 'flutter',
        'source': 'flutter',
        'displayName': 'Flutter',
        'capturedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'types': types,
        'root': treeRoot,
        'nodeCount': retainedNodeCount,
        'visitedNodeCount': visitedNodeCount,
        'maxDepth': maxDepth,
        'maxNodes': maxNodes,
        'truncated': truncated,
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

  Future<AnsightToolResult> _performWidgetAction(
    Map<String, String> arguments,
    AnsightToolContext context,
  ) async {
    final id = arguments['nodeId'];
    final action = arguments['action']?.trim().toLowerCase();
    if (id == null || id.isEmpty || action == null || action.isEmpty) {
      return const AnsightToolResult.failure(
        message: 'nodeId and action are required.',
        errorCode: 'ui_action_arguments_invalid',
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

    final widget = element.widget;
    var invoked = false;
    if (action == 'tap' && widget is GestureDetector && widget.onTap != null) {
      widget.onTap!.call();
      invoked = true;
    } else if (action == 'tap' &&
        widget is Semantics &&
        widget.properties.onTap != null) {
      widget.properties.onTap!.call();
      invoked = true;
    } else if (action == 'focus') {
      final focusNode = Focus.maybeOf(element);
      if (focusNode != null) {
        focusNode.requestFocus();
        invoked = true;
      }
    } else if (action == 'unfocus') {
      FocusManager.instance.primaryFocus?.unfocus();
      invoked = true;
    } else if ((action == 'setvalue' || action == 'typetext') &&
        widget is EditableText) {
      widget.controller.text = arguments['value'] ?? '';
      widget.onChanged?.call(widget.controller.text);
      invoked = true;
    }

    if (!invoked) {
      return AnsightToolResult.failure(
        message:
            "Flutter widget '$id' does not support action '${arguments['action']}'.",
        errorCode: 'ui_action_not_supported',
      );
    }
    return AnsightToolResult.success(
      message: 'Flutter widget action invoked.',
      result: <String, Object?>{
        'platform': 'flutter',
        'capturedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'nodeId': id,
        'action': arguments['action'],
        'invoked': true,
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
    final types =
        (payload['types']! as List<Object?>).whereType<String>().toList();
    final root = payload['root']! as AnsightJson;
    final nodes = _walkWidgetTree(root)
        .where((AnsightJson node) {
          final typeId = node['typeId'];
          final typeName = typeId is int && typeId >= 0 && typeId < types.length
              ? types[typeId]
              : '';
          return typeName.toLowerCase().contains(query) ||
              node.entries.where((entry) => entry.key != 'children').any(
                  (entry) =>
                      entry.value.toString().toLowerCase().contains(query));
        })
        .take(100)
        .map((node) => <String, Object?>{
              for (final entry in node.entries)
                if (entry.key != 'children') entry.key: entry.value,
            })
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

  Iterable<AnsightJson> _walkWidgetTree(AnsightJson root) sync* {
    yield root;
    final children = root['children'];
    if (children is! List<Object?>) {
      return;
    }

    for (final child in children.whereType<AnsightJson>()) {
      yield* _walkWidgetTree(child);
    }
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
    final visual = _describeVisual(element, widget, renderObject);
    final automationId = widget.key is ValueKey<String>
        ? (widget.key! as ValueKey<String>).value.trim()
        : null;
    final type = widget.runtimeType.toString();
    final role = _semanticRole(type);
    final supportedActions = _supportedActions(type);
    final visible = renderObject == null ||
        (renderObject.attached &&
            (renderObject is! RenderBox ||
                !renderObject.hasSize ||
                !renderObject.size.isEmpty));
    final enabled = element.owner != null;
    final text = visual['text']?.toString();
    return <String, Object?>{
      'id': id,
      if (parentId != null) 'parentId': parentId,
      'depth': depth,
      'type': type,
      'widget': widget.toStringShort(),
      if (automationId != null && automationId.isNotEmpty)
        'automationId': automationId,
      if (text != null && text.isNotEmpty) 'text': text,
      if (text != null && text.isNotEmpty) 'label': text,
      'role': role,
      'supportedActions': supportedActions,
      'interactable': visible && enabled && supportedActions.isNotEmpty,
      'visible': visible,
      'enabled': enabled,
      'focusable': supportedActions.contains('focus'),
      if (widget.key != null) 'key': widget.key.toString(),
      'mounted': element.owner != null,
      'dirty': element.dirty,
      if (renderObject != null)
        'renderObjectType': renderObject.runtimeType.toString(),
      'visual': visual,
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

  String _semanticRole(String type) {
    final normalized = type.toLowerCase();
    if (normalized.contains('button') ||
        normalized.contains('gesture') ||
        normalized.contains('inkwell')) {
      return 'button';
    }
    if (normalized.contains('editable') || normalized.contains('textfield')) {
      return 'textbox';
    }
    if (normalized.contains('switch')) return 'switch';
    if (normalized.contains('checkbox')) return 'checkbox';
    if (normalized.contains('radio')) return 'radio';
    if (normalized.contains('slider')) return 'slider';
    if (normalized.contains('scroll') ||
        normalized.contains('listview') ||
        normalized.contains('gridview')) {
      return 'scrollview';
    }
    if (normalized == 'text' || normalized.contains('richtext')) return 'text';
    return 'view';
  }

  List<String> _supportedActions(String type) {
    final normalized = type.toLowerCase();
    final actions = <String>[];
    if (normalized.contains('button') ||
        normalized.contains('gesture') ||
        normalized.contains('inkwell')) {
      actions.add('tap');
    }
    if (normalized.contains('editable') || normalized.contains('textfield')) {
      actions.addAll(const <String>['typeText', 'focus']);
    }
    if (normalized.contains('scroll') ||
        normalized.contains('listview') ||
        normalized.contains('gridview')) {
      actions.addAll(const <String>['scroll', 'swipe']);
    }
    return actions;
  }

  AnsightJson _describeVisual(
    Element element,
    Widget widget,
    RenderObject? renderObject,
  ) {
    String? text;
    Object? value;
    Color? foreground;
    Color? background;
    var opacity = 1.0;

    if (widget is Text) {
      text = widget.data ?? widget.textSpan?.toPlainText();
      foreground =
          widget.style?.color ?? DefaultTextStyle.of(element).style.color;
    } else if (widget is RichText) {
      text = widget.text.toPlainText();
      foreground = widget.text.style?.color;
    } else if (widget is EditableText) {
      foreground = widget.style.color;
      if (!widget.obscureText) {
        value = widget.controller.text;
      }
    }

    if (renderObject is RenderParagraph) {
      text ??= renderObject.text.toPlainText();
      foreground ??= renderObject.text.style?.color;
    } else if (renderObject is RenderEditable) {
      foreground ??= renderObject.text?.style?.color;
    }

    if (renderObject is RenderDecoratedBox &&
        renderObject.decoration is BoxDecoration) {
      background = (renderObject.decoration as BoxDecoration).color;
    } else if (widget is ColoredBox) {
      background = widget.color;
    }

    if (renderObject is RenderOpacity) {
      opacity = renderObject.opacity;
    } else if (widget is Opacity) {
      opacity = widget.opacity;
    }

    final normalizedText = _normalizeVisualText(text);
    final normalizedValue =
        value is String ? _normalizeVisualText(value) : null;
    return <String, Object?>{
      if (foreground != null) 'foreground': _colorToArgbHex(foreground),
      if (background != null) 'background': _colorToArgbHex(background),
      'opacity': opacity.clamp(0.0, 1.0),
      if (normalizedText != null) 'text': normalizedText,
      if (normalizedValue != null)
        'value': normalizedValue
      else if (value != null)
        'value': value,
    };
  }

  String _colorToArgbHex(Color color) =>
      // ignore: deprecated_member_use
      '#${color.value.toRadixString(16).padLeft(8, '0').toUpperCase()}';

  String? _normalizeVisualText(String? value) {
    final normalized = value?.trim();
    if (normalized == null || normalized.isEmpty) {
      return null;
    }
    return normalized.length <= 240
        ? normalized
        : '${normalized.substring(0, 240)}...';
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
  static const String _performActionHandlerId =
      '__ansight_flutter.perform_action';
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
