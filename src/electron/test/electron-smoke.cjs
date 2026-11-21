const { app, BrowserWindow } = require("electron");
const assert = require("node:assert/strict");
const fs = require("node:fs"),
  os = require("node:os"),
  path = require("node:path");
const { createAnsight } = require("..");
app.setPath(
  "userData",
  fs.mkdtempSync(path.join(os.tmpdir(), "ansight-electron-fixture-")),
);
app.whenReady().then(async () => {
  const sdk = createAnsight({
    appId: "com.ansight.electron.fixture",
    enabled: true,
    allowActions: true,
  });
  const win = new BrowserWindow({
    width: 600,
    height: 400,
    webPreferences: {
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  sdk.attachWindow(win);
  try {
    const html =
      '<input id="name" aria-label="Name"><button id="save" onclick="document.getElementById(\'result\').textContent=document.getElementById(\'name\').value">Save</button><p id="result"></p><input type="password" value="secret"><div data-ansight-private><span>private text</span></div><button id="disabled" disabled>No</button>';
    const url = "data:text/html," + encodeURIComponent(html);
    await win.loadURL(url);
    const invoke = (id, args = {}) =>
      sdk.tools.get(id).handler(args, { requestId: "fixture", transfers: [] });
    const flatten = (n) => [n, ...n.children.flatMap(flatten)];
    const tree = await invoke("ui.get_visual_tree");
    assert.equal(tree.adapter, "@ansight/electron");
    assert.ok(!JSON.stringify(tree).includes("secret"));
    assert.ok(!JSON.stringify(tree).includes("private text"));
    assert.equal(
      await win.webContents.executeJavaScript("typeof require"),
      "undefined",
    );
    await invoke("ui.perform_action", {
      automationId: "name",
      action: "setValue",
      value: "Electron works",
    });
    await invoke("ui.perform_action", { automationId: "save", action: "tap" });
    await new Promise((r) => setTimeout(r, 100));
    assert.equal(
      await win.webContents.executeJavaScript(
        'document.getElementById("result").textContent',
      ),
      "Electron works",
    );
    await assert.rejects(
      invoke("ui.perform_action", { automationId: "disabled", action: "tap" }),
      /interactable/,
    );
    const context = { requestId: "fixture", transfers: [] };
    const screenshot = await sdk.screenshot({ maxWidth: 300 }, context);
    assert.ok(screenshot.width <= 300);
    assert.ok(context.transfers[0].bytes.length > 100);
    const previous = flatten(tree.root).find(
      (n) => n.automationId === "name",
    ).id;
    await win.loadURL(url);
    await invoke("ui.get_visual_tree");
    await assert.rejects(
      invoke("ui.perform_action", { nodeId: previous, action: "tap" }),
      /missing/,
    );
    sdk.stop();
    assert.equal(sdk.windows.size, 0);
    console.log(
      "PASS: sandboxed renderer, DOM privacy, typing, native click, disabled target, screenshot, stale IDs, cleanup",
    );
    win.destroy();
    app.quit();
  } catch (e) {
    console.error(e);
    win.destroy();
    app.exit(1);
  }
});
