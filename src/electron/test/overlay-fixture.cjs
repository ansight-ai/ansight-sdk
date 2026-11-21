"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_OVERLAY_FIXTURE_DIR;
if (!root) throw new Error("ANSIGHT_OVERLAY_FIXTURE_DIR required");
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
      'data:text/html,<style>body{background:white}button{position:fixed;left:100px;top:100px;width:150px;height:80px}</style><div style="position:fixed;left:400px;top:100px;width:100px;height:80px;background:rgb(0,255,0)"></div><button data-testid="target" onclick="this.textContent=\'Clicked\'">Target</button>',
    );
    const sdk = createAnsight({
      appId: "com.ansight.overlay.fixture",
      enabled: false,
      toolGuard: "fullAccess",
    });
    sdk.attachWindow(w);
    const call = async (toolId, args = {}) =>
      (await sdk.execute({ toolId, arguments: args }, { transfers: [] }))
        .result;
    const checks = [];
    const query = await call("ui.query_nodes", { automationId: "target" });
    const id = query.matches[0].id;
    const shown = await call("ui.show_overlay", {
      nodeId: id,
      overlayId: "fixture",
      fillColor: "#FF00FF00",
      strokeColor: "red",
      durationMs: 0,
      metadata: { purpose: "fixture" },
    });
    assert.equal(shown.overlay.inputTransparent, true);
    assert.equal(shown.overlay.rects[0].x, 100);
    checks.push("node-anchored geometry and native ARGB colors");
    await new Promise((r) => setTimeout(r, 80));
    const image = await w.webContents.capturePage();
    fs.writeFileSync(path.join(root, "overlay.png"), image.toPNG());
    const size = image.getSize(),
      scale =
        size.width / (await w.webContents.executeJavaScript("innerWidth")),
      pixel = image
        .getBitmap()
        .subarray(
          (Math.round(130 * scale) * size.width + Math.round(140 * scale)) * 4,
        );
    const reference = image
      .getBitmap()
      .subarray(
        (Math.round(130 * scale) * size.width + Math.round(430 * scale)) * 4,
      );
    for (let i = 0; i < 4; i++)
      assert.ok(Math.abs(pixel[i] - reference[i]) <= 2);
    assert.ok(pixel[1] > pixel[0] && pixel[1] > pixel[2]);
    checks.push("opaque green overlay present in captured pixels");
    assert.equal(
      (
        await call("ui.query_overlays", {
          metadataKey: "purpose",
          metadataValue: "FIXTURE",
        })
      ).count,
      1,
    );
    checks.push("metadata query");
    const updated = await call("ui.update_overlay", {
      overlayId: "fixture",
      strokeWidth: 4,
      metadata: { reviewed: true },
    });
    assert.equal(updated.overlay.style.strokeWidth, 4);
    assert.equal(updated.overlay.metadata.purpose, "fixture");
    checks.push("partial update preserves geometry and merges metadata");
    const tree = await call("ui.get_visual_tree", {});
    assert.ok(!JSON.stringify(tree).includes("canvas"));
    checks.push("diagnostic overlay excluded from app visual tree");
    await call("ui.perform_action", { nodeId: id, action: "tap" });
    await call("ui.wait", {
      automationId: "target",
      textContains: "Clicked",
      condition: "exists",
      timeoutMilliseconds: 1000,
    });
    checks.push("native click passes through overlay");
    assert.equal(
      (await call("ui.get_overlay", { overlayId: "fixture" })).overlay.id,
      "fixture",
    );
    assert.equal(
      (await call("ui.remove_overlay", { overlayId: "fixture" })).removed,
      true,
    );
    assert.equal(
      (await call("ui.remove_overlay", { overlayId: "fixture" })).removed,
      false,
    );
    checks.push("get and idempotent removal");
    await call("ui.show_overlay", {
      x: 20,
      y: 20,
      width: 80,
      height: 40,
      durationMs: 30,
      overlayId: "expired",
    });
    await new Promise((r) => setTimeout(r, 80));
    assert.equal((await call("ui.query_overlays", {})).count, 0);
    checks.push("automatic expiration");
    await call("ui.show_overlay", {
      x: 20,
      y: 20,
      width: 80,
      height: 40,
      durationMs: 0,
      metadata: { group: "clear" },
    });
    assert.equal(
      (
        await call("ui.clear_overlays", {
          metadataKey: "group",
          metadataValue: "clear",
        })
      ).count,
      1,
    );
    checks.push("filtered clear");
    await call("ui.show_overlay", {
      x: 20,
      y: 20,
      width: 80,
      height: 40,
      durationMs: 0,
    });
    await sdk.stop();
    assert.equal(
      await w.webContents.executeJavaScriptInIsolatedWorld(1077, [
        { code: "globalThis.__ansightOverlayState.items.size" },
      ]),
      0,
    );
    checks.push("SDK stop removes persistent overlays");
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify({ passed: true, checks }, null, 2),
    );
    console.log(JSON.stringify({ passed: true, checks }));
    clearTimeout(timer);
    app.quit();
  })
  .catch((error) => {
    console.error(error);
    app.exit(1);
  });
