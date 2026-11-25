# @ansight/electron

Ansight inspection and recording for Electron development and explicitly enabled QA builds. Runs in the main process; attach only the BrowserWindows that should be inspected. Renderer instrumentation uses an isolated world without exposing Node or IPC to the page.

## Install and start

Install `@ansight/electron` in the Electron app package. Start the normal Ansight local host, then initialize before creating the windows to inspect:

```js
const { app, BrowserWindow } = require("electron");
const { createAnsight } = require("@ansight/electron");
const ansight = createAnsight({
  appId: "com.example.desktop",
  appName: "Example Desktop",
  enabled: !app.isPackaged && process.env.ANSIGHT_ENABLED === "1",
  toolGuard: "readWrite",
  captureNetwork: true,
  offlineCapture: { enabled: true },
});

app.whenReady().then(async () => {
  await ansight.start();
  const window = new BrowserWindow({
    webPreferences: {
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  ansight.attachWindow(window);
  await window.loadFile("index.html");
});
```

Local enrollment uses loopback discovery, default port 45123. Installation identity and retained evidence live beneath the app's private `userData/ansight` directory. Local SDK enrollment does not require cloud sign-in. Hosted agentic tests use the host's signed-in account and selected organisation.

Capture is opt-in through `enabled`. Packaged QA apps additionally require `allowPackaged: true`. Default tool access is `readOnly`. `readWrite` permits writes; `fullAccess` also permits critical operations, subject to the host registration's policy ceiling. Local enrollment normally retains a write ceiling. A lower ceiling is not bypassed by the SDK.

`stop()` flushes offline recording and detaches windows. Await it before restarting. After `start()` again, explicitly reattach the windows you want inspected. The SDK also flushes during normal application quit.

## What is recorded

Default sampling is 400 ms, clamped to 200–2000 ms by `sampleFrequencyMilliseconds`. Local metric/event readback defaults to 120 seconds, configurable through `retentionPeriodSeconds` (60–3600 seconds), with additional count/byte caps. `clear()` clears local readback, not host sessions or offline archives.

| Channel | Measurement                                                                  |
| ------- | ---------------------------------------------------------------------------- |
| 100     | Main-process resident memory, MiB                                            |
| 101     | Main-process V8 heap used, MiB                                               |
| 102     | Main-process CPU as percentage of one core                                   |
| 103     | Sampling timer lateness, ms                                                  |
| 104     | Mean visible-window animation callbacks per second; not GPU presentation FPS |
| 105     | Dropped capture records                                                      |

Screenshots default to JPEG every 2000 ms, quality 60, maximum width 480. `sessionJpegCapture: false` disables periodic capture. Modes are `screenshotOnly`, `screenshotAndVisualTree`, and `screenshotWithVisualTreeOnTouch`. The last mode captures linked trees on gestures with native coalescing, rather than idle tree polling.

DOM snapshots expose semantic IDs, actions and CSS-pixel bounds. Password values and text under `data-ansight-private` are excluded from structured evidence. Screenshots still show rendered pixels. The SDK does not inspect unattached windows or automatically descend into cross-origin frames.

Automatic network capture observes attached renderers through Chromium's debugger and records request metadata; it strips URL credentials/query/fragment and omits headers/bodies. It does not replace a debugger already owned by another client. `recordNetworkRequest()` captures app-supplied main-process or other requests through the standard network envelope, including measured timing and optional headers/bodies. Default redaction removes credential fields and omits binary payloads; text bodies are limited to 64 KiB. The synchronous `networkCapture.sanitizer` runs before transport or offline persistence and can return `null` to exclude a request.

## Application instrumentation

Declare custom metric channels with `additionalChannels` (unique IDs 0–255; SDK channels 0, 4 and 100–105 are reserved). Call `metric(value, channel)`, `event(label, details, type)`, `log(message, details)` and `screenViewed(name, stringDetails)`. Screen/lifecycle events use native channel 4. `setAppLifecycleState(state)` explicitly controls lifecycle; pass `null` to restore automatic attached-window visibility tracking.

`recordedMetrics()`, `recordedEvents()` and `snapshot()` return isolated copies. `enableFramesPerSecond()` / `disableFramesPerSecond()` control renderer animation sampling. `enableTouchCapture()` / `disableTouchCapture()` control pointer recording; await them for renderer configuration to complete. `touchCapture: false` leaves capture unconfigured and cannot be overridden at runtime. `setTouchCaptureGuard(callback)` evaluates a synchronous guard during capture configuration and before forwarding sampled pointer events; only literal `true` allows recording. For an immediate privacy boundary, await `disableTouchCapture()`.

Connection and diagnostic notifications use the EventEmitter events `connected`, `disconnected` and `diagnostic`. Register tools with `registerTool()`, explicit object roots with `registerRoot()`, and app artifacts with `registerArtifactProvider()`. The TypeScript declarations describe these contracts.

## Optional providers and tools

- Files: configure explicit `fileRoots` aliases. Traversal and symlinks are rejected; binary transfers carry SHA-256 integrity metadata.
- SQLite: configure explicit `databases` aliases and install an Electron-ABI-compatible `better-sqlite3` in the app. Queries execute in a separate read-only worker with deadline, heap and response limits. `sqliteModulePath` can select the app's module explicitly.
- Secure storage: supply the app's actual `secureStorage` adapter. No substitute credential vault is created. All secure operations require critical policy.
- Reflection: inspect only registered JavaScript roots. Read tools do not invoke getters; mutation/invocation requires critical policy.
- OS descriptors: native macOS `lsof` and Linux `/proc` collectors. Windows does not advertise these tools; `electron.get_active_resources` separately reports Node event-loop resources.
- Preferences: attached renderer localStorage/sessionStorage. Clipboard tools use Electron's actual system text clipboard.
- Video: `electron.video.*` records one attached window using scoped WebContents capture, without desktop/audio capture. Recording is bounded to 10 minutes and 64 MiB. Host test-video integration requires ffmpeg for MP4 conversion.

Tool discovery reports only configured providers. Standard UI query, wait, inspection, actions and input-transparent overlays are available on attached DOM windows. Query references are scoped to the current snapshot/window; capture fresh evidence after a reference becomes stale.

## Crash and offline recovery

Offline recording is opt-in. Defaults: 10 sessions, 128 MiB per session, 512 MiB retained and an 8 MiB write queue. Transfers are purged only after host acknowledgment of the archive hash. Automatic and manual transfer modes use the standard host importer.

Node fatal exceptions and renderer crash events use the native Ansight crash handoff. `crashCapture.nativeSignalCapture: true` additionally opts into ownership of Electron Crashpad. It changes the app's crash-dump location to SDK-owned storage, disables direct uploads, and cannot stop Crashpad until process exit. Do not enable it alongside another owner of `crashReporter`.

On the next launch, native dumps are matched to their original process session and retained until host acknowledgment. The host accepts minidumps up to 4 MiB. Larger dumps stay local subject to retention. Inactive-process storage is pruned at startup/acknowledgment to the configured pending count and `maximumPendingReports × 4 MiB`; active Crashpad writers are left alone. Default pending count is 8 and retention is 7 days. SIGKILL, OS watchdog diagnostics and native stack symbolication are not provided by this SDK.

## Verification and platform scope

`npm test` runs the unit/protocol suite; `npm run build` checks package source syntax. `test/*-fixture.cjs` exercises actual Electron behavior using isolated profiles and local hosts. The package excludes tests and fixtures. This implementation has been exercised on macOS with Electron 28.3.3 and Lex IDE. Linux descriptor code exists but has not been verified on this workstation; Windows/macOS differences should be tested before making a platform-wide release claim.
