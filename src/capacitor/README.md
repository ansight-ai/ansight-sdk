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

await Ansight.connect(null, { clientName: 'Capacitor app' });
```

Use `.withAnsightSdk()` or `.withAllToolAccess()` only in trusted development
builds. Do not embed developer pairing material or unrestricted remote tools in
store builds.

Pair with an explicit JSON document when needed:

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
signed pairing-config validation still applies.

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

The builder mirrors the portable React Native helpers for memory-channel
exclusions, numeric JPEG capture configuration, bundled host configuration,
discovery/retention settings, and visual-tree enable/disable. App-state
tracking is available under both `startLifecycleTracking` /
`stopLifecycleTracking` and `startAppStateTracking` / `stopAppStateTracking`.

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
