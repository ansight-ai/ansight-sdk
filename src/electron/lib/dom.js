"use strict";
// Runs in a dedicated isolated world. No Node APIs or IPC are exposed to the page.
function inspectDom(options = {}) {
  const state =
    globalThis.__ansightDom ||
    (globalThis.__ansightDom = {
      ids: new WeakMap(),
      next: 0,
      epoch: `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
    });
  const types = [],
    names = new Map();
  let count = 0,
    truncated = false;
  const maxNodes = Math.max(
    1,
    Math.min(Number(options.maxNodes) || 1500, 5000),
  );
  const maxDepth = Math.max(0, Math.min(Number(options.maxDepth ?? 40), 100));
  function visit(el, depth) {
    if (
      el === globalThis.__ansightOverlayState?.host ||
      ["SCRIPT", "STYLE", "NOSCRIPT"].includes(el.tagName)
    )
      return null;
    if (count >= maxNodes || depth > maxDepth) {
      truncated = true;
      return null;
    }
    const rect = el.getBoundingClientRect(),
      style = getComputedStyle(el);
    const hidden = style.display === "none" || style.visibility === "hidden";
    if (hidden && !options.includeInactivePages) return null;
    count++;
    if (!state.ids.has(el))
      state.ids.set(el, `dom-${state.epoch}-${++state.next}`);
    const tag = el.tagName.toLowerCase();
    if (!names.has(tag)) {
      const key = types.length;
      names.set(tag, key);
      types.push(tag);
    }
    const role =
      el.getAttribute("role") ||
      {
        button: "button",
        a: "link",
        input:
          {
            checkbox: "checkbox",
            radio: "radio",
            button: "button",
            submit: "button",
            range: "slider",
          }[el.type] || "textbox",
        textarea: "textbox",
        select: "combobox",
      }[tag] ||
      tag;
    const editable =
      (tag === "input" &&
        [
          "text",
          "search",
          "email",
          "url",
          "tel",
          "password",
          "number",
        ].includes(el.type)) ||
      tag === "textarea" ||
      el.isContentEditable;
    const secret =
      el.type === "password" || !!el.closest("[data-ansight-private]");
    const text = secret
      ? ""
      : Array.from(el.childNodes)
          .filter((n) => n.nodeType === 3)
          .map((n) => n.textContent)
          .join(" ")
          .trim()
          .slice(0, 512);
    const label =
      el.getAttribute("aria-label") ||
      (el.labels &&
        Array.from(el.labels)
          .map((n) => n.textContent)
          .join(" ")) ||
      el.getAttribute("title") ||
      el.getAttribute("placeholder") ||
      text;
    const visible =
      !hidden &&
      rect.width > 0 &&
      rect.height > 0 &&
      rect.bottom > 0 &&
      rect.right > 0 &&
      rect.top < innerHeight &&
      rect.left < innerWidth;
    const enabled = !el.disabled && el.getAttribute("aria-disabled") !== "true";
    const actions = editable
      ? [
          "tap",
          "focus",
          "unfocus",
          ...(!el.readOnly ? ["setValue", "typeText"] : []),
        ]
      : [
            "button",
            "link",
            "checkbox",
            "radio",
            "switch",
            "tab",
            "combobox",
          ].includes(role)
        ? ["tap", "focus", "unfocus"]
        : [];
    if (["checkbox", "switch"].includes(role)) actions.push("toggle");
    if (role === "tab") actions.push("selectTab");
    if (
      tag === "select" ||
      role === "radio" ||
      el.getAttribute("role") === "option"
    )
      actions.push("select");
    const children = Array.from(el.children)
      .map((c) => visit(c, depth + 1))
      .filter(Boolean);
    return {
      id: state.ids.get(el),
      typeId: names.get(tag),
      automationId:
        el.getAttribute("data-testid") ||
        el.getAttribute("data-automation-id") ||
        el.id ||
        null,
      role,
      label: secret ? "[private]" : label.slice(0, 512),
      visible,
      enabled,
      interactable: visible && enabled && actions.length > 0,
      focusable: el.tabIndex >= 0,
      supportedActions: actions,
      ...(options.includeBounds === false
        ? {}
        : {
            bounds: {
              x: rect.x,
              y: rect.y,
              width: rect.width,
              height: rect.height,
            },
          }),
      ...(options.includeComputedStyles
        ? {
            computedStyles: {
              display: style.display,
              visibility: style.visibility,
              opacity: style.opacity,
              color: style.color,
              backgroundColor: style.backgroundColor,
              fontSize: style.fontSize,
              fontFamily: style.fontFamily,
              overflow: style.overflow,
              position: style.position,
            },
          }
        : {}),
      ...(options.includeProperties
        ? {
            properties: {
              tagName: tag,
              inputType: tag === "input" ? el.type : null,
              readOnly: !!el.readOnly,
              checked: secret
                ? null
                : typeof el.checked === "boolean"
                  ? el.checked
                  : null,
              selectedIndex: secret
                ? null
                : tag === "select"
                  ? el.selectedIndex
                  : null,
              ariaExpanded: el.getAttribute("aria-expanded"),
              ariaSelected: el.getAttribute("aria-selected"),
              contentEditable: el.isContentEditable,
            },
          }
        : {}),
      visual: {
        text,
        value:
          editable && !secret
            ? String(el.value ?? el.textContent).slice(0, 512)
            : undefined,
      },
      children,
      childCount: children.length,
    };
  }
  let rootElement = document.documentElement;
  if (options.rootNodeId) {
    rootElement = Array.from(document.querySelectorAll("*")).find(
      (el) => state.ids.get(el) === options.rootNodeId,
    );
    if (!rootElement)
      throw new Error("Subtree node no longer exists; capture a fresh tree");
  }
  const root = visit(rootElement, 0);
  return {
    format: "ansight.dom.visual-tree.compact.v2",
    platform: "web",
    source: "electron-dom",
    adapter: "@ansight/electron",
    snapshotId: `${Date.now()}`,
    capturedAtUtc: new Date().toISOString(),
    types,
    root,
    truncated,
    coordinateSpace: {
      x: 0,
      y: 0,
      source: "dom.viewport",
      width: innerWidth,
      height: innerHeight,
    },
  };
}
function resolveNode(args) {
  const state = globalThis.__ansightDom;
  const candidates = Array.from(document.querySelectorAll("*")).filter((el) =>
    args.nodeId
      ? state?.ids.get(el) === args.nodeId
      : args.automationId
        ? [
            el.id,
            el.getAttribute("data-testid"),
            el.getAttribute("data-automation-id"),
          ].includes(args.automationId)
        : false,
  );
  if (candidates.length !== 1)
    throw new Error("Target missing or ambiguous; capture a fresh tree");
  const el = candidates[0],
    r = el.getBoundingClientRect();
  const style = getComputedStyle(el);
  if (
    style.visibility === "hidden" ||
    style.display === "none" ||
    el.getAttribute("aria-disabled") === "true" ||
    el.disabled ||
    r.width <= 0 ||
    r.height <= 0 ||
    r.bottom <= 0 ||
    r.top >= innerHeight ||
    r.right <= 0 ||
    r.left >= innerWidth ||
    el.closest("[inert]")
  )
    throw new Error("Target is not interactable");
  const action = args.action;
  let click = action === "tap";
  if (action === "unfocus") el.blur();
  if (["setValue", "typeText"].includes(action)) {
    const editable =
      el.isContentEditable ||
      el.tagName === "TEXTAREA" ||
      (el.tagName === "INPUT" &&
        [
          "text",
          "search",
          "email",
          "url",
          "tel",
          "password",
          "number",
        ].includes(el.type));
    if (!editable || el.readOnly) throw new Error("Target is not editable");
  }
  if (action === "toggle") {
    const role = el.getAttribute("role");
    const native = el.tagName === "INPUT" && el.type === "checkbox";
    if (!native && !["checkbox", "switch"].includes(role))
      throw new Error("Target does not support toggle");
    if (args.checked != null && typeof args.checked !== "boolean")
      throw new Error("checked must be boolean");
    const current = native ? el.checked : el.getAttribute("aria-checked");
    if (!native && !["true", "false", "mixed"].includes(current))
      throw new Error("Toggle state unavailable");
    click =
      args.checked == null ||
      (native ? current : current === "true") !== args.checked;
  }
  if (action === "selectTab") {
    if (el.getAttribute("role") !== "tab")
      throw new Error("Target is not a tab");
    click = el.getAttribute("aria-selected") !== "true";
  }
  if (action === "select") {
    if (el.tagName === "SELECT") {
      if (el.multiple)
        throw new Error("Multiple selection requires an app-specific tool");
      let option;
      if (args.index != null) {
        if (!Number.isInteger(args.index) || args.index < 0)
          throw new Error("Invalid selection index");
        option = el.options[args.index];
      } else {
        const matches = Array.from(el.options).filter(
          (o) => o.value === args.value,
        );
        if (matches.length !== 1)
          throw new Error("Selection value missing or ambiguous");
        option = matches[0];
      }
      if (!option || option.disabled || option.parentElement?.disabled)
        throw new Error("Selection is unavailable");
      if (el.selectedIndex !== option.index) {
        el.selectedIndex = option.index;
        el.dispatchEvent(new Event("input", { bubbles: true }));
        el.dispatchEvent(new Event("change", { bubbles: true }));
      }
    } else if (el.tagName === "INPUT" && el.type === "radio")
      click = !el.checked;
    else if (
      el.getAttribute("role") === "option" ||
      el.getAttribute("role") === "radio"
    )
      click =
        el.getAttribute(
          el.getAttribute("role") === "radio"
            ? "aria-checked"
            : "aria-selected",
        ) !== "true";
    else throw new Error("Target does not support select");
  }
  if (args.focus) {
    el.focus();
    if (document.activeElement !== el)
      throw new Error("Target cannot receive focus");
    if (args.replaceExisting) {
      if (typeof el.select === "function") el.select();
      else if (el.isContentEditable) {
        const range = document.createRange();
        range.selectNodeContents(el);
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
      }
    }
  }
  return {
    click,
    x: Math.round(
      Math.max(0, r.x) + Math.min(r.width, innerWidth - Math.max(0, r.x)) / 2,
    ),
    y: Math.round(
      Math.max(0, r.y) + Math.min(r.height, innerHeight - Math.max(0, r.y)) / 2,
    ),
  };
}
module.exports = { inspectDom, resolveNode };
