"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const {
  reactSemanticRole,
  reactSupportedActions,
} = require("../react-tree-semantics");

test("recognizes Pressable actions before and after React Native lowers them", () => {
  assert.deepEqual(reactSupportedActions("Pressable", { onPress() {} }), ["tap"]);
  assert.deepEqual(reactSupportedActions("RCTView", { onClick() {} }), ["tap"]);
  assert.deepEqual(reactSupportedActions("RCTView", { onResponderRelease() {} }), ["tap"]);
});

test("preserves explicit roles and recognizes component roles", () => {
  assert.equal(reactSemanticRole("RCTView", { accessibilityRole: "button" }, 5), "button");
  assert.equal(reactSemanticRole("ForwardRef(Pressable)", {}, 11), "button");
  assert.equal(reactSemanticRole("RCTText", {}, 5), "text");
});

test("recognizes text-entry and scroll actions", () => {
  assert.deepEqual(reactSupportedActions("TextInput", {}), ["typeText", "focus"]);
  assert.deepEqual(reactSupportedActions("RCTScrollView", {}), ["scroll", "swipe"]);
});
