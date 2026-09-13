import { describe, expect, it } from "vitest";
import { createPurchaseDiagnostics } from "../src/purchases";

describe("purchases", () => {
  it("delegates to native base SDK without interpreting verification", async () => {
    const calls: Record<string, unknown>[] = [];
    const purchases = createPurchaseDiagnostics(async (json) => {
      calls.push(JSON.parse(json));
      return JSON.stringify({ status: "inconclusive", observations: [] });
    });
    await purchases.record({
      productId: "premium",
      source: "store",
      verification: "unknown",
    });
    const result = await purchases.validate({
      productId: "premium",
      expectedEntitled: true,
      requireBackendVerification: true,
    });
    expect(calls[0].action).toBe("record");
    expect(calls[1].action).toBe("purchases.validate");
    expect(result.status).toBe("inconclusive");
  });
  it("propagates unavailable native bridge errors", async () => {
    const purchases = createPurchaseDiagnostics(async () => {
      throw new Error("unavailable");
    });
    await expect(purchases.snapshot()).rejects.toThrow("unavailable");
  });
});
