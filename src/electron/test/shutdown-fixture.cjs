"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_SHUTDOWN_FIXTURE_DIR;
if (!root) throw new Error("ANSIGHT_SHUTDOWN_FIXTURE_DIR required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
// Standard macOS apps stay resident when the final window is manually closed.
// This listener also exposes re-entrant quit bugs after asynchronous SDK cleanup.
app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
const timer = setTimeout(() => {
  console.error("SHUTDOWN_TIMEOUT");
  app.exit(2);
}, 6000);
app.on("will-quit", () => {
  clearTimeout(timer);
  console.log("SHUTDOWN_COMPLETED");
});
app
  .whenReady()
  .then(async () => {
    const sdk = createAnsight({
      appId: "com.ansight.shutdown.fixture",
      enabled: true,
      discoveryPort: process.argv.includes("connected") ? 47123 : 47999,
      offlineCapture: { enabled: true },
    });
    const w = new BrowserWindow({
      show: false,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    sdk.attachWindow(w);
    await w.loadURL("data:text/html,<h1>Shutdown fixture</h1>");
    const connected = new Promise((resolve) => sdk.once("connected", resolve));
    await sdk.start();
    if (process.argv.includes("connected")) await connected;
    sdk.event("Before shutdown");
    setTimeout(
      () =>
        process.argv.includes("signal")
          ? process.kill(process.pid, "SIGTERM")
          : app.quit(),
      200,
    );
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
