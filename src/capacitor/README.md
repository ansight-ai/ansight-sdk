# Ansight for Capacitor

`@ansight/capacitor` brings the Ansight Android and iOS runtimes to Capacitor 8
applications. It exposes native telemetry, pairing, screenshots, touch capture,
session properties, logs, guarded remote tools, JavaScript custom tools,
artifacts, DOM inspection, route tracking, lifecycle tracking, and JavaScript
error capture through one TypeScript API.

## Install

```bash
npm install @ansight/capacitor
npx cap sync
```

Requirements are Node 22+, Capacitor 8, Android API 24+ with Java 21, and iOS
15+. The npm package links `ai.ansight:ansight-android` on Android and the
`Ansight` Swift package/CocoaPod on iOS.

## Start Ansight

```ts
import Ansight from '@ansight/capacitor';

await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder()
    .withAnsightDefaults()
    .withReadOnlyToolAccess()
    .withDomTools()
    .withErrorCapture()
    .registerCustomProperty('app', 'flavour', 'development')
    .build(),
);
```

The native iOS Simulator or Android emulator runtime registers automatically
with a running, signed-in Studio. No pairing file, build variable, host address,
or build-time Studio probe is required. On a physical device, call
`Ansight.enrollFromQrCode(...)` once; later launches use the saved
app-installation registration.

Use `.withAnsightSdk()` or `.withAllToolAccess()` only in trusted development
builds. Do not ship unrestricted remote tools in
store builds.

For advanced paste, file-import, CI, or custom-UI flows, pair with an explicit
JSON document:

```ts
await Ansight.savePairingConfig(pairingJson);
await Ansight.connect(pairingJson, {
  expectedAppId: 'your-app-id',
  clientName: 'Capacitor app',
});
```

Cellular host connections are disabled by default for bundled configs,
QR/payload flows, remembered/saved profiles, and manual connections. Enable
them only for a trusted development host or personal hotspot:

```ts
const options = Ansight.createOptionsBuilder()
  .withCellularHostConnections()
  .build();
```

The equivalent direct option is
`hostConnection.allowCellularConnections: true`. This may consume mobile data
and permits connection attempts over a broader or carrier-managed network;
use it only with a trusted development host.

## Telemetry and capture

```ts
await Ansight.registerMetricChannel({
  id: 42,
  name: 'Queue depth',
  unit: 'items',
});
await Ansight.metric(7, 42);
await Ansight.event({
  label: 'Sync completed',
  type: 'App',
  details: JSON.stringify({ records: 7 }),
});
await Ansight.screenViewed('Settings');
await Ansight.sendClientLog('Settings loaded');
await Ansight.captureScreenFrame({ quality: 70, maxWidth: 720 });
```

The facade also exposes `recordedMetrics`, `recordedEvents`, FPS and touch
capture controls, lifecycle state, runtime snapshots, current options, host
status/capabilities, and session custom-property mutations.

### Network capture

Opt in to `fetch` and `XMLHttpRequest` instrumentation through
the builder or the direct `networkCapture` option:

```ts
await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder()
    .withAnsightDefaults()
    .withNetworkCapture({
      maximumBodyBytes: 64 * 1024,
      additionalSensitiveHeaderNames: ["x-tenant-secret"],
      additionalSensitiveQueryParameterNames: ["session"],
      requestSanitizer: request =>
        request.url.includes("/health") ? null : request,
    })
    .withoutNetworkRequestBodies() // optional, independent opt-out
    .build(),
);
```

Text request and response bodies are included by default after the builder
explicitly enables network capture, with a 64 KiB default per-body limit.
Larger `maximumBodyBytes` values are honored; request and response bodies can be
disabled independently, while binary bodies require `captureBinaryBodies`.
Standard credentials, cloud signed-URL fields, cookies, URL user information,
and sensitive text-body assignments are redacted automatically. The browser
hooks attach only while the native runtime is connected to a host.

Use `installNetworkCapture(...)` / `uninstallNetworkCapture()` for independent
lifecycle control. `recordNetworkRequest(...)` supports custom HTTP stacks,
and `sanitizeNetworkRequest(...)` exposes the app-side policy for inspection
and tests.

The builder mirrors the portable React Native helpers for memory-channel
exclusions, numeric JPEG capture configuration, bundled host configuration,
discovery/retention settings, and visual-tree enable/disable. App-state
tracking is available under both `startLifecycleTracking` /
`stopLifecycleTracking` and `startAppStateTracking` / `stopAppStateTracking`.

Set `sessionJpegCapture.mode` to `screenshotWithVisualTreeOnTouch` to retain
periodic screenshots while the native runtime captures visual trees only on
touch down and touch up. Move and cancel events do not trigger capture. Rapid
boundaries are coalesced and rate-limited to protect screenshot cadence. Native
touch capture and visual-tree providers must remain enabled.

Open-file-handle and JNI reference-count diagnostics are disabled by default.
Enable them with `withOpenFileHandleTracking()` and
`withJniReferenceCountTracking()`; matching `without...` methods disable them
again. Open handles are sampled by the native Android/iOS runtime. JNI counts
are available on Android only when the host integration can supply them.

## Automatic session properties

The JavaScript bridge adds these groups to every session:

| Group | Properties |
| --- | --- |
| `capacitor` | Ansight SDK version, supported Capacitor version and exact build-time core version, platform and runtime language, native/web execution mode, WebView/browser engine and available engine version, and user agent. |
| `localization` | Canonical locale, language, optional region, IANA time zone when exposed by `Intl`, and UTC offset in minutes. |

Capacitor does not expose the installed core package's exact version at
runtime, so `capacitorVersion` records the supported `8.x` runtime and
`compiledCapacitorVersion` records the exact core version used to build this
plugin. Caller values override automatic values with the same group and key.
Clearing properties, or removing one automatic property, restores the current
bridge-owned values. Apps with their own language selector can override the
`localization` group with the selected locale and language.

## JavaScript tools and artifacts

```ts
const tool = Ansight.registerTool(
  {
    id: 'app.get_state',
    name: 'Get app state',
    category: 'App',
    scope: 'read',
  },
  async () => ({ success: true, result: { ready: true } }),
);
await tool.ready;

const provider = Ansight.registerArtifactProvider({
  descriptor: { id: 'app.exports', name: 'App exports' },
  query: async () => [{ id: 'state', name: 'State JSON', contentType: 'application/json' }],
  create: async () => ({
    payload: { text: JSON.stringify({ ready: true }) },
    metadata: { fileName: 'state.json', contentType: 'application/json' },
  }),
});
await provider.ready;
```

Artifact payloads may be text, `Uint8Array`, `ArrayBuffer`, or byte arrays.
Binary payloads use the native live-session transfer channel.

## WebView DOM tools

`.withDomTools()` registers `dom.get_document`, `dom.inspect_node`, and
`dom.query_selector`. Pass `{ allowActions: true }` to add the write-scoped
`dom.invoke_action` tool for click, focus, blur, and value-change operations.
Native `ui.*` tools remain available for the Android/iOS view hierarchy.
Call `uninstallDomTools()` to remove the adapter and its registrations.

For no-bundler applications, `dist/standalone.js` is a self-contained script
that initializes the developer defaults. Configure it before loading:

```html
<script>
  globalThis.__ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__ = {
    toolGuard: 'readOnly',
  };
</script>
<script src="./node_modules/@ansight/capacitor/dist/standalone.js"></script>
```

## Validation

The complete interactive harness is in
[`example-app`](./example-app/README.md). It contains 54 feature checks and
buildable Android and iOS projects. The repository also provides a pinned
25-application open-source compatibility corpus:

```bash
npm --prefix src/capacitor run verify
node scripts/setup-capacitor-test-apps.mjs
```
