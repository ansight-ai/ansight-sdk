"use strict";
const path = require("node:path");
const { randomUUID, createHash } = require("node:crypto");

// Runs only in our private, sandboxed recorder renderer, never in app content.
async function beginRecording(source, width, height) {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: false,
    video: {
      mandatory: {
        chromeMediaSource: "tab",
        chromeMediaSourceId: source,
        minFrameRate: 15,
        maxFrameRate: 15,
        maxWidth: width,
        maxHeight: height,
      },
    },
  });
  const preview = document.createElement("video");
  preview.muted = true;
  preview.srcObject = stream;
  document.body.appendChild(preview);
  await preview.play();
  await new Promise((resolve) => preview.requestVideoFrameCallback(resolve));
  const recorder = new MediaRecorder(stream, {
    mimeType: "video/webm;codecs=vp8",
    videoBitsPerSecond: 2000000,
  });
  const state = (globalThis.recording = {
    chunks: [],
    size: 0,
    error: null,
    recorder,
    stream,
  });
  state.done = new Promise((resolve) => {
    recorder.onstop = resolve;
  });
  recorder.ondataavailable = (event) => {
    state.size += event.data.size;
    if (state.size > 64 * 1024 * 1024) {
      state.error = "Recording exceeded 64 MiB";
      if (recorder.state !== "inactive") recorder.stop();
      stream.getTracks().forEach((track) => track.stop());
    } else state.chunks.push(event.data);
  };
  recorder.onerror = (event) => {
    state.error = event.error?.message || "Native recorder failed";
  };
  stream.getVideoTracks()[0].onended = () => {
    state.error ||= "Captured window stream ended unexpectedly";
    if (recorder.state !== "inactive") recorder.stop();
  };
  state.startedAtUtc = new Date().toISOString();
  recorder.start(250);
  return { startedAtUtc: state.startedAtUtc };
}
async function finishRecording() {
  const s = globalThis.recording;
  if (s.recorder.state !== "inactive") s.recorder.stop();
  await s.done;
  s.stream.getTracks().forEach((track) => {
    track.onended = null;
    track.stop();
  });
  if (s.error) throw new Error(s.error);
  s.bytes = new Uint8Array(await new Blob(s.chunks).arrayBuffer());
  s.chunks = [];
  return s.bytes.length;
}
class WindowVideoRecorder {
  constructor(sdk) {
    this.sdk = sdk;
    this.active = null;
  }
  async start(args = {}) {
    if (this.active) throw new Error("An Electron recording is already active");
    const target = this.sdk.window(args);
    const recording = (this.active = {
      id: randomUUID(),
      target,
      state: "starting",
    });
    try {
      recording.throttling = target.webContents.getBackgroundThrottling();
      target.webContents.setBackgroundThrottling(false);
      const recorder = (recording.window = new this.sdk.electron.BrowserWindow({
        show: false,
        webPreferences: {
          sandbox: true,
          contextIsolation: true,
          nodeIntegration: false,
          backgroundThrottling: false,
          partition: `ansight-video-${recording.id}`,
        },
      }));
      recorder.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
      await recorder.loadFile(path.join(__dirname, "video-recorder.html"));
      const source = target.webContents.getMediaSourceId(recorder.webContents);
      const [w, h] = target.getContentSize();
      const scale = Math.min(1, 1280 / Math.max(w, h));
      const width = Math.max(2, Math.floor((w * scale) / 2) * 2);
      const height = Math.max(2, Math.floor((h * scale) / 2) * 2);
      const result = await this.bounded(
        recorder.webContents.executeJavaScript(
          `(${beginRecording.toString()})(${JSON.stringify(source)},${width},${height})`,
        ),
      );
      Object.assign(recording, result, { state: "recording" });
      recording.timer = setTimeout(() => {
        recording.error = "Recording exceeded the ten-minute limit";
        this.dispose(recording);
      }, 600000);
      recording.timer.unref();
      return this.status();
    } catch (error) {
      this.dispose(recording);
      this.active = null;
      throw error;
    }
  }
  async bounded(promise) {
    let timer;
    try {
      return await Promise.race([
        promise,
        new Promise((_, reject) => {
          timer = setTimeout(
            () => reject(new Error("Native video operation timed out")),
            15000,
          );
        }),
      ]);
    } finally {
      clearTimeout(timer);
    }
  }
  async status() {
    const r = this.active;
    if (r?.state === "recording" && !r.error) {
      if (r.window.isDestroyed() || r.target.isDestroyed())
        r.error = "Recording window closed";
      else {
        try {
          const state = await this.bounded(
            r.window.webContents.executeJavaScript(
              `({error: globalThis.recording?.error, state: globalThis.recording?.recorder.state})`,
            ),
          );
          if (state.error || state.state !== "recording")
            r.error = state.error || "Native recorder stopped unexpectedly";
        } catch (error) {
          r.error = error.message;
        }
      }
      if (r.error) this.dispose(r);
    }
    return r
      ? {
          recordingId: r.id,
          windowId: r.target.id,
          state: r.error ? "failed" : r.state,
          startedAtUtc: r.startedAtUtc,
          error: r.error || null,
        }
      : { state: "idle" };
  }
  require(id) {
    if (!this.active || this.active.id !== id)
      throw new Error("Unknown recordingId");
    return this.active;
  }
  async stop(args, context) {
    const r = this.require(args.recordingId);
    if (r.state !== "recording" || r.error)
      throw new Error(r.error || "Recording is not ready");
    r.state = "stopping";
    try {
      const size = await this.bounded(
        r.window.webContents.executeJavaScript(
          `(${finishRecording.toString()})()`,
        ),
      );
      const endedAtUtc = new Date().toISOString();
      if (!Number.isSafeInteger(size) || size < 1 || size > 64 * 1024 * 1024)
        throw new Error("Invalid recorded video size");
      const bytes = Buffer.alloc(size);
      for (let offset = 0; offset < size; offset += 32768) {
        const encoded = await this.bounded(
          r.window.webContents.executeJavaScript(
            `btoa(String.fromCharCode(...globalThis.recording.bytes.subarray(${offset},${Math.min(size, offset + 32768)})))`,
          ),
        );
        Buffer.from(encoded, "base64").copy(bytes, offset);
      }
      const transferId = randomUUID().replaceAll("-", "");
      context.transfers.push({ transferId, bytes });
      return {
        recordingId: r.id,
        startedAtUtc: r.startedAtUtc,
        endedAtUtc,
        source: "electron.webContents",
        format: "webm",
        mimeType: "video/webm",
        fileName: `recording-${r.id}.webm`,
        sizeBytes: bytes.length,
        sha256: createHash("sha256").update(bytes).digest("hex"),
        deliveryMode: "websocket_binary",
        wireProtocol: "ansight.file-transfer.v1",
        transferId,
        downloadId: context.requestId,
        status: "queued",
      };
    } finally {
      this.dispose(r);
      this.active = null;
    }
  }
  cancel(args) {
    const r = this.require(args.recordingId);
    this.dispose(r);
    this.active = null;
    return { cancelled: true };
  }
  dispose(r = this.active) {
    if (!r) return;
    clearTimeout(r.timer);
    if (r.window && !r.window.isDestroyed()) r.window.destroy();
    if (!r.target.isDestroyed() && r.throttling !== undefined)
      r.target.webContents.setBackgroundThrottling(r.throttling);
  }
}
function installVideo(sdk) {
  const video = (sdk.video = new WindowVideoRecorder(sdk));
  for (const [name, policy, properties, handler] of [
    [
      "start",
      "write",
      { windowId: { type: "integer" } },
      (args) => video.start(args),
    ],
    ["status", "read", {}, () => video.status()],
    [
      "stop",
      "write",
      { recordingId: { type: "string" } },
      (args, context) => video.stop(args, context),
    ],
    [
      "cancel",
      "write",
      { recordingId: { type: "string" } },
      (args) => video.cancel(args),
    ],
  ])
    sdk.registerTool(
      {
        id: `electron.video.${name}`,
        category: "video",
        policy,
        description: `${name} native video of one explicitly attached Electron window; no audio or desktop capture`,
        argumentsSchema: {
          type: "object",
          properties,
          additionalProperties: false,
        },
      },
      handler,
    );
}
module.exports = { WindowVideoRecorder, installVideo };
