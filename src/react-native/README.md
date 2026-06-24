# @ansight/react-native

React Native bridge for the Ansight mobile SDK.

The package is intentionally thin: pairing, transport, telemetry, screen
capture, touch capture, native tool discovery, and native tool execution are
handled by the Ansight iOS and Android SDKs. The JavaScript layer normalizes
React Native inputs, forwards runtime calls to the native bridge, and registers
JavaScript-backed tools for React component-tree inspection.

## Install

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

This package version expects matching native SDK packages:

- CocoaPods: `Ansight`, `AnsightObjC` version `1.0.1`
- Maven: `ai.ansight:ansight-android:1.0.1`

## Quickstart

```ts
import Ansight from "@ansight/react-native";

const isDevelopmentOnly = __DEV__;

await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: isDevelopmentOnly,
  clientName: "My React Native App",
  hostConnection: isDevelopmentOnly ? {
    bundledDeveloperConfigJson: process.env.EXPO_PUBLIC_ANSIGHT_PAIRING_CONFIG_JSON,
  } : undefined,
  toolGuard: isDevelopmentOnly ? "readOnly" : "disabled",
  lifecycle: true,
});

await Ansight.connect(null, {
  clientName: "My React Native App",
  expectedAppId: "com.example.app",
});
```

`useNativeAllInOneDefaults` defaults to `false`. It only applies the native
iOS/Android all-in-one defaults: 400 ms sampling, 120 second retention, FPS,
touch capture, 2000 ms JPEG capture at quality 60 and max width 480, host
auto-probe, and standard native tools. It is not a master SDK enable switch and
does not infer whether the app is a debug build. Gate it with the app's own
condition, such as React Native's `__DEV__`, and configure `toolGuard`, capture
options, host auto-probe, and host connection separately.

## Options

The TypeScript `AnsightOptions` surface mirrors Android `AnsightOptions`, iOS
`AnsightOptions`, and the .NET SDK concepts.

| Option | Purpose |
| --- | --- |
| `useNativeAllInOneDefaults` | Applies native iOS/Android all-in-one defaults when true. Defaults to false. This is not a master enable switch; configure `toolGuard`, capture options, and `hostConnection` separately. |
| `pairingConfigJson` | Legacy top-level pairing JSON. Prefer `hostConnection.*`. |
| `clientName` | Default client name for host auto-probe and connections. |
| `sampleFrequencyMilliseconds` | Built-in telemetry sampling interval. |
| `retentionPeriodSeconds` | Local metric/event retention window. |
| `enableFramesPerSecond` | Enables native FPS sampling. |
| `enableBatteryLevel` | Enables battery sampling where available. |
| `defaultMemoryChannels` | Selects built-in memory channels. Prefer `managedHeap`, `nativeHeap`, `residentSetSize`, and `physicalFootprint`; `javaHeap` and `rss` are accepted as Android/RN compatibility aliases. |
| `reactNativeMemory` | Controls native React Native runtime memory channels. Enabled by default; set to `false` to disable, or use `{ jsHeapUsed, jsHeapTotal }`. |
| `additionalChannels` | Registers custom metric channels. |
| `sessionJpegCapture` | Object to enable/configure capture, or `false` to disable. |
| `touchCapture` | Object to enable/configure capture, or `false` to disable. |
| `lifecycleCapture` | Native lifecycle and screen-view capture options. |
| `toolGuard` | `"disabled"`, `"readOnly"`, `"readWrite"`, or `"fullAccess"`. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Automatic host reconnect loop settings. |
| `hostConnection` | Saved, bundled, and developer pairing settings. |
| `secureStorage` | Compatibility alias for native secure-storage allow-list settings. |
| `remoteTools` | Native visual tree, file, database, preferences, reflection, and secure-storage tool options. |
| `lifecycle` | JS AppState tracking toggle. Defaults to true. |

Example:

```ts
await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: true,
  toolGuard: "readOnly",
  sessionJpegCapture: {
    intervalMilliseconds: 2000,
    quality: 60,
    maxWidth: 480,
  },
  touchCapture: {
    captureMoveEvents: true,
    captureCancelEvents: true,
    moveCaptureDistanceThreshold: 8,
    moveCaptureFramesPerSecond: 20,
  },
  hostAutoProbe: {
    enabled: true,
    clientName: "My React Native App",
  },
});
```

## Native Tool Options

`remoteTools` configures the native tool suites registered by the bridge. Visual tree tools are opt-in:

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

`secureStorage.preferencesName` is Android-specific. `secureStorage.appleService`
is iOS-specific. The top-level `secureStorage` option is still accepted as a
compatibility alias for `remoteTools.secureStorage`.

## Host Connection

Use `connect(null, options)` for the default automatic flow:

```ts
await Ansight.connect(null, {
  clientName: "My React Native App",
  expectedAppId: "com.example.app",
});
```

Automatic connection tries:

1. `hostConnection.bundledDeveloperConfigJson`
2. native cached host profiles where implemented
3. saved pairing config
4. `hostConnection.bundledConfigJson`

Use explicit payloads for QR, paste, or app-owned import flows:

```ts
await Ansight.connect(pairingJson, {
  clientName: "My React Native App",
  expectedAppId: "com.example.app",
  hostAddressOverride: "192.168.1.20",
});

await Ansight.savePairingConfig(pairingJson, {
  expectedAppId: "com.example.app",
});

await Ansight.clearSavedPairing();
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
await Ansight.captureScreenFrame({ quality: 60, maxWidth: 480 });
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
