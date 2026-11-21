"use strict";
const fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict"),
  { createHash } = require("node:crypto"),
  { execFileSync } = require("node:child_process");
const source = process.env.ANSIGHT_OFFLINE_FIXTURE_DIR,
  host = process.env.ANSIGHT_DATA_DIR || "/private/tmp/ansight-electron-host",
  cli = process.env.ANSIGHT_CLI || "/private/tmp/ansight-electron-cli";
const expected = JSON.parse(
    fs.readFileSync(path.join(source, "expected.json")),
  ),
  id = `offline-${expected.sourceSessionId}`;
const dir = path.join(
  host,
  "data/session-captures/com.ansight.electron.offline-fixture",
  id,
);
const query = (type) =>
  JSON.parse(
    execFileSync(cli, ["session", type, id, "--json"], {
      encoding: "utf8",
      maxBuffer: 16 * 1024 * 1024,
    }),
  );
const hashes = new Set(
  fs.readdirSync(path.join(dir, "images")).map((name) =>
    createHash("sha256")
      .update(fs.readFileSync(path.join(dir, "images", name)))
      .digest("hex"),
  ),
);
assert.ok(Object.keys(expected.files).length > 0);
for (const hash of Object.values(expected.files)) assert.ok(hashes.has(hash));
const metrics = query("metrics");
assert.deepEqual(metrics.channels.filter(c=>c.channelId>=100 && c.channelId<=105).map(c=>c.channelId).sort((a,b)=>a-b),[100,101,102,103,104,105]);
assert.ok(metrics.samples.length >= 30);
const session = JSON.parse(
  fs.readFileSync(path.join(dir, "session.json")),
).session;
assert.equal(session.processSessionId, expected.processSessionId);
assert.equal(session.customProperties.fixture.offline, true);
const trees = fs
  .readdirSync(path.join(dir, "visual-trees"))
  .filter((p) => p.endsWith(".json"));
assert.ok(trees.length > 0);
const touches = query("touches").items;
assert.ok(touches.some((t) => t.action === "down"));
assert.ok(touches.some((t) => t.action === "up"));
const network = query("network").requests;
assert.ok(
  network.some((r) => r.url.endsWith("/payload") && r.statusCode === 200),
);
assert.ok(network.every((r) => !r.url.includes("private=")));
const logs = query("logs").logs;
assert.ok(
  logs.some((l) => l.message.startsWith("offline.warning") && l.priority === 3),
);
assert.ok(logs.some((l) => l.message.startsWith("offline.before-connect")));
assert.equal(
  fs.readdirSync(path.join(source, "ansight/offline/sessions")).length,
  0,
);
const report = {
  passed: true,
  sessionId: id,
  metricSamples: metrics.samples.length,
  matchedScreenshotHashes: Object.keys(expected.files).length,
  trees: trees.length,
  pointers: touches.length,
  networkRequests: network.length,
  checks: [
    "original process identity",
    "custom properties",
    "metrics and channels",
    "exact screenshot bytes",
    "visual trees",
    "pointer down/up",
    "network status and URL redaction",
    "warning severity",
    "events",
    "purge after durable receipt",
  ],
};
const out =
  process.env.ANSIGHT_EVIDENCE_DIR || "/private/tmp/ansight-electron-complete";
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(
  path.join(out, "offline-report.json"),
  JSON.stringify(report, null, 2),
);
console.log(JSON.stringify(report, null, 2));
