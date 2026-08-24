import { describe, expect, it } from "vitest";

import { createDomCoordinateSpace, normalizeDomAction } from "../src/dom";

describe("DOM visual-tree geometry", () => {
  it("uses the WebView viewport as the coordinate space", () => {
    expect(
      createDomCoordinateSpace(
        { innerWidth: 411.4286, innerHeight: 923.4286 } as Window,
        null,
      ),
    ).toEqual({
      x: 0,
      y: 0,
      width: 411.4286,
      height: 923.4286,
      source: "dom.viewport",
    });
  });

  it("falls back to the document element dimensions", () => {
    expect(
      createDomCoordinateSpace(
        { innerWidth: 0, innerHeight: 0 } as Window,
        { clientWidth: 390, clientHeight: 844 } as HTMLElement,
      ),
    ).toEqual({
      x: 0,
      y: 0,
      width: 390,
      height: 844,
      source: "dom.viewport",
    });
  });

  it("omits invalid viewport metadata", () => {
    expect(
      createDomCoordinateSpace(
        { innerWidth: Number.NaN, innerHeight: 0 } as Window,
        null,
      ),
    ).toBeUndefined();
  });
});

describe("DOM action names", () => {
  it("maps semantic automation actions to browser operations", () => {
    expect(normalizeDomAction("tap")).toBe("click");
    expect(normalizeDomAction("typeText")).toBe("setValue");
  });

  it("preserves legacy browser action names", () => {
    expect(normalizeDomAction("click")).toBe("click");
    expect(normalizeDomAction("setValue")).toBe("setValue");
    expect(normalizeDomAction("focus")).toBe("focus");
    expect(normalizeDomAction("blur")).toBe("blur");
  });
});
