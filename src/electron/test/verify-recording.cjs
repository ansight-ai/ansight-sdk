const { execFileSync } = require("node:child_process");
const fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const cli =
  process.env.ANSIGHT_CLI ||
  path.resolve(
    "../../../electron-sdk-host/src/Ansight.Cli/bin/Debug/net10.0/ansight",
  );
const data =
  process.env.ANSIGHT_DATA_DIR || "/private/tmp/ansight-electron-host";
const out =
  process.env.ANSIGHT_EVIDENCE_DIR || "/private/tmp/ansight-electron-complete";
fs.mkdirSync(out, { recursive: true });
const run = (...args) =>
  JSON.parse(
    execFileSync(
      cli,
      [
        ...args,
        "--data-dir",
        data,
        "--secret-store-file",
        path.join(data, "test-secrets.json"),
        "--secret-key-file",
        path.join(data, "test-secret.key"),
        "--json",
      ],
      { encoding: "utf8", maxBuffer: 32 * 1024 * 1024, timeout: 45000 },
    ),
  );
const sessions = run(
  "session",
  "list",
  "--connected",
  "--app-id",
  "com.ansight.electron.recording-fixture",
  "--limit",
  "10",
).sessions;
assert.equal(sessions.length, 1);
const id = sessions[0].sessionId,
  checks = [];
function save(name, value) {
  fs.writeFileSync(
    path.join(out, name + ".json"),
    JSON.stringify(value, null, 2),
  );
  return value;
}
function check(name, fn) {
  fn();
  checks.push(name);
  console.log("PASS", name);
}
function call(tool, args = {}, extra = []) {
  const j = run(
    "app",
    "call",
    id,
    tool,
    "--arguments",
    JSON.stringify(args),
    ...extra,
  );
  assert.ok(j.isSuccess, JSON.stringify(j));
  return j.envelope.payload;
}
const fixture = (action) => call("fixture.control", { action }).result;
const tree = () => call("ui.get_visual_tree").result;
const flatten = (n) => [n, ...(n.children || []).flatMap(flatten)];
function ui(command, args = []) {
  const j = run("ui", command, "--session", id, ...args);
  assert.ok(j.succeeded, JSON.stringify(j));
  return j.result;
}
const pause = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  for (const tool of [
    "electron.get_windows",
    "ui.get_visual_tree",
    "dom.get_document",
    "ui.get_screenshot",
    "ui.perform_action",
    "electron.input",
    "ui.dismiss_keyboard",
    "ui.submit_keyboard",
    "electron.get_capabilities",
    "electron.get_metrics",
    "fixture.control",
  ])
    assert.ok(
      run("app", "tools", id, "--tool-id", tool, "--detail", "full").isSuccess,
    );
  fixture("reload");
  fixture("resetZoom");
  check("window and capabilities", () => {
    assert.equal(call("electron.get_windows").result.windows.length, 1);
    assert.ok(
      call("electron.get_capabilities").result.streams.includes("metrics"),
    );
  });
  const before = tree();
  check("DOM and privacy", () => {
    assert.ok(before.root);
    assert.ok(!JSON.stringify(before).includes("fixture-secret"));
    assert.equal(fixture("state").nodeAccess, "undefined");
    assert.ok(call("dom.get_document").result.root);
  });
  const typed = save(
    "standard-type",
    ui("type", ["--automation-id", "name", "--value", "Native input verified"]),
  );
  check("standard semantic typing", () => {
    assert.equal(fixture("state").value, "Native input verified");
    assert.ok(typed.evidence.after);
  });
  ui("tap", ["--automation-id", "save"]);
  check("standard tap submits actual value", () =>
    assert.equal(fixture("state").submitted, "Native input verified"),
  );
  call("ui.perform_action", { automationId: "name", action: "focus" });
  call("electron.input", {
    kind: "type",
    text: "Enter key verified",
    replaceExisting: true,
  });
  call("electron.input", { kind: "key", key: "Enter" });
  check("native typing and Enter", () =>
    assert.equal(fixture("state").submitted, "Enter key verified"),
  );
  call("ui.perform_action", {
    automationId: "name",
    action: "setValue",
    value: "Form submit verified",
  });
  call("ui.submit_keyboard");
  check("form submit", () =>
    assert.equal(fixture("state").submitted, "Form submit verified"),
  );
  call("ui.dismiss_keyboard");
  check("keyboard dismissal", () =>
    assert.notEqual(fixture("state").focused, "name"),
  );
  call("ui.perform_action", {
    automationId: "name",
    action: "typeText",
    text: "!",
  });
  check("semantic append", () =>
    assert.equal(fixture("state").value, "Form submit verified!"),
  );
  const p = flatten(tree().root).find((n) => n.automationId === "count").bounds;
  call("electron.input", {
    kind: "tap",
    x: p.x + p.width / 2,
    y: p.y + p.height / 2,
  });
  const count = Number(fixture("state").count);
  call("electron.input", {
    kind: "longPress",
    x: p.x + p.width / 2,
    y: p.y + p.height / 2,
    durationMs: 550,
  });
  check("native tap and long press", () =>
    assert.equal(Number(fixture("state").count), count + 1),
  );
  const scroll = flatten(tree().root).find(
    (n) => n.automationId === "scroll",
  ).bounds;
  const point = {
    x: scroll.x + scroll.width / 2,
    y: scroll.y + scroll.height / 2,
  };
  call("electron.input", { kind: "scroll", ...point, deltaY: -160 });
  await pause(150);
  check("wheel scroll changes scroll position", () =>
    assert.ok(fixture("state").scroll > 0),
  );
  const previous = fixture("state").scroll;
  call("electron.input", {
    kind: "swipe",
    ...point,
    endX: point.x,
    endY: point.y - 80,
    durationMs: 200,
  });
  await pause(150);
  check("swipe scroll", () => assert.ok(fixture("state").scroll > previous));
  call("electron.input", { kind: "pinch", x: 400, y: 250, scale: 1.25 });
  check("pinch gesture changes viewport scale", () =>
    assert.ok(fixture("state").scale > 1),
  );
  call("electron.input", {
    kind: "pinch",
    x: 400,
    y: 250,
    scale: 1 / fixture("state").scale,
  });
  fixture("zoom");
  const countAtZoom = Number(fixture("state").count);
  call("ui.perform_action", { automationId: "count", action: "tap" });
  check("semantic tap respects page zoom", () =>
    assert.equal(Number(fixture("state").count), countAtZoom + 1),
  );
  fixture("resetZoom");
  for (const format of ["png", "jpeg"]) {
    const image = call("ui.get_screenshot", { format, maxWidth: 600 }).result;
    check(format + " binary screenshot", () => {
      assert.equal(image.status, "complete");
      assert.ok(image.width <= 600);
      assert.equal(fs.statSync(image.artifactPath).size, image.sizeBytes);
    });
  }
  const after = call(
    "ui.perform_action",
    { automationId: "count", action: "tap" },
    ["--after-tree", "--after-screenshot"],
  );
  check("post-action evidence", () => {
    assert.ok(after.evidence.visualTree.success);
    assert.equal(after.evidence.screenshot.result.status, "complete");
  });
  const batch = run(
    "app",
    "batch",
    id,
    "--calls",
    JSON.stringify([
      { toolId: "electron.get_metrics", arguments: {} },
      { toolId: "ui.get_visual_tree", arguments: {} },
    ]),
  );
  check("ordered batch", () =>
    assert.equal(batch.envelope.payload.completed, 2),
  );
  call("ui.perform_action", { automationId: "next", action: "tap" });
  await pause(150);
  check("navigation", () => assert.equal(fixture("state").url, "/next"));
  ui("back");
  check("standard Back", () => assert.equal(fixture("state").url, "/"));
  fixture("emit");
  fixture("busy");
  await pause(1500);
  fixture("hide");
  await pause(1200);
  const hidden = run("session", "show", id);
  save("fixture-hidden", hidden);
  fixture("show");
  await pause(1200);
  const snapshot = save("fixture-recording", run("session", "show", id));
  check("lifecycle and typed session properties", () => {
    assert.notEqual(hidden.session.appState, snapshot.session.appState);
    assert.deepEqual(snapshot.session.customProperties.fixture, {
      marker: "recording-contract",
      number: 42,
      flag: true,
    });
  });
  check("runtime metadata survives ingestion", () =>
    assert.ok(
      snapshot.session.deviceProfile.runtime.stack.some(
        (r) => r.name === "Electron",
      ),
    ),
  );
  const metrics = save(
    "fixture-metrics",
    run("session", "metrics", id, "--limit", "1000"),
  );
  check("metric streams and units", () => {
    assert.deepEqual(metrics.channels.filter(c=>c.channelId>=100 && c.channelId<=105).map(c=>c.channelId).sort((a,b)=>a-b),[100,101,102,103,104,105]);
    for (const channel of metrics.channels.filter(c=>c.channelId>=100 && c.channelId<=105))
      assert.ok(
        metrics.samples.some(
          (s) => s.channelId === channel.channelId && Number.isFinite(s.value),
        ),
      );
  });
  const network = save(
    "fixture-network",
    run("session", "network", id, "--limit", "100"),
  );
  check("network redirects, bytes, failures, and redaction", () => {
    const requests = network.requests;
    assert.ok(requests.some((r) => r.statusCode === 302));
    assert.ok(
      requests.some(
        (r) =>
          r.url.endsWith("/body") &&
          r.statusCode === 200 &&
          r.responseBodySizeBytes === 128,
      ),
    );
    assert.ok(requests.some((r) => r.statusCode === 503));
    assert.ok(requests.some((r) => r.errorType));
    assert.ok(!JSON.stringify(requests).includes("fixture-secret"));
  });
  const touches = save(
    "fixture-touches",
    run("session", "touches", id, "--limit", "1000"),
  );
  check("observed pointer stream coordinates", () => {
    assert.ok(touches.items.some((t) => t.action === "down"));
    assert.ok(touches.items.some((t) => t.action === "up"));
    assert.ok(
      touches.items.every(
        (t) =>
          Number.isFinite(t.normalizedX) &&
          Number.isFinite(t.normalizedY) &&
          t.surfaceWidth > 0,
      ),
    );
  });
  const trees = save(
    "fixture-trees",
    run("session", "trees", id, "--limit", "100"),
  );
  check("periodic trees linked to screenshots", () =>
    assert.ok(
      trees.items.some(
        (t) =>
          t.source === "electron.sdk" &&
          t.screenshotCapturedAtUtc &&
          t.nodeCount > 0,
      ),
    ),
  );
  const logs = save(
    "fixture-logs",
    run("session", "logs", id, "--limit", "100"),
  );
  check("warning and error records retain severity", () => {
    assert.ok(
      logs.logs.some(
        (l) => l.message.includes("fixture.warning") && l.priority === 3,
      ),
    );
    assert.ok(
      logs.logs.some(
        (l) => l.message.includes("fixture.console.error") && l.priority >= 4,
      ),
    );
    assert.ok(
      logs.logs.some((l) => l.message.includes("fixture.renderer.exception")),
    );
    assert.ok(
      logs.logs.some((l) => l.message.includes("fixture.renderer.rejection")),
    );
  });
  const artifacts = save(
    "fixture-artifacts",
    run("session", "artifacts", id, "--limit", "20"),
  );
  check("app artifact stored", () =>
    assert.ok(
      artifacts.items.some(
        (a) => a.relativePath === "fixture.bytes" && a.byteCount === 21,
      ),
    ),
  );
  check("artifact bytes preserved", () => {
    const artifact = artifacts.items.find(
      (a) => a.relativePath === "fixture.bytes",
    );
    assert.equal(
      fs.readFileSync(
        path.join(
          data,
          "data/session-captures/com.ansight.electron.recording-fixture",
          id,
          "artifacts",
          artifact.artifactDirectoryName,
          "known.txt",
        ),
        "utf8",
      ),
      "known artifact bytes\n",
    );
  });
  check("application and lifecycle events", () => {
    assert.ok(
      logs.logs.some(
        (l) => l.message.includes("fixture.event") && l.message.includes("42"),
      ),
    );
    assert.ok(logs.logs.some((l) => l.message.includes("lifecycle.background")));
  });
  const report = { passed: true, session: id, checks };
  save("recording-report", report);
  console.log(JSON.stringify(report, null, 2));
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
