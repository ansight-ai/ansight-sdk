export type AnsightSpanStatus = "ok" | "error" | "cancelled";
export interface AnsightSpanOptions {
  channel?: number;
  parentSpanId?: string;
}
export interface AnsightSpan {
  readonly id: string;
  end(status?: AnsightSpanStatus): Promise<void>;
}
export function createTelemetry(
  recordEvent: (event: {
    label: string;
    type: string;
    channel: number;
    details: string;
  }) => Promise<unknown>,
  clock = () => performance.now(),
) {
  async function beginSpan(
    name: string,
    { channel = 255, parentSpanId }: AnsightSpanOptions = {},
  ): Promise<AnsightSpan> {
    if (typeof name !== "string" || !name.trim())
      throw new TypeError("Span name must not be blank.");
    if (!Number.isInteger(channel) || channel < 0 || channel > 255)
      throw new RangeError("Channel ids must be between 0 and 255.");
    const id =
      globalThis.crypto?.randomUUID?.() ??
      `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`;
    const startedAtUtc = new Date().toISOString();
    const started = clock();
    const publish = (
      phase: string,
      durationMs?: number,
      status?: AnsightSpanStatus,
    ) =>
      recordEvent({
        label: name.trim(),
        type: "Span",
        channel,
        details: JSON.stringify({
          spanId: id,
          parentSpanId,
          phase,
          startedAtUtc,
          durationMs,
          status,
        }),
      });
    await publish("start");
    let completion: Promise<void> | undefined;
    return {
      id,
      end(status: AnsightSpanStatus = "ok") {
        if (!["ok", "error", "cancelled"].includes(status))
          throw new TypeError("Span status must be ok, error, or cancelled.");
        if (!completion)
          completion = Promise.resolve(
            publish("end", Math.max(0, clock() - started), status),
          ).then(() => undefined);
        return completion;
      },
    };
  }
  async function measure<T>(
    name: string,
    operation: (span: AnsightSpan) => T | Promise<T>,
    options?: AnsightSpanOptions,
  ): Promise<T> {
    const span = await beginSpan(name, options);
    try {
      const result = await operation(span);
      await span.end();
      return result;
    } catch (error) {
      await span.end(
        error instanceof Error && error.name === "AbortError"
          ? "cancelled"
          : "error",
      );
      throw error;
    }
  }
  return { beginSpan, measure };
}
