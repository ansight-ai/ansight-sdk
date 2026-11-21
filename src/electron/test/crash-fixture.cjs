"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path");
const { createAnsight } = require("..");
if (!process.env.ANSIGHT_CRASH_FIXTURE_DIR)
  throw new Error("ANSIGHT_CRASH_FIXTURE_DIR required");
app.setPath("userData", process.env.ANSIGHT_CRASH_FIXTURE_DIR);
const recover = process.argv.includes("recover");
const sdk = createAnsight({
  appId: "com.ansight.electron.crash-fixture",
  appName: "Electron crash verification",
  enabled: true,
  discoveryPort: 47123,
  sessionJpegCapture: false,
  crashCapture: { hostHandoffEnabled: recover },
});
sdk.on("diagnostic", (e) => console.error(e));
const timeout = setTimeout(() => {
  console.error("Crash fixture timed out");
  app.exit(1);
}, 20000);
app.whenReady().then(async () => {
  const w = new BrowserWindow({
    show: false,
    webPreferences: {
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  sdk.attachWindow(w);
  await w.loadURL("data:text/html,<h1>Crash verification fixture</h1>");
  sdk.once("connected", () => {
    if (recover) {
      const timer = setInterval(() => {
        if (!sdk.crashes.delivering && sdk.crashes.files().length === 0) {
          clearInterval(timer);
          clearTimeout(timeout);
          console.log("RECOVERY_ACKNOWLEDGED", sdk.processSessionId);
          app.quit();
        }
      }, 100);
    } else {
      sdk.event("Before intentional renderer crash");
      w.webContents.once("render-process-gone", () => {
        setTimeout(() => {
          clearTimeout(timeout);
          console.log(
            "CRASH_RETAINED",
            sdk.processSessionId,
            sdk.crashes.files().length,
          );
          app.exit(sdk.crashes.files().length === 1 ? 0 : 1);
        }, 200);
      });
      w.webContents.debugger.attach("1.3");
      void w.webContents.debugger.sendCommand("Page.crash").catch(() => {});
    }
  });
  await sdk.start();
});
