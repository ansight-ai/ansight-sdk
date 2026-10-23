const {test} = require('node:test');
const assert = require('node:assert/strict');
const {createPencilRuntime, pointerProperties} = require('..');
const rect = {x: 0, y: 0, width: 800, height: 600, viewportWidth: 800, viewportHeight: 600, scale: 2};
const down = {phase: 'down', x: .2, y: .5, pressure: .8, altitudeRadians: .4, azimuthRadians: .7, rollRadians: 1.2};
const up = {phase: 'up', x: .2, y: .5};
function fixture(options = {}) {
  const received = [], recorded = [];
  let busy = false, hit = true, geometry = {...rect}, capture = true;
  const runtime = createPencilRuntime({timeoutMs: options.timeoutMs || 35000,
    checkPlayback: options.checkPlayback || (async () => {}),
    capture: async sample => { if (capture) recorded.push(sample); },
  });
  const dispose = runtime.bindSurface('canvas', {snapshot: () => geometry, hitTest: () => hit, isBusy: () => busy,
    dispatch: sample => { received.push(sample); }, ...options.adapter });
  const call = command => runtime.handle({command: JSON.stringify({runId: 'run', ...command})});
  return {received, recorded, runtime, dispose, call,
    prepare: (samples = [down, up]) => call({action: 'prepare', target: 'canvas', samples}),
    busy: () => { busy = true; }, covered: () => { hit = false; }, moved: () => { geometry = {...rect, x: 20}; }, noCapture: () => { capture = false; }};
}
test('registered handler receives prepared values and capture retains them', async () => {
  const f = fixture(); assert.equal((await f.prepare()).success, true);
  assert.equal((await f.call({action:'sample', sample:down})).success, true);
  assert.equal((await f.call({action:'sample', sample:up})).success, true);
  await f.call({action:'finish'});
  assert.equal(f.received[0].pressure, .8); assert.equal(f.received[0].rollRadians, 1.2);
  assert.deepEqual(f.recorded, f.received);
  assert.deepEqual(f.received.map(x=>x.phase), ['down','up','hoverExit']);
});
test('unregistered and duplicate targets fail without changing handlers', async () => {
  const f = fixture(); assert.throws(()=>f.runtime.bindSurface('canvas', {}), /unique/);
  assert.equal((await f.call({action:'prepare',target:'missing',samples:[down,up]})).success,false);
  assert.deepEqual(f.received, []);
});
test('capture can be disabled before or during playback without losing delivery or cleanup', async () => {
  for (const disableBefore of [true, false]) {
    const f=fixture(); if(disableBefore) f.noCapture();
    assert.equal((await f.prepare()).success,true);
    assert.equal((await f.call({action:'sample',sample:down})).success,true);
    f.noCapture();
    assert.equal((await f.call({action:'sample',sample:up})).success,true);
    assert.equal((await f.call({action:'finish'})).success,true);
    assert.deepEqual(f.received.map(x=>x.phase),['down','up','hoverExit']);
    assert.equal(f.recorded.length, disableBefore ? 0 : 1);
  }
});
test('disconnected or unsupported playback still fails before input', async () => {
  const f=fixture({checkPlayback:async()=>{throw new Error('No live Simulator session');}});
  assert.equal((await f.prepare()).success,false);assert.deepEqual(f.received,[]);
});
test('preflight rejects malformed, out-of-bounds and invalid sequences', async () => {
  for (const samples of [[up,down],[{...down,x:2},up],[{...down,phase:'move'},up],[{...down,pressure:NaN},up]]) {
    const f=fixture();assert.equal((await f.prepare(samples)).success,false);assert.deepEqual(f.received,[]);
  }
});
test('busy real input blocks playback without cancelling it', async () => {
  const f=fixture();f.busy();assert.equal((await f.prepare()).success,false);assert.deepEqual(f.received,[]);
});
test('moved, covered or newly busy surfaces cancel only the simulated contact', async () => {
  for (const change of ['moved','covered','busy']) {
    const f=fixture();await f.prepare();await f.call({action:'sample',sample:down});f[change]();
    assert.equal((await f.call({action:'sample',sample:up})).success,false);
    assert.deepEqual(f.received.map(x=>x.phase),['down','cancel']);
  }
});
test('mismatched samples cancel the active synthetic pointer', async () => {
  const f=fixture();await f.prepare();await f.call({action:'sample',sample:down});
  assert.equal((await f.call({action:'sample',sample:{...up,x:.9}})).success,false);
  assert.equal(f.received.at(-1).phase,'cancel');
});
test('wrong run cannot finish another run', async () => {
  const f=fixture();await f.prepare();assert.equal((await f.call({action:'finish',runId:'wrong'})).success,false);
  assert.equal((await f.call({action:'sample',sample:down})).success,true);await f.dispose();
});
test('unmount and lease expiry release the owned pointer', async () => {
  for (const expired of [false,true]) {
    const f=fixture({timeoutMs:10});await f.prepare();await f.call({action:'sample',sample:down});
    if(expired) await new Promise(resolve=>setTimeout(resolve,30)); else await f.dispose();
    assert.deepEqual(f.received.map(x=>x.phase),['down','cancel']);
  }
});
test('orientation maps to standard pointer fields and preserves source radians', () => {
  const fields=pointerProperties({...down,distance:0},rect,123);
  assert.equal(fields.pointerType,'pen');assert.equal(fields.clientX,160);assert.equal(fields.pressure,.8);
  assert.ok(Math.abs(fields.twist - 1.2 * 180/Math.PI)<1e-9);
  assert.equal(fields.ansightPencil.rollRadians,1.2);
});
test('disposal while native validation is pending cannot revive a run or dispatch input', async () => {
  for (const duringPrepare of [true,false]) {
    let block = duringPrepare, release, entered;
    const gate = new Promise(resolve => { release=resolve; });
    const waiting = new Promise(resolve => { entered=resolve; });
    const f=fixture({checkPlayback: async()=>{ if(block) { entered(); await gate; } }});
    if(!duringPrepare) { await f.prepare(); block=true; }
    const result=duringPrepare ? f.prepare() : f.call({action:'sample',sample:down});
    await waiting;await f.dispose();release();
    assert.equal((await result).success,false);assert.deepEqual(f.received,[]);
  }
});
