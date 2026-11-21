"use strict";
// This code executes in an isolated renderer world. It cannot access Node or IPC.
function observeRenderer(options = {}) {
  if (globalThis.__ansightObservation) {
    globalThis.__ansightObservation.configure(options);
    return;
  }
  const state = {
    events: [],
    frames: 0,
    dropped: 0,
    started: performance.now(),
    raf: 0,
    listeners: [],
    captureFrames: false,
    captureTouches: false,
  };
  globalThis.__ansightObservation = state;
  const append = (entry) => {
    if (state.events.length >= 500) {
      state.events.shift();
      state.dropped++;
    }
    state.events.push(entry);
  };
  for (const [name, action] of [
    ["pointerdown", 0],
    ["pointermove", 1],
    ["pointerup", 2],
    ["pointercancel", 3],
  ]) {
    const handler = (event) => {
      if (!state.captureTouches || (name === "pointermove" && !event.buttons))
        return;
      append({
        kind: "pointer",
        action,
        pointerId: event.pointerId,
        x: event.clientX,
        y: event.clientY,
        pointerType: event.pointerType,
        buttons: event.buttons,
        trusted: event.isTrusted,
        capturedAtUtc: new Date(
          performance.timeOrigin + event.timeStamp,
        ).toISOString(),
        width: innerWidth,
        height: innerHeight,
        scale: devicePixelRatio,
      });
    };
    addEventListener(name, handler, { capture: true, passive: true });
    state.listeners.push([name, handler]);
  }
  const error = (e) =>
    append({
      kind: "error",
      label: "Renderer error",
      details: {
        message: String(e.message || "Unknown error").slice(0, 2048),
        source: e.filename,
        line: e.lineno,
      },
      capturedAtUtc: new Date().toISOString(),
    });
  addEventListener("error", error);
  state.listeners.push(["error", error]);
  const rejection = (e) =>
    append({
      kind: "error",
      label: "Unhandled renderer rejection",
      details: {
        message: String(
          e.reason?.message || e.reason || "Unknown rejection",
        ).slice(0, 2048),
      },
      capturedAtUtc: new Date().toISOString(),
    });
  addEventListener("unhandledrejection", rejection);
  state.listeners.push(["unhandledrejection", rejection]);
  function frame() {
    state.frames++;
    state.raf = requestAnimationFrame(frame);
  }
  state.configure = (config) => {
    const frames = config.frames !== false,
      touches = config.touches !== false;
    if (frames !== state.captureFrames) {
      cancelAnimationFrame(state.raf);
      state.frames = 0;
      state.started = performance.now();
      state.captureFrames = frames;
      if (frames) state.raf = requestAnimationFrame(frame);
    }
    state.captureTouches = touches;
    if (!touches)
      state.events = state.events.filter((e) => e.kind !== "pointer");
  };
  state.configure(options);
}
function drainRenderer() {
  const state = globalThis.__ansightObservation;
  if (!state) return null;
  const now = performance.now(),
    result = {
      events: state.events.splice(0),
      frames: state.frames,
      elapsedMs: now - state.started,
      dropped: state.dropped,
      visible: document.visibilityState === "visible",
    };
  state.frames = 0;
  state.dropped = 0;
  state.started = now;
  return result;
}
function stopRenderer() {
  const state = globalThis.__ansightObservation;
  if (!state) return;
  cancelAnimationFrame(state.raf);
  for (const [name, handler] of state.listeners)
    removeEventListener(name, handler, true);
  // Error listeners were installed without capture.
  for (const [name, handler] of state.listeners)
    removeEventListener(name, handler, false);
  delete globalThis.__ansightObservation;
}
module.exports = { observeRenderer, drainRenderer, stopRenderer };
