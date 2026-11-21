"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_RESTART_DIR;
if (!root) throw new Error("fixture directory required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timeout = setTimeout(() => app.exit(2), 25000);
app
  .whenReady()
  .then(async () => {
    const sdk = createAnsight({
      appId: "com.ansight.electron.restart.fixture",
      enabled: true,
      discoveryPort: 47123,
      offlineCapture: { enabled: true },
      crashCapture: { nativeSignalCapture: true },
    });
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
      'data:text/html,<button id="probe">Restart fixture</button>',
    );
    const checks = [];
    for (let i = 0; i < 3; i++) {
      await sdk.start();
      sdk.attachWindow(w);
      while (!sdk.connected) await new Promise((r) => setTimeout(r, 50));
      assert.ok(sdk.tools.has("offline_capture.transfer"));
      const result = await sdk.execute(
        { toolId: "ui.get_visual_tree", arguments: {} },
        { transfers: [] },
      );
      assert.ok(result.result.root);
      sdk.event("Restart cycle", { cycle: i });
      await new Promise((r) => setTimeout(r, 300));
      await sdk.stop();
      assert.equal(sdk.running, false);
      assert.equal(sdk.tools.has("offline_capture.transfer"), false);
      assert.equal(sdk.windows.size, 0);
      checks.push(
        `cycle ${i + 1}: connect, inspect, record, stop and unregister`,
      );
    }
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify({ passed: true, checks }, null, 2),
    );
    console.log(JSON.stringify({ passed: true, checks }));
    clearTimeout(timeout);
    app.quit();
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
