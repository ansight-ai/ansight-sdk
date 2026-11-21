"use strict";
const { EventEmitter } = require("node:events");
const { randomUUID, randomBytes, createHash } = require("node:crypto");
const { attachNetwork } = require("./lib/network");
const { Streams } = require("./lib/streams");
const { input, focusEditor } = require("./lib/input");
const fs = require("node:fs"),
  path = require("node:path"),
  os = require("node:os");
const WebSocket = require("ws");
const { enroll, jpegFrame, fileFrame } = require("./lib/protocol");
const { inspectDom, resolveNode } = require("./lib/dom");
const object = (properties) => ({
  type: "object",
  properties,
  additionalProperties: false,
});
const str = { type: "string" },
  num = { type: "number" },
  bool = { type: "boolean" };
class AnsightElectron extends EventEmitter {
  constructor(options) {
    super();
    if (!options?.appId) throw new Error("Ansight requires appId");
    this.options = {
      ...options,
      ...require("./lib/retention").normalizedTiming(options),
    };
    this.toolGuard =
      options.toolGuard || (options.allowActions ? "readWrite" : "readOnly");
    if (
      !["disabled", "readOnly", "readWrite", "fullAccess"].includes(
        this.toolGuard,
      )
    )
      throw new Error("Invalid toolGuard");
    this.captureOptions = {
      intervalMilliseconds: 2000,
      quality: 60,
      maxWidth: 480,
      mode: "screenshotOnly",
      ...(options.sessionJpegCapture || {}),
    };
    if (
      ![
        "screenshotOnly",
        "screenshotAndVisualTree",
        "screenshotWithVisualTreeOnTouch",
      ].includes(this.captureOptions.mode)
    )
      throw new Error("Invalid sessionJpegCapture mode");
    this.electron = options.electron || require("electron");
    this.windows = new Map();
    this.pending = new Map();
    this.tools = new Map();
    this.detachers = new Map();
    this.debuggerWindows = new Set();
    this.streams = new Streams(this);
    this.customProperties = {};
    this.processSessionId = randomUUID();
    this.running = false;
    this.connected = false;
    this.captureBusy = false;
    this.touchTrees = new Map();
    this.installTools();
    require("./lib/files").installFiles(this);
    require("./lib/artifacts").installArtifacts(this);
    require("./lib/video").installVideo(this);
    require("./lib/storage").installStorage(this);
    require("./lib/database").installDatabase(this);
    require("./lib/reflection").installReflection(this);
    require("./lib/descriptors").installDescriptors(this);
    require("./lib/ui-query").installUiQuery(this);
    require("./lib/overlays").installOverlays(this);
  }
  registerTool(descriptor, handler) {
    if (
      !descriptor?.id ||
      !["read", "write", "critical"].includes(descriptor.policy) ||
      typeof handler !== "function"
    )
      throw new Error("Invalid tool descriptor");
    if (this.tools.has(descriptor.id))
      throw new Error(`Duplicate tool: ${descriptor.id}`);
    this.tools.set(descriptor.id, {
      descriptor: {
        name: descriptor.id,
        description: descriptor.id,
        category: "electron",
        argumentsSchema: object({}),
        resultSchema: { type: "object" },
        ...descriptor,
      },
      handler,
    });
    return () => this.tools.delete(descriptor.id);
  }
  permits(policy) {
    const maximum = { disabled: -1, readOnly: 0, readWrite: 1, fullAccess: 2 }[
      this.toolGuard
    ];
    return ({ read: 0, write: 1, critical: 2 }[policy] ?? Infinity) <= maximum;
  }
  installTools() {
    const add = (id, description, policy, properties, handler) =>
      this.registerTool(
        {
          id,
          description,
          policy,
          category: id.split(".")[0],
          argumentsSchema: object({
            windowId: { type: "integer" },
            ...properties,
          }),
        },
        handler,
      );
    add(
      "electron.get_windows",
      "List explicitly attached Electron windows",
      "read",
      {},
      () => ({
        windows: [...this.windows.values()]
          .filter((w) => !w.isDestroyed())
          .map((w) => ({
            windowId: w.id,
            title: w.getTitle(),
            focused: w.isFocused(),
            bounds: w.getContentBounds(),
          })),
      }),
    );
    for (const id of ["ui.get_visual_tree", "dom.get_document"])
      add(
        id,
        "Capture accessible DOM nodes and CSS pixel bounds",
        "read",
        {
          maxNodes: num,
          maxDepth: num,
          source: str,
          rootNodeId: str,
          includeBounds: bool,
          includeComputedStyles: bool,
          includeProperties: bool,
          includeInactivePages: bool,
        },
        (a) => this.runDom(this.window(a), inspectDom, a),
      );
    add(
      "ui.get_screenshot",
      "Capture the attached window using binary artifact transfer",
      "read",
      { format: str, quality: num, maxWidth: num, afterScreenUpdates: bool },
      (a, c) => this.screenshot(a, c),
    );
    add(
      "ui.perform_action",
      "Perform an action on an observed DOM node",
      "write",
      {
        nodeId: str,
        automationId: str,
        action: str,
        value: str,
        text: str,
        replaceExisting: bool,
        index: num,
        checked: bool,
      },
      async (a) => {
        const w = this.window(a),
          action = a.action;
        if (
          ![
            "tap",
            "focus",
            "unfocus",
            "setValue",
            "typeText",
            "toggle",
            "select",
            "selectTab",
          ].includes(action)
        )
          throw new Error("Unsupported action");
        const point = await this.runDom(w, resolveNode, {
          ...a,
          focus: ["focus", "setValue", "typeText"].includes(action),
          replaceExisting: action === "setValue" || a.replaceExisting === true,
        });
        w.focus();
        w.webContents.focus();
        if (point.click) {
          const zoom = w.webContents.getZoomFactor();
          this.click(w, {
            x: Math.round(point.x * zoom),
            y: Math.round(point.y * zoom),
          });
        }
        if (action === "setValue" || action === "typeText")
          await w.webContents.insertText(String(a.value ?? a.text ?? ""));
        return { performed: true, action, nodeId: a.nodeId };
      },
    );
    add(
      "electron.input",
      "Send native input to the selected Electron window",
      "write",
      {
        kind: {
          type: "string",
          enum: [
            "tap",
            "longPress",
            "type",
            "key",
            "button",
            "scroll",
            "swipe",
            "pinch",
          ],
        },
        x: num,
        y: num,
        normalized: bool,
        text: str,
        key: str,
        replaceExisting: bool,
        deltaX: num,
        deltaY: num,
        endX: num,
        endY: num,
        durationMs: num,
        scale: num,
        primaryStartX: num,
        primaryStartY: num,
        secondaryStartX: num,
        secondaryStartY: num,
        primaryEndX: num,
        primaryEndY: num,
        secondaryEndX: num,
        secondaryEndY: num,
      },
      (a) => input(this, a),
    );
    add(
      "ui.dismiss_keyboard",
      "Blur the focused editor without navigating",
      "write",
      {},
      (a) => this.runDom(this.window(a), focusEditor, { dismiss: true }),
    );
    add(
      "ui.submit_keyboard",
      "Submit the focused editor's form with browser validation",
      "write",
      {},
      (a) => this.runDom(this.window(a), focusEditor, { submit: true }),
    );
    add(
      "electron.get_capabilities",
      "Describe available streams and their measurement semantics",
      "read",
      {},
      () => ({
        streams: [
          "screenshots",
          "visualTrees",
          "pointerInput",
          "keyboardEvents",
          "rendererLogs",
          "applicationEvents",
          "lifecycle",
          "metrics",
          "sessionProperties",
          "artifacts",
          ...(this.debuggerWindows.size ? ["rendererNetwork"] : []),
        ],
        metrics: {
          cpu: "Main process CPU percent relative to one logical core",
          memory: "Main process RSS and V8 heap in MiB",
          animation:
            "Mean visible-window requestAnimationFrame callbacks per second; not GPU frame rate",
        },
        network: {
          scope: "Attached renderer webContents",
          windowIds: [...this.debuggerWindows],
          requested: this.options.captureNetwork === true,
          bodies: false,
          headers: false,
          queryStrings: false,
        },
        unavailable: [
          "OS-wide logs",
          "GPU frame timing",
          "microphone recording",
          "mobile motion/location sensors",
        ],
        droppedRecords: this.streams.dropped,
      }),
    );
    add(
      "electron.get_metrics",
      "Read main-process memory and CPU metrics",
      "read",
      {},
      () => ({
        memory: process.memoryUsage(),
        cpu: process.cpuUsage(),
        uptimeSeconds: process.uptime(),
        processes: this.electron.app.getAppMetrics(),
      }),
    );
  }
  window(args = {}) {
    const windows = [...this.windows.values()].filter((w) => !w.isDestroyed());
    const w = args.windowId
      ? windows.find((w) => w.id === args.windowId)
      : windows.length === 1
        ? windows[0]
        : windows.find((w) => w.isFocused());
    if (!w) throw new Error("Select an attached windowId");
    return w;
  }
  async runDom(w, fn, args) {
    const response = await w.webContents.executeJavaScriptInIsolatedWorld(
      1077,
      [
        {
          code: `(() => { try { return { ok: true, value: (${fn.toString()})(${JSON.stringify(args)}) }; } catch (error) { return { ok: false, error: String(error.message) }; } })()`,
        },
      ],
    );
    if (!response.ok) throw new Error(response.error);
    return response.value;
  }
  click(w, point) {
    for (const type of ["mouseDown", "mouseUp"])
      w.webContents.sendInputEvent({
        type,
        ...point,
        button: "left",
        clickCount: 1,
      });
  }
  attachWindow(w) {
    const contents = w.webContents,
      windowId = w.id;
    if (this.windows.has(windowId)) return this.detachers.get(windowId);
    this.windows.set(windowId, w);
    const onConsole = (_e, level, message, line, source) =>
      this.log(message, { level, source, line });
    const onCrash = (_e, details) => {
      this.streams.event("Renderer process gone", "Exception", details);
      if (["crashed", "oom", "abnormal-exit"].includes(details.reason)) {
        this.crashes?.record(
          "renderer_crash",
          new Error(`Renderer ${details.reason}`),
          { ...details, runtime: "chromium", windowId: windowId },
        );
        void this.crashes?.deliver();
      }
    };
    const detachStreams =
      this.options.enabled === true ? this.streams.attach(w) : () => {};
    const alreadyAttached = contents.debugger?.isAttached();
    const detachNetwork =
      this.options.enabled === true && this.options.captureNetwork === true
        ? attachNetwork(
            contents,
            (r) => {
              try {
                this.recordNetworkRequest(r);
              } catch (e) {
                this.diagnostic(e);
              }
            },
            (e) => this.diagnostic(e),
          )
        : () => {};
    if (!alreadyAttached && contents.debugger?.isAttached())
      this.debuggerWindows.add(windowId);
    const onDebuggerDetach = () => this.debuggerWindows.delete(windowId);
    contents.debugger?.on("detach", onDebuggerDetach);
    contents.on("console-message", onConsole);
    contents.on("render-process-gone", onCrash);
    const detach = () => {
      this.touchTrees.get(windowId)?.close();
      this.touchTrees.delete(windowId);
      detachNetwork();
      contents.debugger?.removeListener("detach", onDebuggerDetach);
      detachStreams();
      this.debuggerWindows.delete(windowId);
      this.windows.delete(windowId);
      this.detachers.delete(windowId);
      w.removeListener("closed", detach);
      if (!contents.isDestroyed()) {
        contents.removeListener("console-message", onConsole);
        contents.removeListener("render-process-gone", onCrash);
      }
    };
    this.detachers.set(windowId, detach);
    w.once("closed", detach);
    return detach;
  }
  async start() {
    if (this.running || this.options.enabled !== true) return;
    if (this.electron.app.isPackaged && this.options.allowPackaged !== true)
      throw new Error("Packaged builds require explicit allowPackaged");
    await this.electron.app.whenReady();
    const dir = path.join(this.electron.app.getPath("userData"), "ansight");
    fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
    const file = path.join(dir, "identity.json");
    try {
      this.identity = JSON.parse(fs.readFileSync(file, "utf8"));
    } catch (e) {
      if (e.code !== "ENOENT") throw e;
      this.identity = {
        deviceId: randomUUID(),
        accessToken: randomBytes(32).toString("base64url"),
      };
      fs.writeFileSync(file, JSON.stringify(this.identity), {
        mode: 0o600,
        flag: "wx",
      });
    }
    this.offline = new (require("./lib/offline").OfflineCapture)(this, dir);
    await this.offline.initialize();
    this.crashes = new (require("./lib/crashes").CrashCapture)(this, dir);
    if (
      this.crashes.enabled &&
      this.options.crashCapture?.nativeSignalCapture === true
    )
      this.nativeCrashes =
        new (require("./lib/native-crashes").NativeCrashCapture)(this, dir);
    this.running = true;
    this.streams.start();
    this.captureTimer = setInterval(
      () => this.captureFrame().catch((e) => this.diagnostic(e)),
      Math.max(
        250,
        this.options.captureIntervalMs ||
          this.captureOptions.intervalMilliseconds,
      ),
    );
    this.captureTimer.unref();

    this.quitHandler = (event) => {
      if (this.offline?.options.enabled) {
        event.preventDefault();
        // Resume on a new event-loop turn: a resolved promise can otherwise
        // re-enter Electron's original quit cycle before its state is reset.
        void this.stop()
          .catch((e) => this.diagnostic(e))
          .finally(() => setImmediate(() => this.electron.app.quit()));
      } else void this.stop();
    };
    this.electron.app.once("before-quit", this.quitHandler);
    void this.connect();
  }
  async connect() {
    if (!this.running || this.connecting) return;
    this.connecting = true;
    try {
      const url = await enroll({
        ...this.identity,
        appId: this.options.appId,
        appName: this.options.appName || this.electron.app.getName(),
        processSessionId: this.processSessionId,
        port: this.options.discoveryPort || 45123,
      });
      if (!this.running) return;
      const ws = new WebSocket(url, {
        handshakeTimeout: 3000,
        maxPayload: 4 * 1024 * 1024,
      });
      this.socket = ws;
      ws.on("message", (bytes, binary) => {
        if (!binary) this.receive(bytes).catch((e) => this.diagnostic(e));
      });
      ws.on("error", (e) => this.diagnostic(e));
      ws.on("close", () => {
        if (this.socket !== ws) return;
        this.connected = false;
        for (const coordinator of this.touchTrees.values())
          coordinator.interrupt();

        for (const p of this.pending.values())
          p.reject(new Error("Ansight disconnected"));
        this.pending.clear();
        this.emit("disconnected");
        this.retry();
      });
      await new Promise((resolve, reject) => {
        ws.once("open", resolve);
        ws.once("error", reject);
      });
      await this.control("session.open", {
        clientName: this.options.appName || this.electron.app.getName(),
        configId: `local:${this.options.appId}`,
        customProperties: this.customProperties,
      });
      const policy = await this.control("device.profile", this.profile());
      this.hostCapture = policy?.sessionJpegCapture?.mode === "host";
      this.periodicPaused = false;
      this.connected = true;
      await this.offline.connected();
      await this.streams.connected();
      void this.crashes?.deliver();
      this.emit("connected");
      this.log("Ansight Electron connected");
    } catch (e) {
      this.diagnostic(e);
      this.socket?.terminate();
      this.retry();
    } finally {
      this.connecting = false;
    }
  }
  diagnostic(e) {
    this.emit("diagnostic", e.message);
  }
  retry() {
    if (!this.running) return;
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => this.connect(), 10000);
    this.retryTimer.unref();
  }
  profile() {
    return {
      type: "DeviceAppProfile",
      sessionJpegCaptureControlVersion: 1,
      periodicScreenshotCaptureControlVersion: 1,
      schema: "ansight.device-app-profile.v1",
      sentAt: Date.now(),
      reasonCode: 1,
      profileSeq: 1,
      sdk: {
        name: "Ansight Electron",
        packageId: "@ansight/electron",
        version: "0.1.0",
        language: "JavaScript",
      },
      app: {
        appId: this.options.appId,
        appName: this.options.appName || this.electron.app.getName(),
        processId: process.pid,
        versionName: this.electron.app.getVersion(),
      },
      device: {
        nativeDeviceId: `electron:${this.processSessionId}`,
        osName: process.platform === "darwin" ? "macos" : process.platform,
        osVersion: process.getSystemVersion?.() || os.release(),
        formFactor: "desktop",
        isVirtual: false,
        cpuArch: process.arch,
        cpuCoreCount: os.cpus().length,
        memoryTotalMb: Math.round(os.totalmem() / 1048576),
      },
      runtime: {
        primaryVersion: process.versions.electron,
        engine: { name: "V8", version: process.versions.v8 },
        stack: [
          {
            name: "Electron",
            version: process.versions.electron,
            layer: "framework",
          },
          { name: "Node.js", version: process.versions.node, layer: "runtime" },
          {
            name: "Chromium",
            version: process.versions.chrome,
            layer: "engine",
          },
        ],
      },
      tags: ["electron", "development"],
    };
  }
  send(data) {
    return new Promise((resolve, reject) => {
      if (this.socket?.readyState !== WebSocket.OPEN)
        return reject(new Error("Ansight is disconnected"));
      this.socket.send(
        Buffer.isBuffer(data) ? data : JSON.stringify(data),
        (e) => (e ? reject(e) : resolve()),
      );
    });
  }
  control(action, payload, timeoutMs = 5000) {
    const id = randomUUID();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Control timed out: ${action}`));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (v) => {
          clearTimeout(timer);
          resolve(v);
        },
        reject: (e) => {
          clearTimeout(timer);
          reject(e);
        },
      });
      this.send({ type: "CONTROL_REQ", id, action, payload }).catch((e) => {
        this.pending.delete(id);
        clearTimeout(timer);
        reject(e);
      });
    });
  }
  log(message, details = {}) {
    const type =
      details.level === 3
        ? "Error"
        : details.level === 2
          ? "Warning"
          : details.level === 0
            ? "Debug"
            : "Info";
    this.streams.event(String(message).slice(0, 8192), type, details);
  }
  screenViewed(name, details = {}, channel = 4) {
    if (typeof name !== "string" || !name.trim() || name.trim().length > 1024)
      throw new Error("Screen name must contain 1–1024 characters");
    if (
      !details ||
      typeof details !== "object" ||
      Array.isArray(details) ||
      Object.values(details).some((v) => typeof v !== "string")
    )
      throw new Error("Screen details must be string key/value pairs");
    const encoded = JSON.stringify(details);
    if (Buffer.byteLength(encoded) > 16384)
      throw new Error("Screen details exceed 16 KiB");
    if (
      !Number.isInteger(channel) ||
      !this.streams.channels.some((c) => c.id === channel)
    )
      throw new Error("Screen channel must be registered");
    if (this.options.enabled !== true || this.streams.stopped) return;
    const capturedAtUtc = new Date().toISOString();
    this.currentScreen = {
      name: name.trim(),
      details: structuredClone(details),
      capturedAtUtc,
    };
    this.streams.event(
      name.trim(),
      "ScreenViewed",
      details,
      capturedAtUtc,
      channel,
    );
  }
  setAppLifecycleState(state, changedAtUtc = new Date().toISOString()) {
    if (
      state !== null &&
      !["unknown", "foreground", "background"].includes(state)
    )
      throw new Error("Invalid app lifecycle state");
    if (
      typeof changedAtUtc !== "string" ||
      !Number.isFinite(Date.parse(changedAtUtc))
    )
      throw new Error("Invalid lifecycle timestamp");
    this.streams.lifecycleOverride = state;
    if (state === null) this.streams.lifecycle();
    else this.streams.setLifecycle(state, new Date(changedAtUtc).toISOString());
  }
  get isFramesPerSecondEnabled() {
    return this.streams.framesEnabled;
  }
  enableFramesPerSecond() {
    this.streams.framesEnabled = true;
    return this.streams.configureCapture();
  }
  disableFramesPerSecond() {
    this.streams.framesEnabled = false;
    return this.streams.configureCapture();
  }
  get isTouchCaptureEnabled() {
    return this.streams.touchesEnabled;
  }
  enableTouchCapture() {
    this.streams.touchesEnabled = this.streams.touchesConfigured;
    return this.streams.configureCapture();
  }
  disableTouchCapture() {
    this.streams.touchesEnabled = false;
    return this.streams.configureCapture();
  }
  setTouchCaptureGuard(callback) {
    if (callback != null && typeof callback !== "function")
      throw new Error("Touch guard must be a function or null");
    this.streams.touchGuard = callback ?? null;
    return this.streams.configureCapture();
  }
  recordNetworkRequest(request) {
    if (this.options.enabled !== true || this.streams.stopped)
      return { success: false, message: "Capture is disabled" };
    const normalized = require("./lib/network-record").normalizeRequest(
      request,
      this.options.networkCapture?.redactSensitiveData !== false,
    );
    const sanitizer = this.options.networkCapture?.sanitizer;
    const sanitized = sanitizer
      ? sanitizer(structuredClone(normalized))
      : normalized;
    if (sanitized === null)
      return { success: true, message: "Request excluded by sanitizer" };
    if (sanitized && typeof sanitized.then === "function")
      throw new Error("Network sanitizer must be synchronous");
    const final = require("./lib/network-record").normalizeRequest(
      sanitized,
      this.options.networkCapture?.redactSensitiveData !== false,
    );
    this.streams.enqueue({
      type: "CLIENT_NETWORK_REQUEST",
      sentAtUtc: new Date().toISOString(),
      request: final,
    });
    return { success: true, message: "Network request queued", id: final.id };
  }
  setNetworkCaptureRedactionEnabled(enabled) {
    if (typeof enabled !== "boolean")
      throw new Error("Redaction state must be boolean");
    this.options.networkCapture = {
      ...this.options.networkCapture,
      redactSensitiveData: enabled,
    };
  }
  metric(value, channel = 0) {
    if (!Number.isFinite(value))
      throw new Error("Metric values must be finite");
    if (!Number.isInteger(channel) || channel < 0 || channel > 255)
      throw new Error("Channel ids must be between 0 and 255");
    if (!this.streams.channels.some((c) => c.id === channel)) return false;
    this.streams.enqueue({
      type: "CLIENT_METRICS",
      metrics: [{ channel, value, capturedAtUtc: new Date().toISOString() }],
    });
    return this.options.enabled === true && !this.streams.stopped;
  }
  recordedMetrics() {
    return this.streams.retention.read("metrics");
  }
  recordedEvents() {
    return this.streams.retention.read("events");
  }
  clear() {
    this.streams.retention.clear();
  }
  snapshot() {
    const metrics = this.recordedMetrics(),
      events = this.recordedEvents();
    return {
      initialized: true,
      active: this.running,
      sessionOpen: this.connected,
      metricsRecorded: metrics.length,
      eventsRecorded: events.length,
      lastMetric: metrics.at(-1) ?? null,
      lastEvent: events.at(-1) ?? null,
      frameRateCaptureActive: this.running && this.streams.framesEnabled,
      touchCaptureEnabled: this.streams.touchesEnabled,
      registeredTools: this.tools.size,
      lifecycleState: this.streams.state ?? "unknown",
      currentScreen: this.currentScreen
        ? structuredClone(this.currentScreen)
        : null,
      channels: structuredClone(this.streams.channels),
    };
  }
  event(label, details = {}, type = "Event") {
    this.streams.event(label, type, details);
  }
  async setSessionProperties(properties) {
    if (
      !properties ||
      typeof properties !== "object" ||
      Array.isArray(properties)
    )
      throw new Error("Properties must be an object");
    for (const [group, values] of Object.entries(properties)) {
      if (
        !group.trim() ||
        !values ||
        typeof values !== "object" ||
        Array.isArray(values)
      )
        throw new Error("Each property group must be an object");
      for (const [key, value] of Object.entries(values))
        if (
          !key.trim() ||
          (value !== null &&
            !["string", "boolean", "number"].includes(typeof value)) ||
          (typeof value === "number" && !Number.isFinite(value))
        )
          throw new Error("Session properties must be scalar values");
    }
    this.customProperties = JSON.parse(JSON.stringify(properties));
    await this.offline?.updateProperties();
    if (this.connected)
      await this.control("session.properties", {
        customProperties: this.customProperties,
      });
  }
  async setOfflineCaptureEnabled(enabled) {
    await this.offline?.setEnabled(Boolean(enabled));
  }
  async transferOfflineCaptures() {
    if (!this.offline) throw new Error("SDK has not started");
    await this.offline.transfer();
  }
  get isOfflineCaptureRecording() {
    return this.offline?.isRecording || false;
  }
  get offlineCaptureLastError() {
    return this.offline?.lastError || null;
  }
  async publishArtifact(metadata, data) {
    if (!this.connected)
      throw new Error("Artifact publication requires a connection");
    for (const key of [
      "artifactId",
      "providerId",
      "name",
      "kind",
      "mimeType",
      "fileName",
    ])
      if (typeof metadata[key] !== "string" || !metadata[key].trim())
        throw new Error(`Artifact requires ${key}`);
    if (path.basename(metadata.fileName) !== metadata.fileName)
      throw new Error("Artifact filename must not contain a path");
    const bytes = Buffer.isBuffer(data) ? data : Buffer.from(data);
    if (bytes.length > 64 * 1024 * 1024)
      throw new Error("Electron artifacts are limited to 64 MiB");
    const transferId = randomUUID().replaceAll("-", "");
    await this.control("artifact.publish.begin", {
      schema: "ansight.artifact.publish.v1",
      transferId,
      wireProtocol: "ansight.file-transfer.v1",
      artifact: {
        ...metadata,
        sizeBytes: bytes.length,
        createdAtUtc: new Date().toISOString(),
      },
    });
    try {
      await this.transfer({ transferId, bytes });
      return await this.control("artifact.publish.commit", { transferId });
    } catch (e) {
      await this.control("artifact.publish.abort", { transferId }).catch(
        () => {},
      );
      throw e;
    }
  }
  async receive(bytes) {
    let request;
    try {
      request = JSON.parse(bytes.toString());
    } catch {
      return;
    }
    if (request.type === "CONTROL_RESP") {
      const p = this.pending.get(request.replyTo);
      if (p) {
        this.pending.delete(request.replyTo);
        request.success
          ? p.resolve(request.payload)
          : p.reject(new Error(request.message || "Control failed"));
      }
      return;
    }
    if (request.type === "session.screenshots") {
      this.periodicPaused = request.payload?.paused === true;
      await this.send({
        type: "session.screenshots.result",
        id: randomUUID(),
        replyTo: request.id,
        payload: { paused: this.periodicPaused },
      });
      return;
    }
    if (!["tool.query", "tool.call", "tool.batch"].includes(request.type))
      return;
    const response = {
      id: randomUUID(),
      replyTo: request.id,
      sessionId: request.sessionId,
      capability: "tool.exec",
      sentAt: new Date().toISOString(),
    };
    try {
      if (request.type === "tool.query") {
        const p = request.payload || {};
        let entries = (
          this.toolGuard === "disabled" ? [] : [...this.tools.values()]
        ).map((t) => ({
          ...t.descriptor,
          executable: this.permits(t.descriptor.policy),
        }));
        const revision = createHash("sha256")
          .update(JSON.stringify(entries))
          .digest("hex");
        if (Array.isArray(p.toolIds))
          entries = entries.filter((t) => p.toolIds.includes(t.id));
        if (p.category)
          entries = entries.filter((t) => t.category === p.category);
        if (p.query)
          entries = entries.filter((t) =>
            (t.id + " " + t.description)
              .toLowerCase()
              .includes(String(p.query).toLowerCase()),
          );
        await this.send({
          ...response,
          type: "tool.catalog",
          payload: {
            schema: "ansight.tool-catalog.v3",
            revision,
            tools: entries,
            count: entries.length,
            totalCount: this.tools.size,
          },
        });
        return;
      }
      const context = {
        requestId: request.id,
        sessionId: request.sessionId,
        transfers: [],
      };
      let payload;
      if (request.type === "tool.batch") {
        const p = request.payload || {},
          calls = p.calls;
        if (!Array.isArray(calls) || calls.length < 1 || calls.length > 32)
          throw new Error("Batch requires 1–32 calls");
        const results = [];
        for (const [index, call] of calls.entries()) {
          try {
            results.push({
              ...(await this.execute(call, context)),
              index,
              callId: call.callId || null,
            });
          } catch (e) {
            results.push({
              index,
              toolId: call?.toolId,
              success: false,
              error: {
                code: e.code || "electron_tool_failed",
                message: e.message,
              },
            });
            if (!p.continueOnError) break;
          }
        }
        payload = {
          success: results.every((r) => r.success),
          completed: results.length,
          requested: calls.length,
          stoppedEarly: results.length < calls.length,
          results,
        };
      } else payload = await this.execute(request.payload || {}, context);
      await this.send({
        ...response,
        type:
          request.type === "tool.batch" ? "tool.batch.result" : "tool.result",
        payload,
      });
      for (const transfer of context.transfers) await this.transfer(transfer);
    } catch (e) {
      await this.send({
        ...response,
        type: "tool.error",
        payload: {
          code: e.code || "electron_tool_failed",
          message: e.message,
          retryable: false,
        },
      });
    }
  }
  async execute(p, context) {
    const tool = this.tools.get(p.toolId);
    if (!tool) throw new Error(`Unknown tool: ${p.toolId}`);
    if (!this.permits(tool.descriptor.policy))
      throw new Error(
        `${tool.descriptor.policy} tools are disabled by toolGuard`,
      );
    let args = p.arguments || {};
    if (typeof args === "string") args = JSON.parse(args);
    if (!args || typeof args !== "object" || Array.isArray(args))
      throw new Error("Arguments must be an object");
    const result = await tool.handler(args, context);
    const response = { toolId: p.toolId, success: true, result };
    if (p.after) {
      const delay = Math.max(
        0,
        Math.min(2000, Number(p.after.delayMilliseconds) || 0),
      );
      if (delay) await new Promise((r) => setTimeout(r, delay));
      response.evidence = {};
      for (const name of p.after.include || ["visualTree"]) {
        const toolId =
          name === "screenshot"
            ? "ui.get_screenshot"
            : ["tree", "visualTree", "visual_tree"].includes(name)
              ? "ui.get_visual_tree"
              : null;
        if (!toolId || toolId === p.toolId) continue;
        try {
          response.evidence[name] = await this.execute(
            {
              toolId,
              arguments:
                p.after[
                  toolId === "ui.get_screenshot"
                    ? "screenshotArguments"
                    : "visualTreeArguments"
                ] || {},
            },
            context,
          );
        } catch (e) {
          response.evidence[name] = {
            toolId,
            success: false,
            error: { message: e.message },
          };
        }
      }
    }
    return response;
  }
  async screenshot(a, c) {
    let image = await this.window(a).webContents.capturePage();
    if (image.isEmpty()) throw new Error("Window capture is empty");
    if (a.maxWidth && image.getSize().width > a.maxWidth)
      image = image.resize({
        width: Math.max(1, Math.min(8192, Math.round(a.maxWidth))),
      });
    const format = a.format === "jpeg" ? "jpeg" : "png",
      size = image.getSize(),
      bytes =
        format === "png"
          ? image.toPNG()
          : image.toJPEG(Math.max(1, Math.min(100, Number(a.quality) || 80)));
    const transferId = randomUUID().replaceAll("-", "");
    c.transfers.push({ transferId, bytes });
    return {
      platform: process.platform,
      capturedAtUtc: new Date().toISOString(),
      format,
      ...size,
      annotationApplied: false,
      deliveryMode: "websocket_binary",
      wireProtocol: "ansight.file-transfer.v1",
      transferId,
      downloadId: c.requestId,
      sizeBytes: bytes.length,
      fileName: `screenshot-${transferId}.${format}`,
      mimeType: `image/${format}`,
      artifactKind: "screenshot",
      status: "queued",
    };
  }
  async transfer({ transferId, bytes, chunkBytes = 65536 }) {
    let seq = 0,
      offset = 0;
    for (; offset < bytes.length; ) {
      const chunk = bytes.subarray(offset, offset + chunkBytes);
      await this.send(fileFrame(transferId, 1, seq++, offset, chunk));
      offset += chunk.length;
    }
    await this.send(fileFrame(transferId, 2, seq, offset, Buffer.alloc(0)));
  }
  observeTouch(w, touch) {
    if (
      !this.connected ||
      !this.running ||
      this.options.sessionJpegCapture === false ||
      this.captureOptions.mode !== "screenshotWithVisualTreeOnTouch"
    )
      return;
    let coordinator = this.touchTrees.get(w.id);
    if (!coordinator) {
      coordinator = new (require("./lib/touch-trees").TouchTreeCoordinator)(
        async (trigger) => {
          if (
            !this.connected ||
            !this.running ||
            w.isDestroyed() ||
            !w.isVisible() ||
            w.isMinimized() ||
            w.webContents.isCrashed?.()
          )
            return;
          try {
            const tree = await this.runDom(w, inspectDom, {});
            tree.captureTrigger = { kind: "touch", ...trigger };
            const count = (n) =>
              1 + (n.children || []).reduce((sum, c) => sum + count(c), 0);
            this.streams.enqueue({
              type: "CLIENT_VISUAL_TREE",
              snapshotId: randomUUID(),
              capturedAtUtc: tree.capturedAtUtc,
              captureTrigger: "touch",
              ...trigger,
              visualTreeFormat: tree.format,
              visualTreeKind: "dom",
              runtimePlatform: "web",
              source: "sdk.touchCapture",
              nodeCount: tree.root ? count(tree.root) : 0,
              truncated: tree.truncated,
              payload: tree,
            });
          } catch (e) {
            this.diagnostic(e);
          }
        },
        this.captureOptions.intervalMilliseconds,
      );
      this.touchTrees.set(w.id, coordinator);
    }
    coordinator.observe(touch);
  }
  async captureFrame() {
    if (
      (!this.connected && !this.offline?.isRecording) ||
      (this.connected && this.periodicPaused) ||
      (this.connected && this.hostCapture) ||
      this.options.sessionJpegCapture === false ||
      this.captureBusy ||
      this.socket?.bufferedAmount > 2097152 ||
      !this.windows.size
    )
      return;
    this.captureBusy = true;
    try {
      const w = this.window();
      if (
        w.webContents.isCrashed?.() ||
        (!this.connected && (!w.isVisible() || w.isMinimized()))
      )
        return;
      const capturedAt = Date.now();
      let image = await w.webContents.capturePage();
      if (!image.isEmpty()) {
        const maxWidth = this.captureOptions.maxWidth;
        if (maxWidth && image.getSize().width > maxWidth)
          image = image.resize({ width: maxWidth });
        const quality = Math.max(1, Math.min(100, this.captureOptions.quality));
        const { width, height } = image.getSize();
        const jpeg = image.toJPEG(quality);
        if (this.connected)
          await this.send(jpegFrame(jpeg, width, height, quality, capturedAt));
        else
          this.offline.screenshot(jpeg, {
            capturedAtUtc: new Date(capturedAt).toISOString(),
            width,
            height,
            quality,
          });
        if (
          this.captureOptions.mode === "screenshotOnly" ||
          (this.connected &&
            this.captureOptions.mode === "screenshotWithVisualTreeOnTouch")
        )
          return;
        const tree = await this.runDom(w, inspectDom, {});
        const count = (n) =>
          1 + (n.children || []).reduce((sum, c) => sum + count(c), 0);
        this.streams.enqueue({
          type: "CLIENT_VISUAL_TREE",
          snapshotId: randomUUID(),
          capturedAtUtc: tree.capturedAtUtc,
          screenshotCapturedAtUtc: new Date(capturedAt).toISOString(),
          visualTreeFormat: tree.format,
          visualTreeKind: "dom",
          runtimePlatform: "web",
          source: "electron.sdk",
          nodeCount: tree.root ? count(tree.root) : 0,
          truncated: tree.truncated,
          payload: tree,
        });
      }
    } finally {
      this.captureBusy = false;
    }
  }
  async stop() {
    this.running = false;
    this.connected = false;
    this.streams.stop();
    this.video.dispose();
    this.video.active = null;
    clearTimeout(this.retryTimer);
    clearInterval(this.captureTimer);
    this.crashes?.stop();
    const offlineStop = this.offline?.stop();
    const overlayStop = this.clearDiagnosticOverlays();
    if (this.quitHandler)
      this.electron.app.removeListener("before-quit", this.quitHandler);
    for (const p of this.pending.values())
      p.reject(new Error("Ansight stopped"));
    this.pending.clear();
    this.socket?.terminate();
    for (const detach of [...this.detachers.values()]) detach();
    await offlineStop;
    await overlayStop;
  }
}
function createAnsight(options) {
  return new AnsightElectron(options);
}
module.exports = { createAnsight, AnsightElectron };
