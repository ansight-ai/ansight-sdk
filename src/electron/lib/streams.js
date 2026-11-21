"use strict";
const { randomUUID } = require("node:crypto");
const { performance } = require("node:perf_hooks");
const {
  observeRenderer,
  drainRenderer,
  stopRenderer,
} = require("./observations");
const CHANNELS = [
  [100, "Main process RSS", "MiB", "memory", "rss"],
  [101, "Main JavaScript heap used", "MiB", "memory", "heap.used"],
  [102, "Main process CPU", "%", "cpu", "cpu.percent.one-core"],
  [103, "Main sampling timer lateness", "ms", "custom", "event-loop.delay"],
  [
    104,
    "Mean visible-window animation callbacks",
    "Hz",
    "custom",
    "animation.callback-rate",
  ],
  [105, "Dropped capture records", "count", "custom", "capture.dropped"],
].map(([id, name, unit, type, kind]) => ({
  id,
  name,
  unit,
  type,
  kind,
  color: "#FF6699CC",
  source: "electron.sdk",
  group: "Electron",
}));
class Streams {
  constructor(sdk) {
    this.sdk = sdk;
    this.queue = [];
    this.retention = new (require("./retention").Retention)(
      sdk.options.retentionPeriodSeconds ?? 120,
    );
    this.channels = [
      {
        id: 0,
        name: "Unspecified",
        unit: "",
        type: "custom",
        kind: "custom",
        source: "app",
        color: "#FF6699CC",
      },
      {
        id: 4,
        name: "Lifecycle",
        unit: "",
        type: "lifecycle",
        color: "#FF9500",
        source: "electron.sdk",
        group: "Runtime",
      },
      ...CHANNELS,
    ];
    const extra = sdk.options.additionalChannels ?? [];
    if (!Array.isArray(extra))
      throw new Error("additionalChannels must be an array");
    for (const channel of extra) {
      if (
        !Number.isInteger(channel.id) ||
        channel.id < 0 ||
        channel.id > 255 ||
        this.channels.some((c) => c.id === channel.id)
      )
        throw new Error("Invalid or duplicate metric channel id");
      if (
        typeof channel.name !== "string" ||
        !channel.name.trim() ||
        channel.name.length > 128
      )
        throw new Error(
          "Metric channel requires a name of at most 128 characters",
        );
      const descriptor = {
        id: channel.id,
        name: channel.name,
        type: "custom",
        source: "app",
      };
      for (const key of ["unit", "kind", "color", "group"]) {
        if (
          channel[key] != null &&
          (typeof channel[key] !== "string" || channel[key].length > 128)
        )
          throw new Error("Invalid channel " + key);
        descriptor[key] = channel[key] ?? "";
      }
      this.channels.push(descriptor);
    }
    this.framesEnabled = sdk.options.enableFramesPerSecond !== false;
    this.touchesConfigured = sdk.options.touchCapture !== false;
    this.touchesEnabled = this.touchesConfigured;
    this.touchGuard = null;
    this.dropped = 0;
    this.windows = new Map();
    this.lastCpu = process.cpuUsage();
    this.lastTime = performance.now();
  }
  touchAllowed() {
    if (!this.touchesEnabled) return false;
    try {
      return !this.touchGuard || this.touchGuard() === true;
    } catch (e) {
      this.sdk.diagnostic(e);
      return false;
    }
  }
  captureConfiguration() {
    return { frames: this.framesEnabled, touches: this.touchAllowed() };
  }
  async configureCapture() {
    const config = this.captureConfiguration();
    if (!config.touches) {
      for (const coordinator of this.sdk.touchTrees.values())
        coordinator.close();
      this.sdk.touchTrees.clear();
    }
    await Promise.all(
      [...this.windows.values()]
        .filter((w) => !w.isDestroyed() && !w.webContents.isLoadingMainFrame())
        .map((w) => this.sdk.runDom(w, observeRenderer, config)),
    );
  }
  enqueue(message) {
    if (this.sdk.options.enabled !== true || this.stopped) return;
    this.retention.append(message);
    if (this.sdk.offline?.append(message)) return;
    if (this.queue.length >= 2000) {
      this.queue.shift();
      this.dropped++;
    }
    this.queue.push(message);
    void this.flush();
  }
  async flush() {
    if (this.flushing || !this.sdk.connected) return;
    this.flushing = true;
    try {
      while (
        this.queue.length &&
        this.sdk.connected &&
        this.sdk.socket?.bufferedAmount < 2097152
      ) {
        const record = this.queue.shift();
        try {
          await this.sdk.send(record);
        } catch (e) {
          this.queue.unshift(record);
          throw e;
        }
      }
    } catch (e) {
      this.sdk.diagnostic(e);
    } finally {
      this.flushing = false;
    }
  }
  event(
    label,
    eventType = "Event",
    details = {},
    capturedAtUtc = new Date().toISOString(),
    channel = 0,
  ) {
    this.sdk.crashes?.breadcrumb(eventType, label);
    this.enqueue({
      type: "CLIENT_EVENTS",
      events: [
        {
          id: randomUUID(),
          label: String(label).slice(0, 1024),
          eventType,
          details: JSON.stringify(details).slice(0, 16384),
          capturedAtUtc,
          channel,
        },
      ],
    });
  }
  attach(w) {
    const wc = w.webContents;
    const ready = () => {
      if (!wc.isDestroyed())
        this.sdk
          .runDom(w, observeRenderer, this.captureConfiguration())
          .catch((e) => this.sdk.diagnostic(e));
    };
    const navigation = (_event, url) => {
      let safe;
      try {
        const u = new URL(url);
        u.search = "";
        u.hash = "";
        u.username = "";
        u.password = "";
        safe = u.toString();
      } catch {
        safe = "[invalid URL]";
      }
      this.event("Navigation", "Navigation", { windowId: w.id, url: safe });
    };
    const lifecycle = () => this.lifecycle();
    const key = (_event, input) => {
      if (input.type === "keyDown")
        this.event("Keyboard input", "Event", {
          windowId: w.id,
          key: [
            "Enter",
            "Escape",
            "Tab",
            "Backspace",
            "Delete",
            "ArrowUp",
            "ArrowDown",
            "ArrowLeft",
            "ArrowRight",
          ].includes(input.key)
            ? input.key
            : "character",
          modifiers: {
            control: input.control,
            shift: input.shift,
            alt: input.alt,
            meta: input.meta,
          },
        });
    };
    wc.on("dom-ready", ready);
    wc.on("did-navigate", navigation);
    wc.on("did-navigate-in-page", navigation);
    wc.on("before-input-event", key);
    for (const event of [
      "show",
      "hide",
      "minimize",
      "restore",
      "focus",
      "blur",
    ])
      w.on(event, lifecycle);
    this.windows.set(w.id, w);
    if (!wc.isLoadingMainFrame()) ready();
    return () => {
      this.windows.delete(w.id);
      wc.removeListener("dom-ready", ready);
      wc.removeListener("did-navigate", navigation);
      wc.removeListener("did-navigate-in-page", navigation);
      wc.removeListener("before-input-event", key);
      for (const event of [
        "show",
        "hide",
        "minimize",
        "restore",
        "focus",
        "blur",
      ])
        w.removeListener(event, lifecycle);
      if (!wc.isDestroyed())
        void this.sdk.runDom(w, stopRenderer, {}).catch(() => {});
    };
  }
  lifecycle() {
    const state =
      this.lifecycleOverride ??
      ([...this.windows.values()].some(
        (w) => !w.isDestroyed() && w.isVisible() && !w.isMinimized(),
      )
        ? "foreground"
        : "background");
    this.setLifecycle(state);
  }
  setLifecycle(state, changedAtUtc = new Date().toISOString()) {
    if (
      this.sdk.options.enabled !== true ||
      this.stopped ||
      state === this.state
    )
      return;
    this.state = state;
    this.enqueue({
      type: "CLIENT_APP_STATE",
      state,
      changedAtUtc,
    });
    this.lifecycleChangedAtUtc = changedAtUtc;
    this.event(`lifecycle.${state}`, "Lifecycle", { state }, changedAtUtc, 4);
  }
  async connected() {
    await this.sdk.send({
      type: "CLIENT_METRIC_CHANNELS",
      channels: this.channels,
    });
    if (this.state == null) this.lifecycle();
    else
      await this.sdk.send({
        type: "CLIENT_APP_STATE",
        state: this.state,
        changedAtUtc: this.lifecycleChangedAtUtc,
      });
    await this.flush();
  }
  start() {
    if (this.timer) return;
    this.stopped = false;
    this.lastCpu = process.cpuUsage();
    this.lastTime = performance.now();
    this.interval = this.sdk.options.sampleFrequencyMilliseconds ?? 400;
    this.expected = performance.now() + this.interval;
    this.timer = setInterval(
      () => this.sample().catch((e) => this.sdk.diagnostic(e)),
      this.interval,
    );
    this.timer.unref();
    this.onException = (e) =>
      this.event("Uncaught main-process exception", "Exception", {
        message: e.message,
        stack: e.stack,
      });
    process.on("uncaughtExceptionMonitor", this.onException);
  }
  async sample() {
    if (this.sampling) return;
    this.sampling = true;
    try {
      const now = performance.now(),
        elapsed = now - this.lastTime,
        cpu = process.cpuUsage(),
        memory = process.memoryUsage();
      const cpuPercent =
        elapsed > 0
          ? ((cpu.user -
              this.lastCpu.user +
              (cpu.system - this.lastCpu.system)) /
              1000 /
              elapsed) *
            100
          : 0;
      const values = [
        [100, memory.rss / 1048576],
        [101, memory.heapUsed / 1048576],
        [102, cpuPercent],
        [103, Math.max(0, now - this.expected)],
        [105, this.dropped],
      ];
      this.lastCpu = cpu;
      this.lastTime = now;
      this.expected = now + this.interval;
      const rates = [];
      for (const w of this.windows.values()) {
        if (
          w.isDestroyed() ||
          w.webContents.isLoadingMainFrame() ||
          w.webContents.isCrashed?.()
        )
          continue;
        await this.sdk.runDom(w, observeRenderer, this.captureConfiguration());
        const data = await this.sdk.runDom(w, drainRenderer, {});
        if (!data) continue;
        this.dropped += data.dropped;
        if (this.framesEnabled && data.visible && data.elapsedMs > 0)
          rates.push((data.frames * 1000) / data.elapsedMs);
        for (const e of data.events) {
          if (e.kind === "pointer" && !this.touchAllowed()) continue;
          if (e.kind === "pointer") this.sdk.observeTouch?.(w, e);
          if (e.kind === "pointer")
            this.enqueue({
              type: "CLIENT_TOUCH_INPUT",
              schema: "ansight.touches.v1",
              ids: [randomUUID()],
              t0: e.capturedAtUtc,
              space: "w",
              unit: "pt",
              surface: [e.width, e.height, e.scale],
              rows: [
                [
                  0,
                  e.action,
                  e.pointerId,
                  e.x,
                  e.y,
                  0,
                  1,
                  {
                    tool:
                      e.pointerType === "mouse"
                        ? "mouse"
                        : e.pointerType === "pen"
                          ? "stylus"
                          : e.pointerType === "touch"
                            ? "finger"
                            : "unknown",
                    sampleKind: "current",
                    source: "electron.pointer",
                    pointerType: e.pointerType,
                    buttons: e.buttons,
                    trusted: e.trusted,
                    windowId: w.id,
                  },
                ],
              ],
            });
          else
            this.event(
              e.label,
              "Exception",
              { windowId: w.id, ...e.details },
              e.capturedAtUtc,
            );
        }
      }
      if (this.framesEnabled && rates.length)
        values.push([104, rates.reduce((a, b) => a + b, 0) / rates.length]);
      const capturedAtUtc = new Date().toISOString();
      this.enqueue({
        type: "CLIENT_METRICS",
        metrics: values
          .filter(([, v]) => Number.isFinite(v))
          .map(([channel, value]) => ({ channel, value, capturedAtUtc })),
      });
      this.lifecycle();
      await this.flush();
    } finally {
      this.sampling = false;
    }
  }
  stop() {
    this.stopped = true;
    clearInterval(this.timer);
    this.timer = null;
    if (this.onException)
      process.removeListener("uncaughtExceptionMonitor", this.onException);
    this.queue.length = 0;
  }
}
module.exports = { Streams, CHANNELS };
