"use strict";

function readDimensionSize(dimensions, name) {
  if (!dimensions || typeof dimensions.get !== "function") {
    return null;
  }
  try {
    const value = dimensions.get(name);
    const width = Number(value && value.width);
    const height = Number(value && value.height);
    if (Number.isFinite(width) && width > 0 && Number.isFinite(height) && height > 0) {
      return { width, height };
    }
  } catch (_) {
    // Some non-native React renderers do not expose screen dimensions.
  }
  return null;
}

function viewportHostBounds(nodes, screenSize) {
  const candidates = (nodes || [])
    .map((node) => node && node.bounds)
    .filter((bounds) => bounds
      && Number.isFinite(Number(bounds.x))
      && Number.isFinite(Number(bounds.y))
      && Number.isFinite(Number(bounds.width))
      && Number.isFinite(Number(bounds.height))
      && Number(bounds.width) >= screenSize.width * 0.85
      && Number(bounds.height) >= screenSize.height * 0.5);
  if (candidates.length === 0) {
    return null;
  }
  return candidates.reduce((largest, candidate) => (
    Number(candidate.width) * Number(candidate.height)
      > Number(largest.width) * Number(largest.height)
      ? candidate
      : largest
  ));
}

function createReactCoordinateSpace(dimensions, measuredNodes = []) {
  const screenSize = readDimensionSize(dimensions, "screen");
  const windowSize = readDimensionSize(dimensions, "window");
  const size = screenSize || windowSize;
  if (!size) {
    return undefined;
  }

  const hostBounds = viewportHostBounds(measuredNodes, size);
  return {
    x: hostBounds ? Math.min(0, Number(hostBounds.x)) : 0,
    y: hostBounds ? Math.min(0, Number(hostBounds.y)) : 0,
    width: size.width,
    height: size.height,
    source: screenSize ? "react-native.screen" : "react-native.window",
  };
}

module.exports = {
  createReactCoordinateSpace,
};
