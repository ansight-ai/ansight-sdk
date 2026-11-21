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
      'data:text/html,<main><button data-testid="save" onclick="this.textContent=\'Saved\'">Save</button><input type="password" value="never-export"></main>',
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
    const query = await call("ui.query_nodes", {
      automationId: "save",
      role: "button",
      action: "tap",
      visible: true,
    });
    assert.equal(query.count, 1);
    checks.push("query semantic filters and reference");
    const inspected = await call("ui.inspect_node", {
      reference: query.matches[0].reference,
      includeAncestors: true,
    });
    assert.equal(inspected.node.automationId, "save");
    assert.ok(inspected.ancestors.length);
    checks.push("snapshot-scoped node and ancestor inspection");
    await call("ui.perform_action", {
      reference: query.matches[0].reference,
      action: "tap",
    });
    await call("ui.wait", {
      automationId: "save",
      textContains: "Saved",
      condition: "exists",
      timeoutMilliseconds: 2000,
    });
    assert.equal(
      await w.webContents.executeJavaScript(
        'document.querySelector("button").textContent',
      ),
      "Saved",
    );
    checks.push("reference delivers real native input");
    await call("ui.query_nodes", {});
    await assert.rejects(
      call("ui.inspect_node", { reference: query.matches[0].reference }),
      /superseded/,
    );
    checks.push("superseded references rejected");
    const secret = await call("ui.query_nodes", { role: "textbox" });
    assert.equal(secret.count, 1);
    assert.ok(!JSON.stringify(secret).includes("never-export"));
    checks.push("private text remains redacted");
    await w.webContents.executeJavaScript(
      'setTimeout(()=>{const b=document.createElement("button");b.id="later";b.textContent="Later";document.body.append(b)},120)',
    );
    const waited = await call("ui.wait", {
      automationId: "later",
      condition: "visible",
      timeoutMilliseconds: 2000,
      pollMilliseconds: 25,
    });
    assert.equal(waited.matched, true);
    checks.push("wait sees asynchronously appearing node");
    await w.webContents.executeJavaScript(
      'document.getElementById("later").remove()',
    );
    await assert.rejects(
      call("ui.inspect_node", { reference: waited.query.matches[0].reference }),
      /no longer/,
    );
    checks.push("removed live node rejected");
    assert.equal(
      (
        await call("ui.wait", {
          automationId: "later",
          condition: "notExists",
          timeoutMilliseconds: 100,
        })
      ).matched,
      true,
    );
    checks.push("notExists wait succeeds only on complete snapshot");
    await assert.rejects(
      call("ui.wait", {
        automationId: "never",
        condition: "exists",
        timeoutMilliseconds: 50,
        pollMilliseconds: 10,
      }),
      /timed out/,
    );
    checks.push("unsatisfied wait reports failure");
    await w.webContents.executeJavaScript(
      'const panel=document.createElement("section");panel.id="panel";panel.innerHTML=`<div><button id="deep">Deep</button></div><input id="secret-prop" type="password" value="private-property-value"><div id="hidden" style="display:none">Hidden</div>`;document.body.append(panel)',
    );
    const tree = await call("ui.get_visual_tree", {
      includeBounds: false,
      includeComputedStyles: true,
      includeProperties: true,
    });
    const flatten = (n) =>
      n ? [n, ...(n.children || []).flatMap(flatten)] : [];
    assert.ok(flatten(tree.root).every((n) => !Object.hasOwn(n, "bounds")));
    assert.ok(tree.root.computedStyles.display);
    assert.ok(tree.root.properties.tagName);
    assert.ok(!JSON.stringify(tree).includes("private-property-value"));
    checks.push("bounds/style/property options with private-value redaction");
    const panel = flatten(tree.root).find((n) => n.automationId === "panel");
    const subtree = await call("ui.get_visual_tree", {
      rootNodeId: panel.id,
      maxDepth: 0,
    });
    assert.equal(subtree.root.id, panel.id);
    assert.equal(subtree.root.children.length, 0);
    assert.equal(subtree.truncated, true);
    checks.push("subtree root and depth truncation");
    await assert.rejects(
      call("ui.wait", {
        automationId: "deep",
        condition: "notExists",
        maxDepth: 0,
        timeoutMilliseconds: 30,
        pollMilliseconds: 10,
      }),
      /timed out/,
    );
    checks.push("depth-limited tree cannot prove absence");
    const hidden = await call("ui.query_nodes", {
      automationId: "hidden",
      includeInactivePages: true,
    });
    assert.equal(hidden.count, 1);
    assert.equal(hidden.matches[0].visible, false);
    checks.push("inactive DOM nodes inspectable without claiming visibility");
    const target = await call("ui.query_nodes", {
      automationId: "secret-prop",
    });
    const details = await call("ui.inspect_node", {
      reference: target.matches[0].reference,
      includeProperties: true,
    });
    assert.equal(details.node.properties.inputType, "password");
    assert.ok(!JSON.stringify(details).includes("private-property-value"));
    checks.push("inspect-node properties respect privacy");
    await assert.rejects(
      call("ui.get_visual_tree", { rootNodeId: "missing" }),
      /no longer/,
    );
    checks.push("invalid subtree rejected");
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
