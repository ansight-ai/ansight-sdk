"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const {
  ANSIGHT_HOOK_MARKER,
  createReactInspectionHook,
  ensureReactInspectionHook,
} = require("../react-inspection-hook");

test("installs a passive React inspection hook when DevTools is unavailable", () => {
  const runtimeGlobal = {};

  const hook = ensureReactInspectionHook(runtimeGlobal);

  assert.equal(runtimeGlobal.__REACT_DEVTOOLS_GLOBAL_HOOK__, hook);
  assert.equal(hook[ANSIGHT_HOOK_MARKER], true);
  assert.equal(hook.supportsFiber, true);
  assert.equal(hook.isDisabled, false);
});

test("preserves an existing React DevTools hook", () => {
  const existing = { supportsFiber: true };
  const runtimeGlobal = { __REACT_DEVTOOLS_GLOBAL_HOOK__: existing };

  const hook = ensureReactInspectionHook(runtimeGlobal);

  assert.equal(hook, existing);
  assert.equal(runtimeGlobal.__REACT_DEVTOOLS_GLOBAL_HOOK__, existing);
});

test("tracks mounted roots for every injected renderer", () => {
  const hook = createReactInspectionHook();
  const renderer = { rendererPackageName: "react-native-renderer", version: "19.0.0" };
  const rendererId = hook.inject(renderer);
  const mountedRoot = { current: { memoizedState: { element: {} } } };

  hook.onCommitFiberRoot(rendererId, mountedRoot);

  assert.equal(hook.renderers.get(rendererId), renderer);
  assert.deepEqual(Array.from(hook.getFiberRoots(rendererId)), [mountedRoot]);
});

test("removes a root after React commits an unmount", () => {
  const hook = createReactInspectionHook();
  const rendererId = hook.inject({ rendererPackageName: "react-native-renderer" });
  const root = { current: { memoizedState: { element: {} } } };
  hook.onCommitFiberRoot(rendererId, root);

  root.current = { memoizedState: { element: null } };
  hook.onCommitFiberRoot(rendererId, root);

  assert.deepEqual(Array.from(hook.getFiberRoots(rendererId)), []);
});

test("retains roots for renderer state shapes without an element property", () => {
  const hook = createReactInspectionHook();
  const rendererId = hook.inject({ rendererPackageName: "custom-renderer" });
  const root = { current: { memoizedState: { cache: {} } } };

  hook.onCommitFiberRoot(rendererId, root);

  assert.deepEqual(Array.from(hook.getFiberRoots(rendererId)), [root]);
});
