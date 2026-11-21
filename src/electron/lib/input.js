"use strict";
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
function focusEditor({
  replaceExisting = false,
  submit = false,
  dismiss = false,
}) {
  const el = document.activeElement;
  if (dismiss) {
    el?.blur();
    return { performed: true };
  }
  if (submit) {
    const form = el?.form;
    if (!form) throw new Error("Focused editor has no form to submit");
    form.requestSubmit();
    return { performed: true };
  }
  if (
    !el ||
    !(["INPUT", "TEXTAREA"].includes(el.tagName) || el.isContentEditable)
  )
    throw new Error("No editable element is focused");
  if (el.disabled || el.readOnly)
    throw new Error("Focused editor is not editable");
  if (replaceExisting) {
    if (typeof el.select === "function") el.select();
    else {
      const range = document.createRange();
      range.selectNodeContents(el);
      const selection = getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    }
  }
  return { performed: true };
}
function coordinate(value, size, normalized) {
  if (!Number.isFinite(value) || value < 0 || value > (normalized ? 1 : size))
    throw new Error("Input coordinates must be finite and inside the window");
  return Math.max(
    0,
    Math.min(size - 1, Math.round(normalized ? value * size : value)),
  );
}
async function input(sdk, args) {
  const w = sdk.window(args),
    wc = w.webContents,
    b = w.getContentBounds();
  w.focus();
  wc.focus();
  const point = () => ({
    x: coordinate(args.x, b.width, args.normalized),
    y: coordinate(args.y, b.height, args.normalized),
  });
  switch (args.kind) {
    case "tap":
      sdk.click(w, point());
      break;
    case "longPress": {
      const ms = args.durationMs ?? 800;
      if (!Number.isInteger(ms) || ms < 500 || ms > 10000)
        throw new Error("Long press duration must be 500–10000 ms");
      const p = point();
      wc.sendInputEvent({
        type: "mouseDown",
        ...p,
        button: "left",
        clickCount: 1,
      });
      try {
        await delay(ms);
      } finally {
        if (!wc.isDestroyed())
          wc.sendInputEvent({
            type: "mouseUp",
            ...p,
            button: "left",
            clickCount: 1,
          });
      }
      break;
    }
    case "type":
      if (typeof args.text !== "string" || args.text.length > 100000)
        throw new Error("Text must be a string of at most 100000 characters");
      await sdk.runDom(w, focusEditor, {
        replaceExisting: args.replaceExisting === true,
      });
      await wc.insertText(args.text);
      break;
    case "key":
    case "button": {
      const key = String(args.key || "");
      if (key.toLowerCase() === "back") {
        if (!wc.canGoBack())
          throw new Error("No browser history entry to return to");
        wc.goBack();
        break;
      }
      const keys = {
        enter: "Enter",
        escape: "Escape",
        tab: "Tab",
        backspace: "Backspace",
        delete: "Delete",
        arrowup: "Up",
        arrowdown: "Down",
        arrowleft: "Left",
        arrowright: "Right",
      };
      const code = keys[key.toLowerCase()];
      if (!code) throw new Error("Unsupported key");
      for (const type of code === "Enter"
        ? ["keyDown", "char", "keyUp"]
        : ["keyDown", "keyUp"])
        wc.sendInputEvent({ type, keyCode: code });
      break;
    }
    case "scroll":
    case "swipe": {
      const p = point();
      let dx = args.deltaX ?? 0,
        dy = args.deltaY ?? 0;
      if (args.kind === "swipe") {
        dx = coordinate(args.endX, b.width, args.normalized) - p.x;
        dy = coordinate(args.endY, b.height, args.normalized) - p.y;
      }
      if (
        !Number.isFinite(dx) ||
        !Number.isFinite(dy) ||
        Math.abs(dx) > 10000 ||
        Math.abs(dy) > 10000
      )
        throw new Error("Invalid scroll distance");
      wc.sendInputEvent({
        type: "mouseWheel",
        ...p,
        deltaX: dx,
        deltaY: dy,
        canScroll: true,
      });
      await delay(60);
      break;
    }
    case "pinch": {
      const p = point();
      let scale = args.scale;
      if (scale == null) {
        const values = [
          "primaryStartX",
          "primaryStartY",
          "secondaryStartX",
          "secondaryStartY",
          "primaryEndX",
          "primaryEndY",
          "secondaryEndX",
          "secondaryEndY",
        ];
        if (
          values.some(
            (k) => !Number.isFinite(args[k]) || args[k] < 0 || args[k] > 1,
          )
        )
          throw new Error("Invalid pinch coordinates");
        const distance = (prefix) =>
          Math.hypot(
            (args[`primary${prefix}X`] - args[`secondary${prefix}X`]) * b.width,
            (args[`primary${prefix}Y`] - args[`secondary${prefix}Y`]) *
              b.height,
          );
        const start = distance("Start");
        if (start <= 0) throw new Error("Pinch start points must be distinct");
        scale = distance("End") / start;
      }
      if (!Number.isFinite(scale) || scale < 0.1 || scale > 10)
        throw new Error("Pinch scale must be 0.1–10");
      const already = wc.debugger.isAttached();
      if (already && !sdk.debuggerWindows.has(w.id))
        throw new Error("Debugger is owned by another client");
      if (!already) wc.debugger.attach("1.3");
      try {
        await wc.debugger.sendCommand("Input.synthesizePinchGesture", {
          x: p.x,
          y: p.y,
          scaleFactor: scale,
          gestureSourceType: "touch",
        });
      } finally {
        if (!already && wc.debugger.isAttached()) wc.debugger.detach();
      }
      break;
    }
    default:
      throw new Error("Unsupported input kind");
  }
  return { performed: true, kind: args.kind, windowId: w.id };
}
module.exports = { input, focusEditor, coordinate };
