"use strict";
const electron = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_STORAGE_FIXTURE_DIR;
if (!root) throw new Error("ANSIGHT_STORAGE_FIXTURE_DIR required");
fs.mkdirSync(root, { recursive: true });
electron.app.setPath("userData", path.join(root, "user-data"));
const timer = setTimeout(() => electron.app.exit(2), 20000);
electron.app
  .whenReady()
  .then(async () => {
    const w = new electron.BrowserWindow({
      show: false,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    const file = path.join(root, "fixture.html");
    fs.writeFileSync(
      file,
      "<!doctype html><title>Storage verification</title>",
    );
    await w.loadFile(file);
    const sdk = createAnsight({
      appId: "com.ansight.storage.fixture",
      enabled: false,
      toolGuard: "fullAccess",
    });
    sdk.attachWindow(w);
    const call = async (toolId, args = {}) =>
      (await sdk.execute({ toolId, arguments: args }, { transfers: [] }))
        .result;
    await call("prefs.set_value", {
      key: "ansight-test",
      value: "Résumé ✓",
      valueType: "string",
    });
    assert.equal(
      await w.webContents.executeJavaScript(
        'localStorage.getItem("ansight-test")',
      ),
      "Résumé ✓",
    );
    assert.equal(
      (await call("prefs.get_value", { key: "ansight-test" })).value,
      "Résumé ✓",
    );
    assert.equal(
      (
        await call("prefs.get_value", {
          key: "ansight-test",
          store: "sessionStorage",
        })
      ).exists,
      false,
    );
    await w.reload();
    await new Promise((resolve) =>
      w.webContents.once("did-finish-load", resolve),
    );
    assert.equal(
      (await call("prefs.get_value", { key: "ansight-test" })).value,
      "Résumé ✓",
    );
    assert.deepEqual(
      (await call("prefs.list_keys", { prefix: "ansight-" })).keys,
      ["ansight-test"],
    );
    assert.equal(
      (await call("prefs.remove_key", { key: "ansight-test" })).removed,
      true,
    );
    assert.equal(
      await w.webContents.executeJavaScript(
        'localStorage.getItem("ansight-test")',
      ),
      null,
    );
    assert.equal(typeof (await call("clipboard.has_text")).hasText, "boolean");
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify(
        {
          passed: true,
          checks: [
            "actual localStorage write observed by app",
            "native get contract",
            "session storage separation",
            "persistence across reload",
            "key discovery",
            "deletion observed by app",
            "native clipboard availability",
          ],
          clipboardMutationTested: false,
        },
        null,
        2,
      ),
    );
    await sdk.stop();
    clearTimeout(timer);
    electron.app.quit();
  })
  .catch((error) => {
    console.error(error);
    electron.app.exit(1);
  });
