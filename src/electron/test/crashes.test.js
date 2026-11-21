"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  fs = require("node:fs"),
  os = require("node:os"),
  path = require("node:path");
const { spawnSync } = require("node:child_process");
const { CrashCapture } = require("../lib/crashes");
function setup(t, options = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "ansight-crash-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const sdk = {
    options: { appId: "crash.test", crashCapture: options },
    processSessionId: "original",
    connected: false,
    diagnostic: () => {},
  };
  const capture = new CrashCapture(sdk, root);
  t.after(() => capture.stop());
  return { root, sdk, capture };
}
test("fatal exception persists evidence while Node follows its normal exit path", (t) => {
  const { root, capture } = setup(t);
  capture.stop();
  const child = spawnSync(
    process.execPath,
    [
      "-e",
      `
    const {CrashCapture}=require(${JSON.stringify(require.resolve("../lib/crashes"))});
    const c=new CrashCapture({options:{appId:"crash.test"},processSessionId:"crashing-child",diagnostic:()=>{}},${JSON.stringify(root)});
    c.breadcrumb("Event","before fatal");throw new Error("intentional-crash-evidence");
  `,
    ],
    { encoding: "utf8" },
  );
  assert.equal(child.status, 1);
  const report = JSON.parse(fs.readFileSync(capture.files()[0].path));
  assert.equal(report.previousProcessSessionId, "crashing-child");
  assert.equal(report.breadcrumbs[0].label, "before fatal");
  assert.match(
    Buffer.from(report.traceBase64, "base64").toString(),
    /intentional-crash-evidence/,
  );
});
test("failed handoff retains evidence; acknowledged retry deletes it", async (t) => {
  const { sdk, capture } = setup(t);
  capture.record("renderer_crash", new Error("boom"));
  sdk.processSessionId = "healthy";
  sdk.connected = true;
  sdk.control = async () => {
    throw new Error("lost connection");
  };
  await capture.deliver();
  assert.equal(capture.files().length, 1);
  sdk.control = async (action, p) => {
    assert.equal(action, "crash.handoff");
    assert.equal(p.targetProcessSessionId, "original");
    assert.equal(p.deliveryProcessSessionId, "healthy");
  };
  await capture.deliver();
  assert.equal(capture.files().length, 0);
});
test("crash evidence obeys count, trace, breadcrumb and disabled limits", (t) => {
  const { capture } = setup(t, {
    maximumPendingReports: 2,
    maximumBreadcrumbs: 1,
    maximumTraceBytes: 16384,
  });
  capture.breadcrumb("Event", "old");
  capture.breadcrumb("Event", "new");
  for (let i = 0; i < 4; i++)
    capture.record("renderer_crash", {
      stack: "x".repeat(20000),
      message: "boom",
    });
  assert.equal(capture.files().length, 2);
  const report = JSON.parse(fs.readFileSync(capture.files()[0].path));
  assert.equal(report.breadcrumbs.length, 1);
  assert.equal(report.breadcrumbs[0].label, "new");
  assert.equal(Buffer.from(report.traceBase64, "base64").length, 16384);
  const disabled = setup(t, { enabled: false });
  disabled.capture.record("renderer_crash", new Error());
  assert.equal(fs.existsSync(path.join(disabled.root, "crashes")), false);
});
