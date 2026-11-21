"use strict";
const electron = require("electron");
const fs = require("node:fs"),
  path = require("node:path");
const { WindowVideoRecorder } = require("../lib/video");
const root = process.env.ANSIGHT_VIDEO_FIXTURE_DIR;
if (!root) throw new Error("ANSIGHT_VIDEO_FIXTURE_DIR required");
fs.mkdirSync(root, { recursive: true });
electron.app.setPath("userData", path.join(root, "user-data"));
const timeout = setTimeout(() => electron.app.exit(2), 30000);
electron.app
  .whenReady()
  .then(async () => {
    const target = new electron.BrowserWindow({
      width: 640,
      height: 480,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    await target.loadURL(
      'data:text/html,<h1>Ansight native video fixture</h1><div id="counter"></div><script>setInterval(()=>{document.getElementById("counter").textContent=Date.now();document.body.style.background=`hsl(${Date.now()/10%360},70%,80%)`},100)</script>',
    );
    const video = new WindowVideoRecorder({ electron, window: () => target });
    const before = target.webContents.getBackgroundThrottling();
    const started = await video.start();
    await new Promise((resolve) => setTimeout(resolve, 2200));
    const context = { requestId: "video-fixture", transfers: [] };
    const result = await video.stop(
      { recordingId: started.recordingId },
      context,
    );
    fs.writeFileSync(
      path.join(root, "native.webm"),
      context.transfers[0].bytes,
    );
    const restored = target.webContents.getBackgroundThrottling() === before;
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify(
        { started, result, throttlingRestored: restored },
        null,
        2,
      ),
    );
    if (!restored) throw new Error("App throttling was not restored");
    console.log(JSON.stringify(result));
    clearTimeout(timeout);
    electron.app.quit();
  })
  .catch((error) => {
    console.error(error);
    electron.app.exit(1);
  });
