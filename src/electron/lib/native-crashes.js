"use strict";
const fs = require("node:fs"),
  path = require("node:path"),
  { createHash } = require("node:crypto");
const started = new WeakMap();
const MAX_DUMP_BYTES = 4 * 1024 * 1024;
function inspectMinidump(bytes) {
  if (bytes.length < 32 || bytes.toString("ascii", 0, 4) !== "MDMP")
    throw new Error("Invalid minidump header");
  const count = bytes.readUInt32LE(8),
    directory = bytes.readUInt32LE(12);
  if (count > 256 || directory + count * 12 > bytes.length)
    throw new Error("Invalid minidump directory");
  const result = { timestamp: bytes.readUInt32LE(20) };
  for (let i = 0; i < count; i++) {
    const at = directory + i * 12,
      type = bytes.readUInt32LE(at),
      length = bytes.readUInt32LE(at + 4),
      offset = bytes.readUInt32LE(at + 8);
    if (offset + length > bytes.length)
      throw new Error("Invalid minidump stream");
    if (type === 15 && length >= 12 && bytes.readUInt32LE(offset + 4) & 1)
      result.pid = bytes.readUInt32LE(offset + 8);
    if (type === 6 && length >= 40) {
      result.threadId = bytes.readUInt32LE(offset);
      result.exceptionCode = bytes.readUInt32LE(offset + 8);
      result.exceptionFlags = bytes.readUInt32LE(offset + 12);
      result.exceptionAddress =
        "0x" + bytes.readBigUInt64LE(offset + 24).toString(16);
    }
  }
  if (result.exceptionCode == null)
    throw new Error("Minidump has no exception stream");
  return result;
}
function regular(file) {
  const s = fs.lstatSync(file);
  if (!s.isFile() || s.isSymbolicLink())
    throw new Error("Native crash source must be a regular file");
  return s;
}
class NativeCrashCapture {
  constructor(sdk, directory) {
    this.sdk = sdk;
    this.sources = new Map();
    this.root = path.join(directory, "native-crashpad");
    fs.mkdirSync(this.root, { recursive: true, mode: 0o700 });
    this.recover();
    const app = sdk.electron.app;
    if (started.has(app)) return;
    const processDirectory = path.join(this.root, sdk.processSessionId);
    fs.mkdirSync(processDirectory, { recursive: true, mode: 0o700 });
    fs.writeFileSync(
      path.join(processDirectory, "process.json"),
      JSON.stringify({
        appId: sdk.options.appId,
        processSessionId: sdk.processSessionId,
        pid: process.pid,
        startedAtUtc: new Date().toISOString(),
      }),
      { mode: 0o600, flag: "wx" },
    );
    // Explicit opt-in: Crashpad is process-global and cannot be stopped once started.
    app.setPath("crashDumps", processDirectory);
    sdk.electron.crashReporter.start({
      uploadToServer: false,
      compress: false,
      extra: { ansightProcessSessionId: sdk.processSessionId },
    });
    started.set(app, processDirectory);
  }
  trimSpool() {
    const candidates = [],
      inactive = [];
    const cutoff =
      Date.now() - this.sdk.crashes.options.retentionDays * 86400000;
    for (const entry of fs.readdirSync(this.root, { withFileTypes: true })) {
      if (
        !entry.isDirectory() ||
        entry.isSymbolicLink() ||
        !/^[a-f0-9-]{36}$/.test(entry.name) ||
        entry.name === this.sdk.processSessionId
      )
        continue;
      const directory = path.join(this.root, entry.name);
      try {
        const file = path.join(directory, "process.json");
        if (regular(file).size > 4096) continue;
        const manifest = JSON.parse(fs.readFileSync(file, "utf8"));
        if (
          manifest.appId !== this.sdk.options.appId ||
          manifest.processSessionId !== entry.name ||
          !Number.isInteger(manifest.pid) ||
          !Number.isFinite(Date.parse(manifest.startedAtUtc))
        )
          continue;
        try {
          process.kill(manifest.pid, 0);
          continue;
        } catch (e) {
          if (e.code !== "ESRCH") continue;
        }
        if (Date.parse(manifest.startedAtUtc) < cutoff) {
          fs.rmSync(directory, { recursive: true });
          continue;
        }
        inactive.push(directory);
        for (const bucket of ["pending", "completed", "reports"]) {
          const folder = path.join(directory, bucket);
          if (!fs.existsSync(folder)) continue;
          const info = fs.lstatSync(folder);
          if (!info.isDirectory() || info.isSymbolicLink()) continue;
          for (const name of fs.readdirSync(folder)) {
            if (!/^[a-f0-9-]+\.dmp$/i.test(name)) continue;
            const file = path.join(folder, name),
              stat = regular(file);
            candidates.push({ file, size: stat.size, time: stat.mtimeMs });
          }
        }
      } catch (e) {
        this.sdk.diagnostic(e);
      }
    }
    candidates.sort((a, b) => b.time - a.time);
    let retained = 0,
      bytes = 0;
    const budget =
      this.sdk.crashes.options.maximumPendingReports * MAX_DUMP_BYTES;
    for (const item of candidates) {
      if (
        retained >= this.sdk.crashes.options.maximumPendingReports ||
        bytes + item.size > budget
      ) {
        fs.unlinkSync(item.file);
        this.sdk.diagnostic(
          new Error(
            "Native crash source removed by pending count/byte retention budget",
          ),
        );
      } else {
        retained++;
        bytes += item.size;
      }
    }
    for (const directory of inactive) {
      const hasDump = ["pending", "completed", "reports"].some((bucket) => {
        const folder = path.join(directory, bucket);
        if (!fs.existsSync(folder)) return false;
        const stat = fs.lstatSync(folder);
        return (
          !stat.isDirectory() ||
          stat.isSymbolicLink() ||
          fs.readdirSync(folder).some((name) => name.endsWith(".dmp"))
        );
      });
      if (!hasDump) fs.rmSync(directory, { recursive: true });
    }
  }
  recover() {
    this.trimSpool();
    const cutoff =
      Date.now() - this.sdk.crashes.options.retentionDays * 86400000;
    const folders = fs
      .readdirSync(this.root, { withFileTypes: true })
      .filter(
        (d) =>
          d.isDirectory() &&
          !d.isSymbolicLink() &&
          /^[a-f0-9-]{36}$/.test(d.name),
      );
    for (const entry of folders) {
      if (entry.name === this.sdk.processSessionId) continue;
      const directory = path.join(this.root, entry.name);
      try {
        const manifestFile = path.join(directory, "process.json");
        if (regular(manifestFile).size > 4096)
          throw new Error("Native crash manifest exceeds limit");
        const manifest = JSON.parse(fs.readFileSync(manifestFile, "utf8"));
        if (
          manifest.appId !== this.sdk.options.appId ||
          manifest.processSessionId !== entry.name
        )
          throw new Error("Native crash identity mismatch");
        if (
          !Number.isInteger(manifest.pid) ||
          !Number.isFinite(Date.parse(manifest.startedAtUtc))
        )
          throw new Error("Invalid native crash manifest");
        let alive = false;
        try {
          process.kill(manifest.pid, 0);
          alive = true;
        } catch (e) {
          if (e.code !== "ESRCH") alive = true;
        }
        if (alive) continue;
        if (Date.parse(manifest.startedAtUtc) < cutoff) {
          fs.rmSync(directory, { recursive: true });
          continue;
        }
        for (const bucket of ["pending", "completed", "reports"]) {
          const folder = path.join(directory, bucket);
          if (!fs.existsSync(folder)) continue;
          if (
            !fs.lstatSync(folder).isDirectory() ||
            fs.lstatSync(folder).isSymbolicLink()
          )
            throw new Error("Invalid native crash directory");
          for (const name of fs
            .readdirSync(folder)
            .filter((n) => /^[a-f0-9-]+\.dmp$/i.test(n))) {
            const file = path.join(folder, name),
              stat = regular(file);
            if (stat.size > MAX_DUMP_BYTES) {
              this.sdk.diagnostic(
                new Error(
                  "Native minidump exceeds 4 MiB handoff limit; retained locally",
                ),
              );
              continue;
            }
            const bytes = fs.readFileSync(file),
              info = inspectMinidump(bytes);
            const reportId = createHash("sha256")
              .update(manifest.processSessionId)
              .update(bytes)
              .digest("hex")
              .slice(0, 32);
            this.sources.set(reportId, file);
            const reportFile = path.join(
              this.sdk.crashes.root,
              reportId + ".json",
            );
            if (fs.existsSync(reportFile)) continue;
            const main = info.pid === manifest.pid;
            const report = {
              schema: "ansight.crash.v1",
              reportId,
              appId: manifest.appId,
              previousProcessSessionId: manifest.processSessionId,
              platform: process.platform,
              kind: "native_crash",
              confidence: "confirmed",
              occurredAtUtc: new Date(
                (info.timestamp || Math.floor(stat.mtimeMs / 1000)) * 1000,
              ).toISOString(),
              candidate: {
                runtime: "electron.crashpad",
                kind: "native_crash",
                message: `Native ${main ? "main" : "child or unidentified"} process crash (exception ${info.exceptionCode})`,
                fatal: main,
              },
              termination: {
                runtime: "electron.crashpad",
                processKind: main ? "main" : "child-or-unidentified",
                ...info,
                traceFormat: "minidump",
                traceSha256: createHash("sha256").update(bytes).digest("hex"),
              },
              breadcrumbs: [],
              traceBase64: bytes.toString("base64"),
            };
            const temporary = reportFile + ".tmp";
            fs.writeFileSync(temporary, JSON.stringify(report), {
              mode: 0o600,
            });
            fs.renameSync(temporary, reportFile);
          }
        }
      } catch (e) {
        this.sdk.diagnostic(e);
      }
    }
    this.sdk.crashes.trim();
    // Apply the same pending-report count budget to source dumps as to JSON reports.
    for (const [reportId, file] of this.sources) {
      if (
        !fs.existsSync(path.join(this.sdk.crashes.root, reportId + ".json"))
      ) {
        fs.unlinkSync(file);
        this.sources.delete(reportId);
      }
    }
  }
  acknowledge(reportId) {
    const file = this.sources.get(reportId);
    if (!file) return;
    fs.unlinkSync(file);
    this.sources.delete(reportId);
    this.trimSpool();
  }
}
module.exports = { NativeCrashCapture, inspectMinidump };
