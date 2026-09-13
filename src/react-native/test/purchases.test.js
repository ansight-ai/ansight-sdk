const test = require('node:test');
const assert = require('node:assert/strict');
const { createPurchaseDiagnostics } = require('../purchases');

test('purchase facade forwards typed observations and expectations to native core', async () => {
  const calls = [];
  const purchases = createPurchaseDiagnostics(async (json) => { calls.push(JSON.parse(json)); return '{"success":true}'; });
  await purchases.register();
  await purchases.record({ productId: 'premium', source: 'app', entitled: false });
  await purchases.validate({ productId: 'premium', expectedEntitled: true, requireBackendVerification: true, expectedDeliveryCount: 1 });
  await purchases.refreshStoreKit(['premium']);
  await purchases.clear();
  assert.deepEqual(calls.map(c => c.action), ['register', 'record', 'purchases.validate', 'refreshStoreKit', 'clear']);
  assert.equal(calls[1].observation.entitled, false);
  assert.equal(typeof calls[1].observation.observedAt, 'number');
  assert.equal(calls[2].arguments.requireBackendVerification, true);
});

test('native failures propagate instead of becoming successful empty snapshots', async () => {
  const purchases = createPurchaseDiagnostics(async () => { throw new Error('native unavailable'); });
  await assert.rejects(purchases.snapshot(), /native unavailable/);
});
