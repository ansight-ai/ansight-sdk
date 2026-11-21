"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  { createHash } = require("node:crypto");
const { createAnsight } = require("..");
const server = require("node:http").createServer((req, res) => {
  res.setHeader(
    "Content-Type",
    req.url === "/payload" ? "text/plain" : "text/html",
  );
  res.end(
    req.url === "/payload"
      ? "offline network evidence"
      : '<h1>Offline evidence marker</h1><button id="action" style="width:200px;height:70px">Offline action</button>',
  );
});
app.on("will-quit", () => server.close());
const directory = process.env.ANSIGHT_OFFLINE_FIXTURE_DIR;
if (!directory) throw new Error("ANSIGHT_OFFLINE_FIXTURE_DIR required");
app.setPath("userData", directory);
const sdk = createAnsight({
  appId: "com.ansight.electron.offline-fixture",
  appName: "Electron offline verification",
  enabled: true,
  toolGuard: "readWrite",
  captureNetwork: true,
  discoveryPort: 47999,
  offlineCapture: {
    enabled: true,
    transferMode: process.argv.includes("manual") ? "manual" : "automatic",
  },
  sessionJpegCapture: {
    intervalMilliseconds: 500,
    mode: "screenshotAndVisualTree",
  },
});
sdk.on("diagnostic", (e) => console.error(e));
const timeout = setTimeout(() => {
  console.error("Offline fixture timed out", sdk.offlineCaptureLastError);
  app.exit(1);
}, 30000);
app.whenReady().then(async () => {
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const w = new BrowserWindow({
    width: 500,
    height: 400,
    focusable: false,
    webPreferences: {
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  sdk.attachWindow(w);
  await w.loadURL(`http://127.0.0.1:${server.address().port}/`);
  await sdk.setSessionProperties({ fixture: { offline: true } });
  await sdk.start();
  sdk.event("offline.before-connect", { marker: 42 });
  sdk.log("offline.warning", { level: 2 });
  await w.webContents.executeJavaScript(
    "fetch('/payload?private=redacted').then(r => r.text())",
  );
  sdk.click(w, { x: 60, y: 100 });
  setTimeout(async () => {
    try {
      await sdk.offline.queue;
      const sessions = await sdk.offline.sessions();
      if (!sessions.length) throw new Error("No offline capture");
      const source = sessions[0],
        dir = sdk.offline.directory(source.sessionId);
      const evidence = {
        sourceSessionId: source.sessionId,
        processSessionId: sdk.processSessionId,
        files: {},
      };
      const index = fs
        .readFileSync(path.join(dir, "screenshots/index/frames.jsonl"), "utf8")
        .trim()
        .split("\n")
        .map(JSON.parse);
      for (const frame of index)
        evidence.files[frame.path] = createHash("sha256")
          .update(fs.readFileSync(path.join(dir, frame.path)))
          .digest("hex");
      fs.writeFileSync(
        path.join(directory, "expected.json"),
        JSON.stringify(evidence, null, 2),
      );
      sdk.options.discoveryPort = 47123;
      clearTimeout(sdk.retryTimer);
      await sdk.connect();
      if (process.argv.includes("manual")) console.log("MANUAL_READY");
      const timer = setInterval(async () => {
        if (sdk.offline.transferring) return;
        if ((await sdk.offline.sessions()).length === 0) {
          clearInterval(timer);
          clearTimeout(timeout);
          console.log("OFFLINE_IMPORTED", source.sessionId);
          app.quit();
        }
      }, 100);
    } catch (e) {
      console.error(e);
      app.exit(1);
    }
  }, 4200);
});
