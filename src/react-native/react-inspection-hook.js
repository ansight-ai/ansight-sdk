"use strict";

const ANSIGHT_HOOK_MARKER = "__ansightReactInspectionHook";

function hasElementState(current) {
  const state = current && current.memoizedState;
  if (!state || typeof state !== "object") {
    return true;
  }
  if (!Object.prototype.hasOwnProperty.call(state, "element")) {
    return true;
  }
  return state.element != null;
}

function createReactInspectionHook() {
  const renderers = new Map();
  const fiberRoots = new Map();
  let nextRendererId = 1;

  function rootsForRenderer(rendererId) {
    let roots = fiberRoots.get(rendererId);
    if (!roots) {
      roots = new Set();
      fiberRoots.set(rendererId, roots);
    }
    return roots;
  }

  return {
    [ANSIGHT_HOOK_MARKER]: true,
    supportsFiber: true,
    isDisabled: false,
    renderers,
    _fiberRoots: fiberRoots,
    inject(renderer) {
      const rendererId = nextRendererId++;
      renderers.set(rendererId, renderer);
      rootsForRenderer(rendererId);
      return rendererId;
    },
    getFiberRoots(rendererId) {
      return rootsForRenderer(rendererId);
    },
    onCommitFiberRoot(rendererId, root) {
      const roots = rootsForRenderer(rendererId);
      if (root && root.current && hasElementState(root.current)) {
        roots.add(root);
      } else {
        roots.delete(root);
      }
    },
    onCommitFiberUnmount() {},
  };
}

function ensureReactInspectionHook(runtimeGlobal) {
  if (!runtimeGlobal || typeof runtimeGlobal !== "object") {
    return undefined;
  }

  const existing = runtimeGlobal.__REACT_DEVTOOLS_GLOBAL_HOOK__;
  if (existing) {
    return existing;
  }

  const hook = createReactInspectionHook();
  runtimeGlobal.__REACT_DEVTOOLS_GLOBAL_HOOK__ = hook;
  return hook;
}

module.exports = {
  ANSIGHT_HOOK_MARKER,
  createReactInspectionHook,
  ensureReactInspectionHook,
};
