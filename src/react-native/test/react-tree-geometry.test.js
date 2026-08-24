"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const { createReactCoordinateSpace } = require("../react-tree-geometry");

function dimensions(values) {
  return {
    get(name) {
      return values[name];
    },
  };
}

test("uses the React Native screen dimensions as the visual-tree coordinate space", () => {
  const result = createReactCoordinateSpace(dimensions({
    screen: { width: 411.43, height: 923.43 },
    window: { width: 411.43, height: 899.43 },
  }), [
    { bounds: { x: 0, y: 0, width: 411.43, height: 923.43 } },
  ]);

  assert.deepEqual(result, {
    x: 0,
    y: 0,
    width: 411.43,
    height: 923.43,
    source: "react-native.screen",
  });
});

test("preserves a negative host-window origin for non-edge-to-edge Android apps", () => {
  const result = createReactCoordinateSpace(dimensions({
    screen: { width: 411.43, height: 923.43 },
    window: { width: 411.43, height: 899.43 },
  }), [
    { bounds: { x: 0, y: -54.1, width: 411.43, height: 899.43 } },
    { bounds: { x: 0, y: -300, width: 300, height: 20 } },
  ]);

  assert.deepEqual(result, {
    x: 0,
    y: -54.1,
    width: 411.43,
    height: 923.43,
    source: "react-native.screen",
  });
});

test("falls back to window dimensions when screen dimensions are unavailable", () => {
  const result = createReactCoordinateSpace(dimensions({
    window: { width: 390, height: 844 },
  }));

  assert.deepEqual(result, {
    x: 0,
    y: 0,
    width: 390,
    height: 844,
    source: "react-native.window",
  });
});

test("omits coordinate-space metadata when React Native exposes no valid dimensions", () => {
  assert.equal(createReactCoordinateSpace(dimensions({ screen: { width: 0, height: 0 } })), undefined);
});
