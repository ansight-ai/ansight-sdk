"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_UI_FIXTURE_DIR;
if (!root) throw new Error("ANSIGHT_UI_FIXTURE_DIR required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timer = setTimeout(() => app.exit(2), 20000);
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
      "data:text/html," +
        encodeURIComponent(
          '<main><input id="check" type="checkbox"><input id="radio" type="radio"><input id="editor"><input id="readonly" readonly value="fixed"><div id="rich" contenteditable="true">old</div><select id="choice" onchange="document.body.dataset.choice=this.value"><option value="a">A</option><option value="b">B</option><option value="c" disabled>C</option></select><button id="tab" role="tab" aria-selected="false" onclick="this.setAttribute(\'aria-selected\',\'true\')">Tab</button><button id="plain">Plain</button></main>',
        ),
    );
    const sdk = createAnsight({
      appId: "com.ansight.ui.fixture",
      enabled: false,
      toolGuard: "fullAccess",
    });
    sdk.attachWindow(w);
    const call = async (toolId, args = {}) =>
      (await sdk.execute({ toolId, arguments: args }, { transfers: [] }))
        .result;
    const checks = [];
    const state = (code) => w.webContents.executeJavaScript(code);
    const act = (automationId, action, extra = {}) =>
      call("ui.perform_action", { automationId, action, ...extra });
    const wait = async (code) => {
      for (let i = 0; i < 50; i++) {
        if (await state(code)) return;
        await new Promise((r) => setTimeout(r, 20));
      }
      throw new Error("State did not converge: " + code);
    };
    const q = await call("ui.query_nodes", { automationId: "check" });
    assert.ok(q.matches[0].supportedActions.includes("toggle"));
    assert.ok(!q.matches[0].supportedActions.includes("typeText"));
    checks.push("checkbox advertises toggle, not text editing");
    await act("check", "toggle");
    await wait('document.getElementById("check").checked');
    await act("check", "toggle", { checked: true });
    assert.equal(await state('document.getElementById("check").checked'), true);
    await act("check", "toggle", { checked: false });
    await wait('!document.getElementById("check").checked');
    checks.push("native toggle and explicit idempotent checked state");
    await act("radio", "select");
    await wait('document.getElementById("radio").checked');
    checks.push("native radio selection");
    await act("choice", "select", { value: "b" });
    assert.equal(await state("document.body.dataset.choice"), "b");
    await act("choice", "select", { index: 0 });
    assert.equal(await state("document.body.dataset.choice"), "a");
    await assert.rejects(act("choice", "select", { index: 2 }), /unavailable/);
    await assert.rejects(
      act("choice", "select", { value: "missing" }),
      /missing/,
    );
    checks.push(
      "select by value/index with application change events and invalid-option rejection",
    );
    await act("tab", "selectTab");
    await wait(
      'document.getElementById("tab").getAttribute("aria-selected")==="true"',
    );
    checks.push("native tab activation");
    await act("editor", "focus");
    assert.equal(await state("document.activeElement.id"), "editor");
    await act("editor", "unfocus");
    assert.notEqual(await state("document.activeElement.id"), "editor");
    checks.push("focus and unfocus");
    await act("rich", "setValue", { value: "replacement" });
    assert.equal(
      await state('document.getElementById("rich").textContent'),
      "replacement",
    );
    checks.push("contenteditable replacement selects existing content");
    await assert.rejects(
      act("readonly", "setValue", { value: "changed" }),
      /not editable/,
    );
    await assert.rejects(act("plain", "toggle"), /support toggle/);
    await assert.rejects(act("plain", "selectTab"), /not a tab/);
    assert.equal(
      await state('document.getElementById("readonly").value'),
      "fixed",
    );
    checks.push("unsupported and readonly mutations rejected");
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify({ passed: true, checks }, null, 2),
    );
    console.log(JSON.stringify({ passed: true, checks }));
    await sdk.stop();
    clearTimeout(timer);
    app.quit();
  })
  .catch((error) => {
    console.error(error);
    app.exit(1);
  });
