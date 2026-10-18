import { describe, expect, it } from 'vitest';
import { createTelemetry } from '../src/telemetry';

describe('timed spans', () => {
  it('preserves fractional duration and parent identity and ends once', async () => {
    const events: Array<{ details: string }> = []; let time = 10;
    const api = createTelemetry(async event => events.push(event), () => time);
    const span = await api.beginSpan('Load', { channel: 64, parentSpanId: 'parent' });
    time = 26.67;
    await span.end('error'); await span.end();
    expect(events).toHaveLength(2);
    expect(JSON.parse(events[1].details)).toMatchObject({ spanId: span.id, parentSpanId: 'parent', status: 'error' });
    expect(JSON.parse(events[1].details).durationMs).toBeCloseTo(16.67);
  });
  it('keeps the original failure from measured operations', async () => {
    const events: Array<{ details: string }> = [];
    const api = createTelemetry(async event => events.push(event));
    const error = new Error('failed');
    await expect(api.measure('failure', async () => { throw error; })).rejects.toBe(error);
    expect(JSON.parse(events[1].details).status).toBe('error');
  });
});
