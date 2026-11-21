"use strict";
const assert = require("node:assert/strict"),
  fs = require("node:fs"),
  path = require("node:path"),
  { execFileSync } = require("node:child_process");
const cli = process.env.ANSIGHT_CLI || "/private/tmp/ansight-electron-cli";
const run = (...args) =>
  JSON.parse(
    execFileSync(cli, [...args, "--json"], {
      encoding: "utf8",
      maxBuffer: 16 * 1024 * 1024,
    }),
  );
(async () => {
  const sessions = run(
    "session",
    "list",
    "--connected",
    "--app-id",
    "com.ansight.electron.recording-fixture",
  ).sessions;
  assert.equal(sessions.length, 1);
  const id = sessions[0].sessionId;
  const before = run("session", "trees", id).items;
  assert.equal(
    before.filter(
      (t) => t.source === "electron.sdk" || t.source === "sdk.touchCapture",
    ).length,
    0,
  );
  const action = run(
    "app",
    "call",
    id,
    "ui.perform_action",
    "--arguments",
    JSON.stringify({ automationId: "count", action: "tap" }),
  );
  assert.equal(action.isSuccess, true);
  await new Promise((r) => setTimeout(r, 1200));
  const trees = run("session", "trees", id).items;
  assert.ok(trees.some((t) => t.source === "sdk.touchCapture"));
  assert.equal(
    trees.some((t) => t.source === "electron.sdk"),
    false,
  );
  const touches = run("session", "touches", id).items;
  assert.ok(touches.some((t) => t.action === "down"));
  assert.ok(touches.some((t) => t.action === "up"));
  const dir = path.join(
    process.env.ANSIGHT_DATA_DIR || "/private/tmp/ansight-electron-host",
    "data/session-captures/com.ansight.electron.recording-fixture",
    id,
    "visual-trees",
  );
  const captured = fs
    .readdirSync(dir)
    .filter((f) => f.endsWith(".json"))
    .map((f) => JSON.parse(fs.readFileSync(path.join(dir, f))));
  const findTrigger = (o) => {
    if (!o || typeof o !== "object") return null;
    if (o.captureTrigger?.kind === "touch") return o.captureTrigger;
    for (const v of Object.values(o)) {
      const found = findTrigger(v);
      if (found) return found;
    }
    return null;
  };
  const triggers = captured.map(findTrigger).filter(Boolean);
  assert.ok(triggers.length);
  assert.ok(
    triggers.every(
      (t) =>
        t.gestureId.startsWith("gesture-") &&
        touches.some(
          (p) =>
            Date.parse(p.capturedAtUtc) === Date.parse(t.touchCapturedAtUtc),
        ),
    ),
  );
  const images = run("session", "images", id).items;
  assert.ok(images.length > 0);
  const report = {
    passed: true,
    sessionId: id,
    touchTrees: triggers.length,
    periodicScreenshots: images.length,
    checks: [
      "no idle periodic tree",
      "observed input triggers tree",
      "original touch timestamp and gesture identity",
      "periodic screenshots continue",
    ],
  };
  fs.writeFileSync(
    "/private/tmp/ansight-electron-complete/touch-tree-report.json",
    JSON.stringify(report, null, 2),
  );
  console.log(report);
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
