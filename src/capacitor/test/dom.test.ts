import { afterEach, describe, expect, it, vi } from "vitest";

import {
  createDomCoordinateSpace,
  displayedValue,
  normalizeDomAction,
  installDomTools,
} from "../src/dom";
import type { AnsightToolHandler } from "../src/definitions";

describe("focused form submission", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("uses requestSubmit once without synthesizing keys or changing the input", async () => {
    const requestSubmit = vi.fn();
    class Input {
      form = { requestSubmit };
      disabled = false;
      readOnly = false;
      value = "query";
    }
    const input = new Input();
    vi.stubGlobal("HTMLInputElement", Input);
    vi.stubGlobal("document", { activeElement: input });
    const handlers = new Map<string, AnsightToolHandler>();
    installDomTools(
      (definition, handler) => {
        handlers.set(definition.id, handler);
        return {
          id: definition.id,
          ready: Promise.resolve({ success: true, message: "ready" }),
          unregister: async () => ({ success: true, message: "removed" }),
        };
      },
      { allowActions: true },
    );
    const submit = handlers.get("ui.submit_keyboard")!;
    const context = {
      requestId: "submit",
      toolId: "ui.submit_keyboard",
      platform: "ios",
    };
    expect((await submit({}, context)).success).toBe(true);
    expect(requestSubmit).toHaveBeenCalledTimes(1);
    expect(input.value).toBe("query");
    input.readOnly = true;
    expect((await submit({}, context)).success).toBe(false);
    expect(requestSubmit).toHaveBeenCalledTimes(1);
  });

  it("does not register a write tool when DOM actions are disabled", () => {
    const ids: string[] = [];
    installDomTools((definition) => {
      ids.push(definition.id);
      return {
        id: definition.id,
        ready: Promise.resolve({ success: true, message: "ready" }),
        unregister: async () => ({ success: true, message: "removed" }),
      };
    });
    expect(ids).not.toContain("ui.submit_keyboard");
  });
});

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


describe("input values and placeholders", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("preserves an empty value, literal whitespace and text matching the hint", () => {
    class Input { type = "text"; value = ""; placeholder = "Search products…"; }
    vi.stubGlobal("HTMLInputElement", Input);
    const input = new Input();
    expect(displayedValue(input as unknown as Element)).toBe("");
    input.value = "  query\n";
    expect(displayedValue(input as unknown as Element)).toBe("  query\n");
    input.value = input.placeholder;
    expect(displayedValue(input as unknown as Element)).toBe("Search products…");
    input.type = "password";
    expect(displayedValue(input as unknown as Element)).toBeUndefined();
  });
});
