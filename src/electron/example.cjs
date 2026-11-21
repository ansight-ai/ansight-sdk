// In your Electron main process, after app.whenReady():
const { app, BrowserWindow } = require("electron");
const { createAnsight } = require("@ansight/electron");
app.whenReady().then(async () => {
  const ansight = createAnsight({
    appId: "com.example.desktop",
    appName: "Example Desktop",
    enabled: !app.isPackaged && process.env.ANSIGHT_ENABLED === "1",
    allowActions: true,
    captureNetwork: true,
  });
  const window = new BrowserWindow({
    webPreferences: { contextIsolation: true, sandbox: true },
  });
  ansight.attachWindow(window);
  ansight.on("diagnostic", (message) => console.warn("[Ansight]", message));
  await ansight.start();
  await window.loadFile("index.html");
});
// Start the Ansight host, then launch with ANSIGHT_ENABLED=1.
// Discover tools: ansight app tools <session-id> --query electron --json
// Read DOM: ansight app call <session-id> ui.get_visual_tree --arguments '{}' --json
// Input: ui.perform_action using observed nodeId/automationId and action.
// Screenshots use native capturePage plus the existing ASFT/ASJP protocols.
// This package does not expose Node or IPC to the renderer.
// Network capture is optional, uses CDP, and skips a pre-attached debugger.
// Bodies, headers, URL credentials, query strings and fragments are not captured.
// Password/private DOM values are omitted; screenshots may still show app content.
// Host versions with the Electron input adapter also support standard ui tap/type commands.
