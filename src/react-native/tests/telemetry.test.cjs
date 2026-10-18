const assert = require('node:assert/strict');
const test = require('node:test');
const { createTelemetry } = require('../telemetry');
test('span emits fractional monotonic duration, parent identity, and only one end', async () => {
  const events = []; let time = 10;
  const api = createTelemetry(async event => events.push(event), () => time);
  const span = await api.beginSpan('load', { channel: 64, parentSpanId: 'parent' });
  time = 26.67;
  await Promise.all([span.end(), span.end('error')]);
  assert.equal(events.length, 2);
  const end = JSON.parse(events[1].details);
  assert.equal(end.spanId, span.id); assert.equal(end.parentSpanId, 'parent');
  assert.ok(Math.abs(end.durationMs - 16.67) < .00001); assert.equal(end.status, 'ok');
});
test('measure records errors and rethrows the original error', async () => {
  const events = []; const api = createTelemetry(async event => events.push(event));
  const error = new Error('failed');
  await assert.rejects(api.measure('failing', async () => { throw error; }), failure => failure === error);
  assert.equal(JSON.parse(events[1].details).status, 'error');
  await assert.rejects(api.beginSpan(' '));
});
