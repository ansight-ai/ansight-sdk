"use strict";
const fs = require("node:fs"),
  fsp = fs.promises,
  path = require("node:path"),
  { randomUUID } = require("node:crypto");
const { archive } = require("./archive"),
  { fileFrame } = require("./protocol"),
  { CHANNELS } = require("./streams");
class OfflineCapture {
  constructor(sdk, directory) {
    this.sdk = sdk;
    this.options = {
      enabled: false,
      recordingEnabled: true,
      maximumSessions: 10,
      transferMode: "automatic",
      maximumSessionBytes: 128 * 1024 * 1024,
      maximumRetainedBytes: 512 * 1024 * 1024,
      maximumQueuedBytes: 8 * 1024 * 1024,
      ...sdk.options.offlineCapture,
    };
    for (const key of [
      "maximumSessions",
      "maximumSessionBytes",
      "maximumRetainedBytes",
      "maximumQueuedBytes",
    ])
      if (!Number.isSafeInteger(this.options[key]) || this.options[key] <= 0)
        throw new Error(`Invalid offlineCapture.${key}`);
    if (
      !["automatic", "manual"].includes(this.options.transferMode) ||
      this.options.maximumSessionBytes > this.options.maximumRetainedBytes
    )
      throw new Error("Invalid offline capture policy");
    this.root = path.join(
      this.options.rootDirectory || path.join(directory, "offline"),
      "sessions",
    );
    this.queue = Promise.resolve();
    this.queuedBytes = 0;
    this.leases = new Set();
    this.dropped = 0;
    this.enabled = this.options.enabled && this.options.recordingEnabled;
    this.lastError = null;
  }
  get isRecording() {
    return this.enabled && this.sdk.running && !this.sdk.connected;
  }
  work(fn) {
    const operation = this.queue.then(fn);
    this.queue = operation.catch((e) => {
      this.lastError = e.message;
      this.sdk.diagnostic(e);
    });
    return operation;
  }
  async initialize() {
    if (!this.options.enabled) return;
    await fsp.mkdir(this.root, { recursive: true, mode: 0o700 });
    for (const s of await this.sessions()) {
      if (!s.stoppedAtUtc) {
        s.stoppedAtUtc = new Date().toISOString();
        s.terminationKind = "interrupted";
        s.bytes = await this.payloadBytes(this.directory(s.sessionId));
        await this.manifest(s);
      }
    }
    await this.retain();
    this.unregisterTool = this.sdk.registerTool(
      {
        id: "offline_capture.transfer",
        name: "Transfer offline captures",
        description:
          "Begin transfer of retained offline sessions to the connected host",
        category: "offline_capture",
        policy: "write",
      },
      () => {
        if (!this.sdk.connected)
          throw new Error("Offline capture transfer requires a connection");
        void this.transfer().catch((e) => this.sdk.diagnostic(e));
        return { started: true };
      },
    );
  }
  directory(id) {
    if (!/^[a-f0-9]{32}$/.test(id))
      throw new Error("Invalid offline session ID");
    return path.join(this.root, id);
  }
  async payloadBytes(dir) {
    let bytes = 0;
    for (const entry of await fsp.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) bytes += await this.payloadBytes(full);
      else if (
        entry.isFile() &&
        entry.name !== "manifest.json" &&
        !entry.name.endsWith(".tmp")
      )
        bytes += (await fsp.stat(full)).size;
    }
    return bytes;
  }
  async sessions() {
    const result = [];
    for (const e of await fsp.readdir(this.root, { withFileTypes: true })) {
      if (e.isDirectory() && /^[a-f0-9]{32}$/.test(e.name)) {
        const s = JSON.parse(
          await fsp.readFile(
            path.join(this.root, e.name, "manifest.json"),
            "utf8",
          ),
        );
        if (s.sessionId !== e.name || s.appId !== this.sdk.options.appId)
          throw new Error("Offline manifest identity mismatch");
        result.push(s);
      }
    }
    return result.sort((a, b) => a.ordinal - b.ordinal);
  }
  async manifest(s) {
    const file = path.join(this.directory(s.sessionId), "manifest.json"),
      tmp = file + ".tmp";
    await fsp.writeFile(tmp, JSON.stringify(s), { mode: 0o600 });
    await fsp.rename(tmp, file);
  }
  async retain(extraSessions = 0, extraBytes = 0) {
    const sessions = await this.sessions();
    let count = sessions.length + extraSessions,
      bytes = sessions.reduce((n, s) => n + s.bytes, 0) + extraBytes;
    for (const s of sessions) {
      if (
        count <= this.options.maximumSessions &&
        bytes <= this.options.maximumRetainedBytes
      )
        return;
      if (
        s.sessionId === this.current?.sessionId ||
        this.leases.has(s.sessionId)
      )
        continue;
      await fsp.rm(this.directory(s.sessionId), { recursive: true });
      count--;
      bytes -= s.bytes;
    }
    if (
      count > this.options.maximumSessions ||
      bytes > this.options.maximumRetainedBytes
    )
      throw new Error(
        "Offline retention blocked by active session or transfer lease",
      );
  }
  async start() {
    if (this.current) return;
    const now = new Date().toISOString(),
      sessionId = randomUUID().replaceAll("-", "");
    const header = [
      {
        sessionId,
        appId: this.sdk.options.appId,
        processSessionId: this.sdk.processSessionId,
        clientName: this.sdk.options.appName || this.sdk.options.appId,
        startedAtUtc: now,
      },
      { type: "DeviceAppProfile", ...this.sdk.profile() },
      { type: "CLIENT_METRIC_CHANNELS", channels: this.sdk.streams.channels },
    ]
      .map((x) => JSON.stringify(x) + "\n")
      .join("");
    const properties = JSON.stringify(this.sdk.customProperties),
      bytes = Buffer.byteLength(header) + Buffer.byteLength(properties);
    if (bytes >= this.options.maximumSessionBytes)
      throw new Error("Offline metadata exceeds session budget");
    await this.retain(1, bytes);
    const s = {
      sessionId,
      appId: this.sdk.options.appId,
      processSessionId: this.sdk.processSessionId,
      startedAtUtc: now,
      ordinal: Date.now(),
      bytes,
      records: 3,
      droppedRecords: this.dropped,
    };
    await fsp.mkdir(path.join(this.directory(sessionId), "metadata"), {
      recursive: true,
      mode: 0o700,
    });
    await fsp.writeFile(
      path.join(this.directory(sessionId), "records.jsonl"),
      header,
      { mode: 0o600 },
    );
    await fsp.writeFile(
      path.join(this.directory(sessionId), "metadata/custom-properties.json"),
      properties,
      { mode: 0o600 },
    );
    await this.manifest(s);
    this.current = s;
  }
  append(record, attachment) {
    if (!this.isRecording) return false;
    const data = Buffer.from(JSON.stringify(record) + "\n"),
      size = data.length + (attachment?.bytes.length || 0);
    if (size > this.options.maximumQueuedBytes - this.queuedBytes) {
      this.dropped++;
      return true;
    }
    this.queuedBytes += size;
    void this.work(async () => {
      try {
        await this.start();
        if (this.current.bytes + size > this.options.maximumSessionBytes) {
          await this.seal("sizeLimit");
          await this.start();
        }
        if (this.current.bytes + size > this.options.maximumSessionBytes) {
          this.dropped++;
          return;
        }
        await this.retain(0, size);
        const dir = this.directory(this.current.sessionId);
        if (attachment) {
          const file = path.join(dir, attachment.relativePath);
          await fsp.mkdir(path.dirname(file), { recursive: true, mode: 0o700 });
          await fsp.writeFile(file, attachment.bytes, { mode: 0o600 });
        }
        const file = path.join(
          dir,
          attachment ? "screenshots/index/frames.jsonl" : "records.jsonl",
        );
        await fsp.mkdir(path.dirname(file), { recursive: true, mode: 0o700 });
        await fsp.appendFile(file, data, { mode: 0o600 });
        this.current.bytes += size;
        this.current.records++;
        this.current.droppedRecords = this.dropped;
        await this.manifest(this.current);
      } finally {
        this.queuedBytes -= size;
      }
    }).catch(() => {});
    return true;
  }
  screenshot(bytes, metadata) {
    const relativePath = `screenshots/${randomUUID()}.jpg`;
    return this.append(
      { path: relativePath, format: "jpeg", ...metadata },
      { relativePath, bytes },
    );
  }
  async seal(reason = "normal") {
    if (!this.current) return;
    const s = this.current;
    const fd = await fsp.open(
      path.join(this.directory(s.sessionId), "records.jsonl"),
      "r",
    );
    try {
      await fd.sync();
    } finally {
      await fd.close();
    }
    s.stoppedAtUtc = new Date().toISOString();
    s.terminationKind = reason;
    s.droppedRecords = this.dropped;
    await this.manifest(s);
    this.current = null;
  }
  async updateProperties() {
    if (!this.options.enabled) return;
    await this.work(async () => {
      if (!this.current) return;
      const data = Buffer.from(JSON.stringify(this.sdk.customProperties)),
        file = path.join(
          this.directory(this.current.sessionId),
          "metadata/custom-properties.json",
        ),
        old = (await fsp.stat(file)).size,
        delta = data.length - old;
      if (this.current.bytes + delta > this.options.maximumSessionBytes) {
        await this.seal("sizeLimit");
        await this.start();
        return;
      }
      await this.retain(0, Math.max(0, delta));
      await fsp.writeFile(file, data, { mode: 0o600 });
      this.current.bytes += delta;
      await this.manifest(this.current);
    });
  }
  async connected() {
    await this.work(() => this.seal());
    if (this.enabled && this.options.transferMode === "automatic")
      void this.transfer().catch((e) => this.sdk.diagnostic(e));
  }
  async setEnabled(enabled) {
    this.enabled = this.options.enabled && enabled;
    if (!this.enabled) await this.work(() => this.seal());
  }
  async stop() {
    this.unregisterTool?.();
    this.unregisterTool = null;
    this.enabled = false;
    await this.work(() => this.seal());
  }
  async transfer() {
    if (!this.enabled || !this.sdk.connected)
      throw new Error("Offline transfer requires an enabled connected runtime");
    if (this.transferring) return;
    this.transferring = true;
    try {
      await this.work(() => this.seal());
      const pending = await this.work(async () => {
        const sessions = (await this.sessions()).filter((s) => s.stoppedAtUtc);
        for (const s of sessions) this.leases.add(s.sessionId);
        return sessions;
      });
      for (const s of pending) {
        const zip = path.join(path.dirname(this.root), `${s.sessionId}.zip`);
        try {
          await fsp.rm(zip, { force: true });
          const info = await archive(this.directory(s.sessionId), zip),
            transferId = randomUUID().replaceAll("-", "");
          const descriptor = {
            schema: "ansight.offline-transfer.v1",
            sessionId: s.sessionId,
            transferId,
            ...info,
          };
          const socket = this.sdk.socket;
          const check = () => {
            if (
              !this.sdk.connected ||
              this.sdk.socket !== socket ||
              !this.enabled
            )
              throw new Error("Offline connection changed during transfer");
          };
          check();
          await this.sdk.control("offline_capture.begin", descriptor);
          let offset = 0,
            sequence = 0;
          for await (const bytes of fs.createReadStream(zip, {
            highWaterMark: 65536,
          })) {
            check();
            await this.sdk.send(
              fileFrame(transferId, 1, sequence++, offset, bytes),
            );
            offset += bytes.length;
          }
          check();
          await this.sdk.send(
            fileFrame(transferId, 2, sequence, offset, Buffer.alloc(0)),
          );
          const receipt = await this.sdk.control(
            "offline_capture.commit",
            descriptor,
            120000,
          );
          if (
            receipt?.durable !== true ||
            receipt.sessionId !== s.sessionId ||
            receipt.sha256 !== info.sha256
          )
            throw new Error("Offline receipt does not match retained source");
          await this.work(() =>
            fsp.rm(this.directory(s.sessionId), { recursive: true }),
          );
        } finally {
          this.leases.delete(s.sessionId);
          await fsp.rm(zip, { force: true });
        }
      }
    } catch (e) {
      this.lastError = e.message;
      throw e;
    } finally {
      this.leases.clear();
      this.transferring = false;
    }
  }
}
module.exports = { OfflineCapture };
