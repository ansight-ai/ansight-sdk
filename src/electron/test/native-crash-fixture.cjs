"use strict";
const { app } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_NATIVE_CRASH_DIR;
if (!root) throw new Error("ANSIGHT_NATIVE_CRASH_DIR required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const mode = process.argv.includes("crash") ? "crash" : "recover";
const timeout = setTimeout(() => app.exit(2), 20000);
app
  .whenReady()
  .then(async () => {
    const sdk = createAnsight({
      appId: "com.ansight.electron.native-crash.fixture",
      enabled: true,
      discoveryPort: 47123,
      sessionJpegCapture: false,
      crashCapture: { nativeSignalCapture: true },
    });
    await sdk.start();
    while (!sdk.connected) await new Promise((r) => setTimeout(r, 100));
    fs.writeFileSync(
      path.join(root, mode + ".json"),
      JSON.stringify(
        {
          processSessionId: sdk.processSessionId,
          pid: process.pid,
          crashDumps: app.getPath("crashDumps"),
          upload: require("electron").crashReporter.getUploadToServer(),
        },
        null,
        2,
      ),
    );
    await new Promise((r) => setTimeout(r, 1500));
    if (mode === "crash") process.crash();
    else {
      await sdk.crashes.deliver();
      fs.writeFileSync(
        path.join(root, "recovered.json"),
        JSON.stringify({
          pendingReports: sdk.crashes.files().length,
          nativeSources: sdk.nativeCrashes.sources.size,
        }),
      );
      await sdk.stop();
      clearTimeout(timeout);
      app.quit();
    }
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
