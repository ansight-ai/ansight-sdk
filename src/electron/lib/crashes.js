"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { randomUUID } = require("node:crypto");
class CrashCapture {
  constructor(sdk, directory) {
    this.sdk = sdk;
    this.options = {
      enabled: true,
      hostHandoffEnabled: true,
      maximumPendingReports: 8,
      retentionDays: 7,
      maximumBreadcrumbs: 64,
      maximumTraceBytes: 1048576,
      ...sdk.options.crashCapture,
    };
    this.root = path.join(directory, "crashes");
    this.breadcrumbs = [];
    for (const [key, min, max] of [
      ["maximumPendingReports", 1, 32],
      ["retentionDays", 1, 30],
      ["maximumBreadcrumbs", 0, 256],
      ["maximumTraceBytes", 16384, 4194304],
    ]) {
      if (
        !Number.isInteger(this.options[key]) ||
        this.options[key] < min ||
        this.options[key] > max
      )
        throw new Error(`Invalid crashCapture.${key}`);
    }
    this.enabled = sdk.options.crashCapture !== false && this.options.enabled;
    if (!this.enabled) return;
    fs.mkdirSync(this.root, { recursive: true, mode: 0o700 });
    this.trim();
    this.onException = (error, origin) => {
      // Monitor observes the exception without replacing Node's normal fatal path.
      this.record("unhandled_exception", error, { runtime: "node", origin });
    };
    process.on("uncaughtExceptionMonitor", this.onException);
  }
  breadcrumb(kind, label) {
    if (!this.enabled || !this.options.maximumBreadcrumbs) return;
    this.breadcrumbs.push({
      capturedAtUtc: new Date().toISOString(),
      kind: String(kind).slice(0, 64),
      label: String(label).slice(0, 512),
    });
    this.breadcrumbs = this.breadcrumbs.slice(-this.options.maximumBreadcrumbs);
  }
  files() {
    return fs
      .readdirSync(this.root)
      .filter((n) => /^[a-f0-9]{32}\.json$/.test(n))
      .map((n) => ({
        path: path.join(this.root, n),
        time: fs.statSync(path.join(this.root, n)).mtimeMs,
      }))
      .sort((a, b) => a.time - b.time);
  }
  trim() {
    const files = this.files(),
      cutoff = Date.now() - this.options.retentionDays * 86400000;
    for (let i = 0; i < files.length; i++)
      if (
        files[i].time < cutoff ||
        i < files.length - this.options.maximumPendingReports
      )
        fs.unlinkSync(files[i].path);
  }
  record(kind, error, termination = {}) {
    if (!this.enabled) return;
    try {
      const reportId = randomUUID().replaceAll("-", "");
      const occurredAtUtc = new Date().toISOString();
      const trace = Buffer.from(
        String(error?.stack || error?.message || error || ""),
      ).subarray(0, this.options.maximumTraceBytes);
      const report = {
        schema: "ansight.crash.v1",
        reportId,
        appId: this.sdk.options.appId,
        previousProcessSessionId: this.sdk.processSessionId,
        platform: process.platform,
        kind,
        confidence: "confirmed",
        occurredAtUtc,
        candidate: {
          runtime: termination.runtime || "electron",
          kind,
          message: String(error?.message || error || "").slice(0, 16384),
          fatal: true,
        },
        termination,
        breadcrumbs: this.breadcrumbs.slice(),
        traceBase64: trace.toString("base64"),
      };
      const filename = path.join(this.root, reportId + ".json"),
        temporary = filename + ".tmp";
      const fd = fs.openSync(temporary, "wx", 0o600);
      try {
        fs.writeFileSync(fd, JSON.stringify(report));
        fs.fsyncSync(fd);
      } finally {
        fs.closeSync(fd);
      }
      fs.renameSync(temporary, filename);
      this.trim();
      return reportId;
    } catch (e) {
      this.sdk.diagnostic(e);
    }
  }
  async deliver() {
    if (
      !this.enabled ||
      !this.options.hostHandoffEnabled ||
      !this.sdk.connected ||
      this.delivering
    )
      return;
    this.delivering = true;
    try {
      this.trim();
      for (const file of this.files()) {
        if (!this.sdk.connected) break;
        // Corrupt evidence is retained and reported; it must not block other reports.
        try {
          const report = JSON.parse(fs.readFileSync(file.path, "utf8"));
          await this.sdk.control("crash.handoff", {
            reportId: report.reportId,
            targetProcessSessionId: report.previousProcessSessionId,
            deliveryProcessSessionId: this.sdk.processSessionId,
            report,
          });
          this.sdk.nativeCrashes?.acknowledge(report.reportId);
          fs.unlinkSync(file.path);
        } catch (e) {
          this.sdk.diagnostic(e);
        }
      }
    } finally {
      this.delivering = false;
    }
  }
  stop() {
    if (this.onException)
      process.removeListener("uncaughtExceptionMonitor", this.onException);
  }
}
module.exports = { CrashCapture };
