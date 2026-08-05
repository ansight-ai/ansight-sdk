# @ansight/react-native

React Native bridge for the Ansight mobile SDK.

The package is intentionally thin: pairing, transport, telemetry, screen
capture, touch capture, native tool discovery, and native tool execution are
handled by the Ansight iOS and Android SDKs. The JavaScript layer normalizes
React Native inputs, forwards runtime calls to the native bridge, and registers
JavaScript-backed tools for React component-tree inspection.

## Install

### React Native CLI

```sh
npm install @ansight/react-native
```

Rebuild the native app so React Native autolinking installs the iOS pod and
Android library module:

```sh
npx pod-install
npx react-native run-ios
npx react-native run-android
```

### Expo development builds

Ansight contains native iOS and Android code, so it requires an Expo
development build or production/EAS build. It does not run in Expo Go.

```sh
npx expo install @ansight/react-native
```

Add the bundled config plugin to the app config. It supplies the iOS camera
usage description required by QR enrollment and the local-network description
used when connecting to Ansight Studio:

```json
{
  "expo": {
    "plugins": [
      [
        "@ansight/react-native",
        {
          "cameraPermission": "Allow $(PRODUCT_NAME) to scan an Ansight Studio enrollment QR code.",
          "localNetworkPermission": "Allow $(PRODUCT_NAME) to connect to Ansight Studio on your local network."
        }
      ]
    ]
  }
}
```

Omit either message to use the default. Set `cameraPermission` to `false` only
when the app will never call `enrollFromQrCode` or `scanPairingQrCode`. Setting
a permission option to `false` prevents Ansight from adding that key; it does
not remove a value supplied by another plugin or by the app.

Generate and rebuild the native projects after installing or updating the SDK
or changing its plugin options:

```sh
npx expo prebuild --clean
npx expo run:ios
npx expo run:android
```

EAS Build applies the same plugin during prebuild. Expo Updates can update the
JavaScript integration after the native binary has been built with a compatible
version of `@ansight/react-native`.

Expo Web is not currently supported. Do not import this package from a web
bundle; its API requires the native Ansight module.

This package version expects matching native SDK packages:

- CocoaPods: `Ansight`, `AnsightObjC` version `1.2.0-preview.3`
- Maven: `ai.ansight:ansight-android:1.2.0-preview.3`

## Quickstart

```ts
import Ansight from "@ansight/react-native";

await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: __DEV__,
  clientName: "My React Native App",
  toolGuard: __DEV__ ? "readOnly" : "disabled",
  lifecycle: true,
});
```

The native iOS Simulator or Android emulator runtime registers automatically
with a running, signed-in Studio. No pairing file, environment variable, host
address, or build-time Studio probe is required.

`useNativeAllInOneDefaults` defaults to `false`. It only applies the native
iOS/Android all-in-one defaults: 400 ms sampling, 120 second retention, FPS,
touch capture, 2000 ms JPEG capture at quality 60 and max width 480, host
auto-probe, and standard native tools. It is not a master SDK enable switch and
does not infer whether the app is a debug build. Gate it with the app's own
condition, such as React Native's `__DEV__`, and configure `toolGuard`, capture
options, host auto-probe, and host connection separately.

Host auto-probe uses the native iOS/Android remembered-host retry behavior.
When enabled and the runtime is active, it retries previous host connections so
the app can reconnect after the host disappears and later reappears. Probing
pauses while a live session is connected and resumes after
`reconnectDelayMilliseconds` when that session is lost. Use
`withoutHostAutoProbe()` or set `hostAutoProbe.enabled` to `false` for flows
where reconnects should only happen after an explicit app action.

> **Important:** Screen capture will result in an FPS drop while native frames
> are rendered, encoded, and sent. Use conservative interval, quality, and
> max-width settings, and disable `sessionJpegCapture` for performance-focused
> runs unless visual evidence is required.

For simulator/emulator sessions, Studio can acknowledge the native
`device.profile` with host screenshot mode. The native SDK then suspends
periodic in-app JPEG capture for that session so Studio can use a host-side
source such as `simctl` or `adb`.

## Options

The TypeScript `AnsightOptions` surface mirrors Android `AnsightOptions`, iOS
`AnsightOptions`, and the .NET SDK concepts.

| Option | Purpose |
| --- | --- |
| `useNativeAllInOneDefaults` | Applies native iOS/Android all-in-one defaults when true. Defaults to false. This enables the native visual-tree/screenshot tools required by Studio visual tree inspection unless `remoteTools.visualTree` is explicitly false. Configure `toolGuard`, capture options, and `hostConnection` separately. |
| `clientName` | Default client name for host auto-probe and connections. |
| `sampleFrequencyMilliseconds` | Built-in telemetry sampling interval. |
| `retentionPeriodSeconds` | Local metric/event retention window. |
| `enableFramesPerSecond` | Enables native FPS sampling. |
| `enableBatteryLevel` | Enables battery sampling where available. |
| `defaultMemoryChannels` | Selects built-in memory channels. Prefer `managedHeap`, `nativeHeap`, `residentSetSize`, and `physicalFootprint`; `javaHeap` and `rss` are accepted as Android/RN compatibility aliases. |
| `reactNativeMemory` | Controls native React Native runtime memory channels. Enabled by default; set to `false` to disable, or use `{ jsHeapUsed, jsHeapTotal }`. |
| `additionalChannels` | Registers custom metric channels. |
| `sessionJpegCapture` | Object to enable/configure capture, or `false` to disable. Supports `captureGpuBackedSurfaces`; on iOS it defaults to `true` so Metal, SceneKit, and similar GPU-backed views are included. |
| `touchCapture` | Object to enable/configure capture, or `false` to disable. |
| `lifecycleCapture` | Native lifecycle and screen-view capture options. |
| `toolGuard` | `"disabled"`, `"readOnly"`, `"readWrite"`, or `"fullAccess"`. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Remembered-host retry settings for reconnecting after the host disappears and later reappears. |
| `hostConnection` | Enrollment reconnect and network policy. |
| `secureStorage` | Compatibility alias for native secure-storage allow-list settings. |
| `remoteTools` | Native visual tree, file, database, preferences, reflection, and secure-storage tool options. |
| `lifecycle` | JS AppState tracking toggle. Defaults to true. |

Cellular host connections are disabled by default. The restriction applies to
enrollment and reconnect requests. Opt in only for a trusted development host
or personal hotspot:

```ts
const options = Ansight.createOptionsBuilder()
  .withCellularHostConnections()
  .build();
```

You can also set `hostConnection.allowCellularConnections: true` directly.
This may consume mobile data and permits connection attempts over a broader or
carrier-managed network; use it only with a trusted development host.

Example:

```ts
await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: true,
  toolGuard: "readOnly",
  sessionJpegCapture: {
    intervalMilliseconds: 2000,
    quality: 60,
    maxWidth: 480,
    captureGpuBackedSurfaces: true,
  },
  touchCapture: {
    captureMoveEvents: true,
    captureCancelEvents: true,
    moveCaptureDistanceThreshold: 8,
    moveCaptureFramesPerSecond: 20,
  },
  hostAutoProbe: {
    enabled: true,
    initialDelayMilliseconds: 1000,
    probeIntervalMilliseconds: 5000,
    reconnectDelayMilliseconds: 10000,
    clientName: "My React Native App",
  },
});
```

On iOS, `captureGpuBackedSurfaces` defaults to `true` so Metal, SceneKit, and
similar GPU-backed views are included. Set it to `false` to use a lower-overhead
capture path when those surfaces are not needed.

## Native Tool Options

`remoteTools` configures the native tool suites registered by the bridge. `useNativeAllInOneDefaults: true` enables visual tree tools by default so Studio can pair `ui.get_visual_tree` data with `ui.get_screenshot` frames. Apps that do not use all-in-one defaults can opt in explicitly:

```ts
await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder()
    .withReadOnlyToolAccess()
    .withVisualTreeTools()
    .withFileSystemTools({
      additionalRoots: [{ alias: "exports", path: "/tmp/app-exports" }],
    })
    .withDatabaseTools({
      includePlatformRoots: true,
      additionalRoots: [{ alias: "fixtures", path: "/tmp/app-db" }],
    })
    .withPreferencesTools({
      allowedStores: ["standard"],
      allowedKeyPrefixes: ["debug."],
    })
    .withReflectionTools({
      includeBuiltInRoots: true,
      allowedTypePrefixes: ["App."],
    })
    .withRemoteTools({
      secureStorage: {
        appleService: "com.example.app",
        preferencesName: "secure_debug",
        allowedKeys: ["session_token"],
        allowedKeyPrefixes: ["debug."],
      },
    })
    .build(),
);
```

`reflect.list_roots` reports a `hostRuntime` descriptor on each root. Current
React Native reflection roots are hosted by the native iOS or Android runtime;
future JavaScript reflection roots should report `kind: "javascript"` and
`bridge: "react-native"` so callers can route reflection requests to the
correct runtime boundary.

`secureStorage.preferencesName` is Android-specific. `secureStorage.appleService`
is iOS-specific. The top-level `secureStorage` option is still accepted as a
compatibility alias for `remoteTools.secureStorage`.

## Host Connection

No connection call is needed for a simulator or emulator. On a physical
device, scan the QR displayed by Studio once:

```ts
await Ansight.enrollFromQrCode({
  clientName: "My React Native App",
  expectedAppId: "com.example.app",
});
```

After physical-device enrollment, `connect(null, options)` and the runtime
connection loop use the remembered registration:

```ts
await Ansight.connect(null, { clientName: "My React Native App" });
```

When Studio is closed or signed out, automatic attempts remain dormant and
retry later without failing the React Native app.

If the app already owns a scanner, pass its result through the explicit payload
API:

```ts
await Ansight.connect(enrollmentPayload, {
  clientName: "My React Native App",
  expectedAppId: "com.example.app",
  hostAddressOverride: "192.168.1.20",
});

await Ansight.clearCachedSession();
await Ansight.disconnect();
```

`openSession(pairingPayload, options)` is the low-level direct session path.
Prefer `connect(...)` for normal Studio sessions because it coordinates saved
config, host auto-probe, status, telemetry, and live tool handling.

## Runtime API

The bridge exposes the native SDK runtime surface:

| API | Purpose |
| --- | --- |
| `initialize`, `initializeAndActivate`, `activate`, `deactivate`, `clear` | Runtime lifecycle. |
| `connect`, `disconnect`, `openSession`, `completeSession`, `closeSession` | Host and live-session control. |
| `savePairingConfig`, `clearSavedPairing`, `clearCachedSession` | Pairing persistence. |
| `status`, `snapshot`, `hostConnectionStatus`, `currentOptions` | Diagnostics and state. |
| `registerMetricChannel`, `metric`, `recordMetric` | Metric channels and samples. |
| `event`, `recordEvent`, `screenViewed`, `trackRoute` | App events and screen views. |
| `setAppLifecycleState`, `startAppStateTracking`, `stopAppStateTracking` | Lifecycle state capture. |
| `recordedMetrics`, `recordedEvents` | Local retained telemetry. |
| `sendClientLog`, `addLogListener` | App-provided live-session log lines and SDK-internal log events. |
| `captureBuiltInTelemetrySample`, `captureScreenFrame` | Manual sampling and JPEG frame capture. |
| `isFramesPerSecondEnabled`, `enableFramesPerSecond`, `disableFramesPerSecond` | Runtime FPS sampling status and toggles. |
| `enableTouchCapture`, `disableTouchCapture` | Runtime touch-capture toggle. |
| `updateSessionProperties`, `clearSessionProperties` | Grouped session property mutations. |
| `registerCustomProperty`, `removeCustomProperty`, `clearCustomProperties` | Convenience property mutations. |
| `registerArtifactProvider`, `registerArtifactProviders` | App-defined artifact catalogs and binary exports. |
| `unregisterArtifactProvider`, `listRegisteredArtifactProviders`, `clearArtifactProviders` | Artifact-provider lifecycle. |

Native methods resolve to plain objects. Operation-like methods return
`{ success, message }`. Host connection methods return a richer result with
`success`, `message`, `source`, optional `reasonCode`, and optional live-session
details.

## Telemetry

```ts
await Ansight.registerMetricChannel({
  id: 42,
  name: "Cache",
  colorHex: "#FF9500",
  unit: "items",
  type: "cache",
});

await Ansight.metric(12, 42);
await Ansight.event({
  label: "cache_hit",
  type: "Info",
  details: "warm=true",
  channel: 42,
});

await Ansight.screenViewed("Orders", { route: "/orders" });
await Ansight.setAppLifecycleState("foreground");
```

Read retained samples:

```ts
const metrics = await Ansight.recordedMetrics(100);
const events = await Ansight.recordedEvents(100);
```

## Logs And Session Properties

`sendClientLog` sends an app-provided line over the active live session. It does
not automatically mirror console logs.

```ts
await Ansight.sendClientLog("Checkout loaded cartId=debug-42");
```

`addLogListener` observes SDK-internal logs emitted by the native bridge:

```ts
const logs = Ansight.addLogListener((entry) => {
  console.debug(`[Ansight:${entry.level}] ${entry.message}`);
});

logs.remove();
```

Session/custom properties are grouped string values:

```ts
await Ansight.updateSessionProperties({
  app: {
    region: "au",
    tenant: "debug",
  },
});

await Ansight.registerCustomProperty("app", "build", "debug");
await Ansight.removeCustomProperty("app", "tenant");
await Ansight.clearSessionProperties();
```

When connected, property mutations are sent immediately. When disconnected, the
latest values are included in the next `session.open`.

## Tool Guards

| Value | Allowed scopes |
| --- | --- |
| `"disabled"` | None |
| `"readOnly"` | Read |
| `"readWrite"` | Read, Write |
| `"fullAccess"` | Read, Write, Delete |

`"full"` is accepted as a compatibility alias for `"fullAccess"`.

## JavaScript Tools

Custom JavaScript tools can be exposed to Ansight Studio:

```ts
const registration = Ansight.registerTool(
  {
    id: "app.state.snapshot",
    name: "State Snapshot",
    description: "Returns current app state.",
    category: "app",
    scope: "Read",
    keywords: "state snapshot",
    argumentsSchema: { type: "object", additionalProperties: true },
    resultSchema: { type: "object", additionalProperties: true },
  },
  async (args, context) => ({
    success: true,
    result: {
      capturedAtUtc: new Date().toISOString(),
      platform: context.platform,
      requestId: context.requestId,
    },
  }),
);

await registration.ready;
await registration.unregister();
```

The native bridge registers JavaScript tools with `replaceExisting` semantics so
reloads can refresh handlers.

## App Artifacts

Artifact providers expose requestable app snapshots such as reports, logs,
traces, or images:

```ts
const reportProvider = Ansight.registerArtifactProvider({
  descriptor: {
    id: "app.reports",
    name: "App Reports",
    category: "diagnostics",
  },
  query: async () => [{
    id: "current",
    name: "Current Report",
    description: "Exports the current diagnostic report.",
    kind: "report",
    category: "diagnostics",
    content: {
      supportedMimeTypes: ["application/json"],
      defaultMimeType: "application/json",
      suggestedFileName: "report.json",
      supportsText: true,
    },
  }],
  create: async (request) => ({
    metadata: {
      artifactId: request.artifactId,
      providerId: request.providerId,
      name: "Current Report",
      kind: "report",
      mimeType: "application/json",
      fileName: "report.json",
    },
    payload: JSON.stringify(buildCurrentReport()),
  }),
});

await reportProvider.ready;
```

The first provider installs the read-scoped `artifacts.query` and
`artifacts.request` JavaScript tools in the native registry. A provider can
return text, base64, byte arrays, `ArrayBuffer`, or `Uint8Array`. Artifact
requests require a live Studio tool call; the bridge forwards the returned
bytes through the native binary-transfer channel. Call
`reportProvider.unregister()` during teardown or hot-reload cleanup.

## React Tools

`installReactTools` registers React Native specific remote tools backed by the
current React Fiber runtime:

- `react.get_component_tree`
- `react.get_shadow_tree`
- `react.find_components`
- `react.get_component`
- `react.get_navigation_state`
- `react.invoke_component_action` when `enableActions` is true

```ts
const reactTools = Ansight.installReactTools({
  includeBounds: true,
  includeProps: false,
  includeState: false,
  maxDepth: 60,
  maxNodes: 5000,
  navigationRef,
  enableActions: true,
  allowedActionProps: ["onPress"],
});

await reactTools.ready;
```

The component tree payload redacts sensitive prop and state keys and includes
native view bounds when React Native exposes a measurable native tag. The shadow
tree payload flattens composite components and returns the committed React
Native host/text/root nodes for layout-oriented inspection.

Action invocation is intentionally opt-in. Only function props listed in
`allowedActionProps` can be invoked, and the tool remains subject to the native
tool guard.

## React Navigation

Use the tracker to record route changes:

```tsx
const navigationRef = createNavigationContainerRef();
const tracker = Ansight.createReactNavigationTracker(navigationRef);

<NavigationContainer
  ref={navigationRef}
  onReady={tracker.onReady}
  onStateChange={tracker.onStateChange}
>
  {/* routes */}
</NavigationContainer>
```

Pass the same `navigationRef` to `installReactTools` to expose
`react.get_navigation_state`.

## Error Handlers

`installErrorHandlers` records unhandled JavaScript errors and promise
rejections as Ansight exception events:

```ts
const uninstall = Ansight.installErrorHandlers({ chain: true });

// Later, to restore the previous global ErrorUtils handler:
uninstall();
```

`chain: false` prevents forwarding to the previous global handler.

## Status And Debugging

```ts
const status = await Ansight.hostConnectionStatus();
const snapshot = await Ansight.snapshot();
const options = await Ansight.currentOptions();

await Ansight.captureBuiltInTelemetrySample();
await Ansight.captureScreenFrame({
  quality: 60,
  maxWidth: 480,
  captureGpuBackedSurfaces: true,
});
await Ansight.enableTouchCapture();
await Ansight.disableTouchCapture();
```

Process memory is sampled by the native runtime. On iOS, `physicalFootprint`
is the supported process-memory default and reflects the memory footprint used
by Jetsam, including the React Native runtime inside the app process. On
Android, `managedHeap`/`javaHeap`, `nativeHeap`, and `residentSetSize`/`rss`
map to the platform heap and process memory counters.

## Validation

The package checks its JavaScript and TypeScript surfaces:

```sh
npm run check
```

The first-party validation app lives in:

```text
/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/react-native/ansight-react-native-harness
```

It exercises the native runtime bridge, standard native remote tools,
JavaScript custom tools, React visual-tree tools, SQLite/file fixtures,
screenshot capture, and touch/session telemetry.

The current-Expo validation app lives in:

```text
/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/react-native/ansight-expo-harness
```

It is pinned to Expo SDK 57 and React Native 0.86 with the New Architecture and
Hermes enabled. It validates Expo CNG/autolinking, the bundled config plugin,
Android and iOS native builds, and the same live Studio feature surface as the
baseline harness.
