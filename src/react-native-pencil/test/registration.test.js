const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function load(platform = 'ios', native = {}) {
  const calls = [];
  const bridge = native == null ? undefined : new Proxy(native, { get(target, key) {
    return async () => { calls.push(key); return target[key](); };
  }});
  const module = { exports: {} };
  const context = {
    module, exports: module.exports,
    require(id) {
      if (id === '@ansight/pencil-runtime') return require('../../pencil-runtime');
      if (id === '@ansight/react-native') return { registerTool: () => ({ ready: Promise.resolve(), unregister: async () => {} }) };
      assert.equal(id, 'react-native');
      return { Platform: { OS: platform }, NativeModules: { AnsightReactNativePencil: bridge } };
    },
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../index.js'), 'utf8'), context);
  return { pencil: module.exports.AnsightPencil, calls };
}

test('import is passive and capability does not register', async () => {
  const { pencil, calls } = load('ios', { isSupported: () => true });
  assert.deepEqual(calls, []);
  assert.equal(await pencil.isSupported(), true);
  assert.deepEqual(calls, ['isSupported']);
});

test('registration awaits native completion and preserves device false', async () => {
  for (const value of [true, false]) {
    const { pencil, calls } = load('ios', { register: () => value });
    assert.equal(await pencil.register(), value);
    assert.deepEqual(calls, ['register']);
  }
});

test('unsupported platforms never call native APIs', async () => {
  for (const platform of ['android', 'web', 'macos']) {
    const { pencil, calls } = load(platform);
    assert.equal(await pencil.register(), false);
    assert.equal(await pencil.isSupported(), false);
    assert.deepEqual(calls, []);
  }
});

test('initialization errors reject instead of reporting success', async () => {
  const error = new Error('Initialize the Ansight SDK before registering Pencil simulation.');
  const { pencil } = load('ios', { register: () => { throw error; } });
  await assert.rejects(pencil.register(), error);
});

test('missing native module gives an actionable linking error', async () => {
  const { pencil } = load('ios', null);
  await assert.rejects(pencil.register(), /run pod install and rebuild the iOS app/);
  await assert.rejects(pencil.isSupported(), /Ansight Pencil is not linked/);
});

test('builder plugin setup delegates to optional native registration', async () => {
  const { pencil, calls } = load('ios', { register: () => true });
  assert.equal(pencil.id, 'ansight.pencil');
  assert.deepEqual(calls, []);
  await pencil.initialize();
  assert.deepEqual(calls, ['register']);
});
