import { describe, expect, it } from "vitest";

import { createStandaloneOptions } from "../src/standalone-options";

describe("standalone options", () => {
  it("defaults remote tools to read-only access", () => {
    expect(createStandaloneOptions().toolGuard).toBe("readOnly");
  });

  it.each(["readOnly", "disabled"] as const)(
    "preserves the documented %s tool guard override",
    (toolGuard) => {
      expect(createStandaloneOptions({ toolGuard }).toolGuard).toBe(toolGuard);
    },
  );
});
