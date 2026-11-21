"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_RUNTIME_FIXTURE_DIR;
if (!root) throw new Error("fixture directory required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timeout = setTimeout(() => app.exit(2), 30000);
app
  .whenReady()
  .then(async () => {
    const w = new BrowserWindow({
      width: 400,
      height: 300,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    await w.loadURL(
      "data:text/html,<h1>Ansight custom telemetry verification</h1>",
    );
    const sdk = createAnsight({
      appId: "com.ansight.electron.runtime.fixture",
      appName: "Electron runtime verification",
      enabled: true,
      discoveryPort: 47123,
      sessionJpegCapture: false,
      additionalChannels: [
        {
          id: 42,
          name: "Verified documents",
          unit: "count",
          kind: "documents.verified",
        },
      ],
    });
    sdk.attachWindow(w);
    await sdk.start();
    while (!sdk.connected) await new Promise((r) => setTimeout(r, 100));
    sdk.metric(17, 42);
    sdk.event("Runtime parity probe", { documents: 17 });
    assert.equal(sdk.recordedMetrics().find((m) => m.channel === 42).value, 17);
    assert.ok(
      sdk.recordedEvents().some((e) => e.label === "Runtime parity probe"),
    );
    fs.writeFileSync(
      path.join(root, "ready.json"),
      JSON.stringify({ passed: true, snapshot: sdk.snapshot() }, null, 2),
    );
    await new Promise((r) => setTimeout(r, 3000));
    await sdk.stop();
    clearTimeout(timeout);
    app.quit();
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
