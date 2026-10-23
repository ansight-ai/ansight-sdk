'use strict';

const phases = ['down', 'move', 'up', 'cancel', 'hoverEnter', 'hoverMove', 'hoverExit'];
const fields = ['phase', 'x', 'y', 'pressure', 'altitudeRadians', 'azimuthRadians', 'rollRadians', 'distance'];
function normalize(value) {
  if (!value || typeof value !== 'object' || !phases.includes(value.phase)) throw new Error('Invalid Pencil sample.');
  const sample = { pressure: 0, altitudeRadians: Math.PI / 2, azimuthRadians: 0, rollRadians: 0, distance: 0, ...value };
  if (Object.keys(sample).some(key => !fields.includes(key))) throw new Error('Unknown Pencil sample field.');
  for (const key of fields.slice(1)) if (!Number.isFinite(sample[key])) throw new Error('Pencil values must be finite.');
  for (const key of ['x', 'y', 'pressure', 'distance']) if (sample[key] < 0 || sample[key] > 1) throw new Error(`${key} must be between 0 and 1.`);
  if (sample.altitudeRadians < 0 || sample.altitudeRadians > Math.PI / 2) throw new Error('Invalid altitude.');
  if (!['down', 'move'].includes(sample.phase) && sample.pressure !== 0) throw new Error('Non-contact pressure must be zero.');
  return Object.fromEntries(fields.map(key => [key, sample[key]]));
}
function validateSequence(values) {
  if (!Array.isArray(values) || values.length < 2 || values.length > 2000) throw new Error('Supply 2..2000 samples.');
  let touching = false, hovering = false;
  return values.map(normalize).map((sample, index, all) => {
    const p = sample.phase;
    if ((p === 'down' && touching) || (['move', 'up', 'cancel'].includes(p) && !touching)
      || (p === 'hoverEnter' && (touching || hovering)) || (['hoverMove', 'hoverExit'].includes(p) && (!hovering || touching))) throw new Error('Invalid Pencil sequence.');
    if (p === 'down') { touching = true; hovering = false; }
    if (p === 'up') { touching = false; hovering = true; }
    if (p === 'hoverEnter') hovering = true;
    if (['cancel', 'hoverExit'].includes(p)) { touching = false; hovering = false; }
    if (index === all.length - 1 && touching) throw new Error('End contact with up or cancel.');
    return sample;
  });
}
function geometry(value) {
  if (!value || !['x', 'y', 'width', 'height', 'viewportWidth', 'viewportHeight', 'scale'].every(key => Number.isFinite(value[key]))) throw new Error('Surface is not laid out.');
  if (['width', 'height', 'viewportWidth', 'viewportHeight', 'scale'].some(key => value[key] <= 0)) throw new Error('Surface has no visible area.');
  return Object.fromEntries(['x', 'y', 'width', 'height', 'viewportWidth', 'viewportHeight', 'scale'].map(key => [key, value[key]]));
}
function point(sample, rect) { return { x: sample.x * rect.viewportWidth, y: sample.y * rect.viewportHeight }; }
function inside(sample, rect) {
  const p = point(sample, rect);
  return p.x >= rect.x && p.y >= rect.y && p.x < rect.x + rect.width && p.y < rect.y + rect.height;
}

const definition = {
  id: 'ui.simulate_pen_framework', name: 'Play Pencil input on a registered framework surface', category: 'ui', policy: 'write',
  description: 'Explicit SDK playback; invokes only a registered framework surface. Does not inject HID or install UIKit hooks.',
  argumentsSchema: { type: 'object', required: ['command'], additionalProperties: false, properties: { command: { type: 'string' } } },
};

function createPencilRuntime({ capture, checkPlayback, timeoutMs = 35000 }) {
  const surfaces = new Map();
  let active = null, handling = false, nextPointer = 1900000000;
  async function finish(run = active) {
    if (!run || active !== run) return;
    active = null;
    clearTimeout(run.timer);
    const last = run.last;
    try {
      if (last && (run.touching || run.hovering)) {
        const terminal = { ...last, phase: run.touching ? 'cancel' : 'hoverExit', pressure: 0 };
        try { await run.adapter.dispatch(terminal, run.rect, run.pointerId); }
        finally { await capture(terminal, run.rect, run.pointerId); }
      }
    } finally { await run.adapter.finish?.(run.pointerId); }
  }
  function bindSurface(id, adapter) {
    if (typeof id !== 'string' || !id.trim() || surfaces.has(id)) throw new Error('Pencil surface IDs must be unique and nonempty.');
    surfaces.set(id, adapter);
    return async () => {
      if (surfaces.get(id) !== adapter) return;
      surfaces.delete(id);
      if (active?.adapter === adapter) await finish();
    };
  }
  async function execute(command) {
    if (!command || typeof command.runId !== 'string' || !command.runId.trim()) throw new Error('A run ID is required.');
    if (command.action === 'prepare') {
      if (active) throw new Error('A Pencil run is already active.');
      const adapter = surfaces.get(command.target);
      if (!adapter) throw new Error('Pencil target is not mounted or registered.');
      const samples = validateSequence(command.samples);
      adapter.validate?.(samples);
      await checkPlayback();
      const rect = geometry(await adapter.snapshot());
      if (adapter.isBusy?.()) throw new Error('The surface has active real input.');
      for (const sample of samples) if (!inside(sample, rect) || !(await adapter.hitTest(point(sample, rect)))) throw new Error('A sample does not hit the selected surface.');
      if (surfaces.get(command.target) !== adapter || adapter.isBusy?.()) throw new Error('Surface detached or received real input during preparation.');
      const run = { adapter, target: command.target, id: command.runId, samples, rect, index: 0, pointerId: nextPointer++, touching: false, hovering: false, last: null };
      active = run;
      run.timer = setTimeout(() => { finish(run).catch(error => adapter.onError?.(error)); }, timeoutMs);
      run.timer.unref?.();
      return { protocolVersion: 1, delivery: 'framework' };
    }
    if (!active && command.action === 'finish') return {};
    if (!active || active.id !== command.runId) throw new Error('No matching Pencil run.');
    const run = active;
    if (command.action === 'finish') { await finish(run); return {}; }
    if (command.action !== 'sample') throw new Error('Unknown Pencil action.');
    try {
      const sample = normalize(command.sample);
      if (JSON.stringify(sample) !== JSON.stringify(run.samples[run.index])) throw new Error('Sample differs from the prepared sequence.');
      if (surfaces.get(run.target) !== run.adapter || run.adapter.isBusy?.()) throw new Error('Surface detached or received real input.');
      if (JSON.stringify(geometry(await run.adapter.snapshot())) !== JSON.stringify(run.rect) || !(await run.adapter.hitTest(point(sample, run.rect)))) throw new Error('Surface moved, resized or became covered.');
      await checkPlayback();
      // Native checks yield to the app; disposal or lease expiry can happen while awaiting them.
      if (active !== run || surfaces.get(run.target) !== run.adapter || run.adapter.isBusy?.()) throw new Error('Pencil run ended or the surface changed during validation.');
      run.last = sample;
      run.touching = ['down', 'move'].includes(sample.phase);
      run.hovering = ['up', 'hoverEnter', 'hoverMove'].includes(sample.phase);
      await run.adapter.dispatch(sample, run.rect, run.pointerId);
      await capture(sample, run.rect, run.pointerId);
      if (active !== run) throw new Error('Pencil run ended during delivery.');
      run.index++;
      return { delivered: true, sampleIndex: run.index - 1 };
    } catch (error) { await finish(run); throw error; }
  }
  async function handle(args) {
    if (handling) return { success: false, errorCode: 'pen_busy', message: 'Another Pencil command is executing.' };
    handling = true;
    try {
      const command = JSON.parse(args.command);
      return { success: true, result: await execute(command) };
    } catch (error) { return { success: false, errorCode: 'pen_framework_failed', message: String(error.message || error) }; }
    finally { handling = false; }
  }
  return { definition, bindSurface, handle };
}

function pointerProperties(sample, rect, pointerId) {
  const p = point(sample, rect), contact = ['down', 'move'].includes(sample.phase);
  const altitude = sample.altitudeRadians, azimuth = sample.azimuthRadians;
  const degrees = 180 / Math.PI;
  return { pointerId, pointerType: 'pen', isPrimary: true, clientX: p.x, clientY: p.y,
    pageX: p.x, pageY: p.y, pressure: sample.pressure, width: 1, height: 1,
    buttons: contact ? 1 : 0, button: ['down', 'up'].includes(sample.phase) ? 0 : -1,
    tiltX: Math.atan2(Math.cos(azimuth) * Math.cos(altitude), Math.sin(altitude)) * degrees,
    tiltY: Math.atan2(Math.sin(azimuth) * Math.cos(altitude), Math.sin(altitude)) * degrees,
    twist: ((sample.rollRadians * degrees % 360) + 360) % 360,
    altitudeAngle: altitude, azimuthAngle: azimuth,
    ansightPencil: Object.freeze({ ...sample, inputSource: 'simulator-sdk-framework' }) };
}
module.exports = { createPencilRuntime, pointerProperties, validateSequence };
