"use strict";

function reactSemanticRole(type, props, fiberTag) {
  const declared = props && (props.accessibilityRole || props.role);
  if (declared) return String(declared).toLowerCase();
  if (fiberTag === 6 || /text/i.test(type)) return "text";
  if (/button|pressable|touchable/i.test(type)) return "button";
  if (/textinput/i.test(type)) return "textbox";
  if (/switch/i.test(type)) return "switch";
  if (/scrollview|flatlist|sectionlist/i.test(type)) return "scrollview";
  return "view";
}

function reactSupportedActions(type, props) {
  const actions = [];
  if (props && (
    typeof props.onPress === "function"
    || typeof props.onClick === "function"
    || typeof props.onResponderRelease === "function"
  )) actions.push("tap");
  if (props && (typeof props.onChangeText === "function" || /textinput/i.test(type))) {
    actions.push("typeText", "focus");
  }
  if (/scrollview|flatlist|sectionlist/i.test(type) || (props && typeof props.onScroll === "function")) {
    actions.push("scroll", "swipe");
  }
  return actions;
}

module.exports = {
  reactSemanticRole,
  reactSupportedActions,
};
