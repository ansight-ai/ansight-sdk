"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_CONTROLS_FIXTURE_DIR;
if (!root) throw new Error("fixture directory required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timeout = setTimeout(() => app.exit(2), 20000),
  delay = (ms) => new Promise((r) => setTimeout(r, ms));
app
  .whenReady()
  .then(async () => {
    const w = new BrowserWindow({
      width: 640,
      height: 480,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    await w.loadURL(
      'data:text/html,<button id="probe" style="width:200px;height:100px" onclick="document.body.dataset.clicked=Number(document.body.dataset.clicked||0)+1">Capture controls</button>',
    );
    const sdk = createAnsight({
      appId: "com.ansight.controls.fixture",
      enabled: true,
      electron: require("electron"),
      enableFramesPerSecond: false,
      touchCapture: true,
    });
    sdk.attachWindow(w);
    w.focus();
    w.webContents.focus();
    const messages = [],
      checks = [];
    sdk.offline = {
      append: (m) => {
        messages.push(m);
        return true;
      },
      stop: async () => {},
    };
    const frameSamples = () =>
      messages.flatMap((m) => m.metrics || []).filter((m) => m.channel === 104);
    const touches = () =>
      messages.filter((m) => m.type === "CLIENT_TOUCH_INPUT");
    const sample = async () => {
      await delay(150);
      await sdk.streams.sample();
    };
    const click = async () => {
      sdk.click(w, { x: 60, y: 60 });
      await delay(80);
    };
    await sdk.streams.configureCapture();
    await sample();
    assert.equal(frameSamples().length, 0);
    assert.equal(sdk.isFramesPerSecondEnabled, false);
    checks.push("FPS initially disabled produces no samples");
    await sdk.enableFramesPerSecond();
    await sample();
    assert.ok(frameSamples().some((m) => m.value > 0));
    checks.push("enabling FPS records real renderer callbacks");
    await sdk.disableFramesPerSecond();
    const count = frameSamples().length;
    await sample();
    assert.equal(frameSamples().length, count);
    assert.equal(
      await w.webContents.executeJavaScriptInIsolatedWorld(1077, [
        { code: "globalThis.__ansightObservation.captureFrames" },
      ]),
      false,
    );
    checks.push("disabling FPS stops samples and renderer callback loop");
    await sdk.disableTouchCapture();
    await click();
    await sample();
    assert.equal(touches().length, 0);
    assert.equal(
      await w.webContents.executeJavaScript(
        "Number(document.body.dataset.clicked)",
      ),
      1,
    );
    checks.push(
      "disabled capture preserves actual app input without recording it",
    );
    await sdk.enableTouchCapture();
    await click();
    await sample();
    assert.ok(touches().some((m) => m.rows[0][1] === 0));
    assert.ok(touches().some((m) => m.rows[0][1] === 2));
    checks.push("re-enabled capture records native down and up");
    let allow = false;
    await sdk.setTouchCaptureGuard(() => allow);
    const before = touches().length;
    await click();
    await sample();
    assert.equal(touches().length, before);
    checks.push("guard denies pointer evidence");
    allow = true;
    await sample();
    await click();
    await sample();
    assert.ok(touches().length > before);
    checks.push("dynamic guard resumes recording");
    const after = touches().length;
    await sdk.setTouchCaptureGuard(() => {
      throw new Error("guard fixture");
    });
    await click();
    await sample();
    assert.equal(touches().length, after);
    checks.push("throwing guard fails closed");
    await sdk.setTouchCaptureGuard(null);
    await click();
    await sdk.disableTouchCapture();
    await sample();
    assert.equal(touches().length, after);
    checks.push("disable discards queued pointer evidence");
    const disabled = createAnsight({
      appId: "test",
      enabled: false,
      electron: require("electron"),
      touchCapture: false,
    });
    await disabled.enableTouchCapture();
    assert.equal(disabled.isTouchCaptureEnabled, false);
    checks.push("unconfigured capture cannot be re-enabled");
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify({ passed: true, checks }, null, 2),
    );
    console.log(JSON.stringify({ passed: true, checks }));
    await sdk.stop();
    clearTimeout(timeout);
    app.quit();
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
