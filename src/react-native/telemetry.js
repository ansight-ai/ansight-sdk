"use strict";

function createTelemetry(recordEvent, clock = () => performance.now()) {
  async function beginSpan(name, { channel = 255, parentSpanId } = {}) {
    if (typeof name !== "string" || !name.trim()) throw new TypeError("Span name must not be blank.");
    if (!Number.isInteger(channel) || channel < 0 || channel > 255) throw new RangeError("Channel ids must be between 0 and 255.");
    const id = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`;
    const startedAtUtc = new Date().toISOString();
    const started = clock();
    const publish = (phase, durationMs, status) => recordEvent({
      label: name.trim(), type: "Span", channel,
      details: JSON.stringify({ spanId: id, parentSpanId, phase, startedAtUtc, durationMs, status }),
    });
    await publish("start");
    let completion;
    return {
      id,
      end(status = "ok") {
        if (!["ok", "error", "cancelled"].includes(status)) throw new TypeError("Span status must be ok, error, or cancelled.");
        if (!completion) completion = Promise.resolve(publish("end", Math.max(0, clock() - started), status)).then(() => undefined);
        return completion;
      },
    };
  }
  async function measure(name, operation, options) {
    const span = await beginSpan(name, options);
    try {
      const result = await operation(span);
      await span.end();
      return result;
    } catch (error) {
      await span.end(error?.name === "AbortError" ? "cancelled" : "error");
      throw error;
    }
  }
  return { beginSpan, measure };
}
module.exports = { createTelemetry };
