"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  fs = require("node:fs"),
  os = require("node:os"),
  path = require("node:path"),
  { randomUUID } = require("node:crypto");
const {
    NativeCrashCapture,
    inspectMinidump,
  } = require("../lib/native-crashes"),
  { CrashCapture } = require("../lib/crashes");
function dump(pid) {
  const b = Buffer.alloc(112);
  b.write("MDMP");
  b.writeUInt32LE(2, 8);
  b.writeUInt32LE(32, 12);
  b.writeUInt32LE(1795250000, 20);
  b.writeUInt32LE(15, 32);
  b.writeUInt32LE(12, 36);
  b.writeUInt32LE(56, 40);
  b.writeUInt32LE(6, 44);
  b.writeUInt32LE(40, 48);
  b.writeUInt32LE(68, 52);
  b.writeUInt32LE(1, 60);
  b.writeUInt32LE(pid, 64);
  b.writeUInt32LE(1, 76);
  return b;
}
test("native parser validates stream boundaries and identifies process and exception", () => {
  assert.equal(inspectMinidump(dump(123)).pid, 123);
  assert.equal(inspectMinidump(dump(123)).exceptionCode, 1);
  assert.throws(() => inspectMinidump(Buffer.alloc(32)), /header/);
  const invalid = dump(123);
  invalid.writeUInt32LE(99999, 52);
  assert.throws(() => inspectMinidump(invalid), /stream/);
});
test("native recovery keeps dump until acknowledgment and preserves process identity and binary trace", async (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "ansight-native-test-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const previous = randomUUID(),
    folder = path.join(root, "native-crashpad", previous);
  fs.mkdirSync(path.join(folder, "pending"), { recursive: true });
  const pid = 2147483647;
  fs.writeFileSync(
    path.join(folder, "process.json"),
    JSON.stringify({
      appId: "native.test",
      processSessionId: previous,
      pid,
      startedAtUtc: new Date().toISOString(),
    }),
  );
  const source = path.join(folder, "pending", "abc.dmp");
  fs.writeFileSync(source, dump(pid));
  let uploads;
  const sdk = {
    options: { appId: "native.test", crashCapture: {} },
    processSessionId: randomUUID(),
    diagnostic: (e) => {
      throw e;
    },
    electron: {
      app: { setPath() {} },
      crashReporter: { start: (o) => (uploads = o.uploadToServer) },
    },
    connected: true,
    control: async () => {
      throw new Error("unacknowledged");
    },
  };
  sdk.crashes = new CrashCapture(sdk, root);
  t.after(() => sdk.crashes.stop());
  sdk.nativeCrashes = new NativeCrashCapture(sdk, root);
  assert.equal(uploads, false);
  const files = sdk.crashes.files();
  assert.equal(files.length, 1);
  const report = JSON.parse(fs.readFileSync(files[0].path));
  assert.equal(report.previousProcessSessionId, previous);
  assert.equal(report.termination.processKind, "main");
  assert.deepEqual(Buffer.from(report.traceBase64, "base64"), dump(pid));
  sdk.diagnostic = () => {};
  await sdk.crashes.deliver();
  assert.ok(fs.existsSync(source));
  assert.equal(sdk.crashes.files().length, 1);
  sdk.control = async () => ({ success: true });
  await sdk.crashes.deliver();
  assert.equal(fs.existsSync(source), false);
  assert.equal(sdk.crashes.files().length, 0);
});
test("native spool bounds count and bytes, expires old data, preserves active processes and rejects symlinks", (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "ansight-native-budget-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const spool = path.join(root, "native-crashpad");
  fs.mkdirSync(spool);
  const add = (pid, size, age = 0) => {
    const id = randomUUID(),
      dir = path.join(spool, id);
    fs.mkdirSync(path.join(dir, "pending"), { recursive: true });
    fs.writeFileSync(
      path.join(dir, "process.json"),
      JSON.stringify({
        appId: "native.test",
        processSessionId: id,
        pid,
        startedAtUtc: new Date(Date.now() - age).toISOString(),
      }),
    );
    const file = path.join(dir, "pending", "abc.dmp");
    fs.writeFileSync(file, Buffer.alloc(size));
    return { dir, file };
  };
  const old = add(2147483647, 10, 8 * 86400000),
    huge = add(2147483647, 9 * 1024 * 1024),
    small = add(2147483647, 100),
    active = add(process.pid, 200);
  const empty = add(2147483647, 1);
  fs.unlinkSync(empty.file);
  const outside = path.join(root, "outside");
  fs.writeFileSync(outside, "preserve");
  fs.symlinkSync(outside, path.join(spool, randomUUID()));
  const sdk = {
    options: { appId: "native.test" },
    processSessionId: randomUUID(),
    crashes: { options: { retentionDays: 7, maximumPendingReports: 2 } },
    diagnostic: () => {},
  };
  const capture = Object.create(NativeCrashCapture.prototype);
  Object.assign(capture, { sdk, root: spool, sources: new Map() });
  capture.trimSpool();
  assert.equal(fs.existsSync(old.dir), false);
  assert.equal(fs.existsSync(huge.file), false);
  assert.ok(fs.existsSync(small.file));
  assert.ok(fs.existsSync(active.file));
  assert.equal(fs.existsSync(empty.dir), false);
  assert.equal(fs.readFileSync(outside, "utf8"), "preserve");
  for (let i = 0; i < 4; i++) add(2147483647, 100);
  capture.trimSpool();
  const retained = fs
    .readdirSync(spool, { withFileTypes: true })
    .filter((e) => e.isDirectory() && path.join(spool, e.name) !== active.dir);
  assert.equal(retained.length, 2);
});
