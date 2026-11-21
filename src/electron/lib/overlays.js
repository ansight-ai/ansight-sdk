"use strict";
const { inspectDom } = require("./dom");
function overlayOperation(a) {
  const state = (globalThis.__ansightOverlayState ||= {
    items: new Map(),
    host: null,
  });
  const stamp = () => ({
    platform: "web",
    capturedAtUtc: new Date().toISOString(),
  });
  const describe = (item) => ({
    ...item.data,
    remainingMs:
      item.expires === null ? null : Math.max(0, item.expires - Date.now()),
  });
  function paint() {
    if (!state.items.size) {
      state.host?.remove();
      state.host = null;
      removeEventListener("resize", state.paint);
      return;
    }
    if (!state.host) {
      const host = document.createElement("div");
      host.setAttribute("aria-hidden", "true");
      host.style.cssText =
        "all:initial!important;position:fixed!important;inset:0!important;pointer-events:none!important;z-index:2147483647!important;display:block!important;";
      const shadow = host.attachShadow({ mode: "closed" }),
        canvas = document.createElement("canvas");
      canvas.style.cssText =
        "width:100%;height:100%;display:block;pointer-events:none";
      shadow.append(canvas);
      document.documentElement.append(host);
      state.host = host;
      state.canvas = canvas;
      state.paint = paint;
      addEventListener("resize", paint);
    }
    const canvas = state.canvas;
    canvas.width = Math.min(4096, Math.ceil(innerWidth * devicePixelRatio));
    canvas.height = Math.min(4096, Math.ceil(innerHeight * devicePixelRatio));
    const c = canvas.getContext("2d");
    c.scale(canvas.width / innerWidth, canvas.height / innerHeight);
    const css = (color) =>
      color ? `#${color.slice(3)}${color.slice(1, 3)}` : "transparent";
    for (const item of state.items.values())
      for (const r of item.data.rects) {
        const s = item.data.style;
        c.beginPath();
        c.roundRect(
          r.x,
          r.y,
          r.width,
          r.height,
          Math.min(s.cornerRadius, r.width / 2, r.height / 2),
        );
        if (s.fillColor) {
          c.fillStyle = css(s.fillColor);
          c.fill();
        }
        c.strokeStyle = css(s.strokeColor);
        c.lineWidth = s.strokeWidth;
        if (s.strokeWidth) c.stroke();
        if (r.label) {
          c.font = "12px sans-serif";
          c.fillStyle = css(s.strokeColor);
          c.fillText(r.label, r.x + 3, Math.max(14, r.y - 4));
        }
      }
  }
  function remove(id) {
    const item = state.items.get(id);
    if (item) {
      clearTimeout(item.timer);
      state.items.delete(id);
    }
    return item;
  }
  for (const [id, item] of state.items)
    if (item.expires !== null && item.expires <= Date.now()) remove(id);
  const matches = (item) =>
    a.metadataKey === undefined ||
    (Object.hasOwn(item.data.metadata, a.metadataKey) &&
      (a.metadataValue === undefined ||
        String(item.data.metadata[a.metadataKey]).toLowerCase() ===
          String(a.metadataValue).toLowerCase()));
  if (a.operation === "query" || a.operation === "clear") {
    const items = [...state.items.values()].filter(matches);
    if (a.operation === "clear") for (const item of items) remove(item.data.id);
    paint();
    return { ...stamp(), count: items.length, overlays: items.map(describe) };
  }
  const existing = state.items.get(a.overlayId);
  if (a.operation === "get") {
    paint();
    if (!existing) throw new Error("Overlay not found");
    return { ...stamp(), overlay: describe(existing) };
  }
  if (a.operation === "remove") {
    remove(a.overlayId);
    paint();
    return {
      ...stamp(),
      overlayId: a.overlayId,
      removed: !!existing,
      overlay: existing ? describe(existing) : null,
    };
  }
  if (a.operation === "update" && !existing)
    throw new Error("Overlay not found");
  if (!existing && state.items.size >= 32)
    throw new Error("At most 32 overlays may be active");
  const before = existing?.data,
    own = (key) => Object.hasOwn(a, key);
  const num = (value, fallback, min, max) => {
    const n = value ?? fallback;
    if (typeof n !== "number" || !Number.isFinite(n) || n < min || n > max)
      throw new Error("Invalid overlay numeric value");
    return n;
  };
  function color(value, allowNone) {
    if (
      allowNone &&
      (value == null ||
        ["none", "transparent"].includes(String(value).toLowerCase()))
    )
      return null;
    const names = {
      red: "#FF0000",
      green: "#008000",
      blue: "#0000FF",
      yellow: "#FFFF00",
      white: "#FFFFFF",
      black: "#000000",
      orange: "#FFA500",
      purple: "#800080",
      cyan: "#00FFFF",
      magenta: "#FF00FF",
      gray: "#808080",
    };
    let v = names[String(value).toLowerCase()] || value;
    if (
      typeof v !== "string" ||
      !/^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.test(v)
    )
      throw new Error("Invalid overlay color");
    v = v.slice(1);
    if (v.length <= 4) v = [...v].map((x) => x + x).join("");
    if (v.length === 6) v = "FF" + v;
    return "#" + v.toUpperCase();
  }
  const rects = own("rects")
    ? a.rects
    : own("x") || own("y") || own("width") || own("height")
      ? [a]
      : before?.rects;
  if (!Array.isArray(rects) || !rects.length || rects.length > 128)
    throw new Error("Overlay requires 1–128 rectangles");
  const normalized = rects.map((r) => ({
    x: num(r.x, null, -1e7, 1e7),
    y: num(r.y, null, -1e7, 1e7),
    width: num(r.width, null, Number.MIN_VALUE, 1e7),
    height: num(r.height, null, Number.MIN_VALUE, 1e7),
    label: r.label == null ? null : String(r.label).slice(0, 256),
  }));
  const duration = num(a.durationMs, before?.durationMs ?? 5000, 0, 600000);
  if (!Number.isInteger(duration))
    throw new Error("durationMs must be an integer");
  const mode = a.metadataMode ?? "merge";
  if (!["merge", "replace", "clear"].includes(mode))
    throw new Error("Invalid metadataMode");
  const incoming = a.metadata ?? {};
  if (typeof incoming !== "object" || Array.isArray(incoming))
    throw new Error("metadata must be an object");
  const metadata =
    mode === "clear"
      ? {}
      : mode === "replace"
        ? { ...incoming }
        : { ...before?.metadata, ...incoming };
  if (Object.keys(metadata).length > 16)
    throw new Error("At most 16 metadata entries are allowed");
  for (const [key, value] of Object.entries(metadata))
    if (
      !key ||
      key.length > 64 ||
      (!["string", "number", "boolean"].includes(typeof value) &&
        value !== null) ||
      (typeof value === "string" && value.length > 256) ||
      (typeof value === "number" && !Number.isFinite(value))
    )
      throw new Error("Overlay metadata must contain bounded scalar values");
  const expires =
    before && !own("durationMs")
      ? existing.expires
      : duration
        ? Date.now() + duration
        : null;
  const style = {
    strokeColor: color(
      a.strokeColor ?? before?.style.strokeColor ?? "#FF3B30",
      false,
    ),
    fillColor: own("fillColor")
      ? color(a.fillColor, true)
      : (before?.style.fillColor ?? null),
    strokeWidth: num(a.strokeWidth, before?.style.strokeWidth ?? 2, 0, 128),
    cornerRadius: num(a.cornerRadius, before?.style.cornerRadius ?? 3, 0, 256),
  };
  if (style.strokeWidth === 0 && !style.fillColor)
    throw new Error("Overlay must have a visible stroke or fill");
  const data = {
    id: a.overlayId,
    platform: "web",
    createdAtUtc: before?.createdAtUtc ?? new Date().toISOString(),
    expiresAtUtc: expires === null ? null : new Date(expires).toISOString(),
    durationMs: duration,
    transient: duration > 0,
    inputTransparent: true,
    coordinateSpace: "window",
    style,
    rects: normalized,
    metadata,
  };
  remove(a.overlayId);
  const item = { data, expires };
  state.items.set(a.overlayId, item);
  if (expires !== null)
    item.timer = setTimeout(
      () => {
        remove(a.overlayId);
        paint();
      },
      Math.max(0, expires - Date.now()),
    );
  paint();
  return { ...stamp(), overlay: describe(item) };
}
function installOverlays(sdk) {
  const windows = new Set(),
    { randomUUID } = require("node:crypto");
  const string = { type: "string" },
    number = { type: "number" };
  const mutation = {
    overlayId: string,
    nodeId: string,
    rects: { type: "array" },
    x: number,
    y: number,
    width: number,
    height: number,
    label: string,
    coordinateSpace: { type: "string", enum: ["window", "visualTree"] },
    strokeColor: string,
    fillColor: { type: ["string", "null"] },
    strokeWidth: number,
    cornerRadius: number,
    durationMs: { type: "integer" },
    metadata: { type: "object" },
  };
  const filters = { metadataKey: string, metadataValue: string };
  for (const [operation, suffix] of [
    ["show", "show_overlay"],
    ["get", "get_overlay"],
    ["query", "query_overlays"],
    ["update", "update_overlay"],
    ["remove", "remove_overlay"],
    ["clear", "clear_overlays"],
  ]) {
    sdk.registerTool(
      {
        id: `ui.${suffix}`,
        category: "ui",
        policy: ["get", "query"].includes(operation) ? "read" : "write",
        description:
          "Input-transparent diagnostic rectangles in the attached renderer viewport, using native Ansight overlay geometry, style and metadata contracts.",
        argumentsSchema: {
          type: "object",
          properties: {
            windowId: { type: "integer" },
            ...(["show", "update"].includes(operation)
              ? {
                  ...mutation,
                  ...(operation === "update"
                    ? {
                        metadataMode: {
                          type: "string",
                          enum: ["merge", "replace", "clear"],
                        },
                      }
                    : {}),
                }
              : ["query", "clear"].includes(operation)
                ? filters
                : { overlayId: string }),
          },
          additionalProperties: false,
        },
      },
      async (args) => {
        const a = { ...args, operation },
          w = sdk.window(a);
        if (
          a.coordinateSpace &&
          !["window", "visualTree"].includes(a.coordinateSpace)
        )
          throw new Error("Invalid coordinateSpace");
        if (operation === "show") a.overlayId ??= randomUUID();
        if (
          !["query", "clear"].includes(operation) &&
          (typeof a.overlayId !== "string" ||
            !a.overlayId ||
            a.overlayId.length > 256)
        )
          throw new Error("overlayId is required");
        if (a.nodeId) {
          if (a.rects || a.x !== undefined)
            throw new Error("Use nodeId or rectangle coordinates");
          const tree = await sdk.runDom(w, inspectDom, {});
          const find = (n) =>
            n?.id === a.nodeId
              ? n
              : (n?.children || []).map(find).find(Boolean);
          const node = find(tree.root);
          if (!node) throw new Error("Overlay node no longer exists");
          a.rects = [{ ...node.bounds, label: a.label }];
        }
        const result = await sdk.runDom(w, overlayOperation, a);
        if (operation === "show" || operation === "update") {
          if (!windows.has(w)) {
            windows.add(w);
            w.once("closed", () => windows.delete(w));
          }
        }
        return result;
      },
    );
  }
  sdk.clearDiagnosticOverlays = async () => {
    await Promise.allSettled(
      [...windows]
        .filter((w) => !w.isDestroyed())
        .map((w) => sdk.runDom(w, overlayOperation, { operation: "clear" })),
    );
    windows.clear();
  };
}
module.exports = { installOverlays, overlayOperation };
