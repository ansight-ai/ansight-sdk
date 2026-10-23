const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const entry = require.resolve('../index.js');
const localRequire = createRequire(entry);

function load(failNative = false) {
  const calls = [];
  const native = new Proxy({}, { get: (_target, name) => async options => {
    if (name === 'initialize' || name === 'initializeAndActivate') {
      calls.push({ name, options });
      if (failNative) throw new Error('native initialization failed');
    }
    if (name === 'snapshot') return { registeredTools: 1 };
    return {};
  }});
  const context = { module: { exports: {} }, global: {}, console, setTimeout, clearTimeout,
    require(name) {
      if (name === 'react') return { version: '19.1.0' };
      if (name === 'react-native') return { NativeModules: { AnsightReactNative: native },
        Platform: { OS: 'ios' }, NativeEventEmitter: class { addListener() { return { remove() {} }; } } };
      return localRequire(name);
    },
  };
  vm.runInNewContext(fs.readFileSync(entry, 'utf8'), context, { filename: entry });
  return { sdk: context.module.exports, calls };
}

test('builder is passive, deduplicates and preserves plugins through defaults and copies', () => {
  const { sdk, calls } = load();
  const plugin = { id: 'optional', initialize: async () => calls.push('plugin') };
  const builder = sdk.createOptionsBuilder().withPlugin(plugin).withAnsightDefaults().withPlugin(plugin);
  const first = builder.build();
  const second = sdk.createOptionsBuilder(first).withReadWriteToolAccess().build();
  assert.equal(second.plugins.length, 1);
  assert.equal(second.plugins[0], plugin);
  first.plugins.length = 0;
  assert.equal(builder.build().plugins.length, 1);
  assert.equal(second.plugins.length, 1);
  assert.equal(second.toolGuard, 'readWrite');
  assert.deepEqual(calls, []);
});

for (const method of ['initialize', 'initializeAndActivate']) {
  test(`${method} awaits plugin setup after native initialization and returns updated snapshot`, async () => {
    const { sdk, calls } = load();
    let release;
    const gate = new Promise(resolve => { release = resolve; });
    let reached;
    const entered = new Promise(resolve => { reached = resolve; });
    const plugin = { id: 'optional', async initialize() {
      assert.equal(calls[0].name, method);
      reached();
      await gate;
      calls.push('plugin complete');
    }};
    const options = sdk.createOptionsBuilder({ lifecycle: false, networkCapture: false })
      .withReadWriteToolAccess().withPlugin(plugin).build();
    let complete = false;
    const result = sdk[method](options).then(value => { complete = true; return value; });
    await entered;
    assert.equal(complete, false);
    assert.equal(calls[0].options.plugins, undefined);
    assert.equal(calls[0].options.toolGuard, 'readWrite');
    release();
    assert.equal((await result).registeredTools, 1);
    assert.equal(calls[1], 'plugin complete');
  });
}

test('native initialization failure prevents plugin setup', async () => {
  const { sdk, calls } = load(true);
  await assert.rejects(sdk.initialize(sdk.createOptionsBuilder().withPlugin({
    id: 'optional', initialize: async () => calls.push('plugin'),
  }).build()), /native initialization failed/);
  assert.equal(calls.length, 1);
});

test('plugin failures reject initialization and stop subsequent plugins', async () => {
  const { sdk } = load();
  const options = sdk.createOptionsBuilder()
    .withPlugin({ id: 'first', initialize: async () => { throw new Error('plugin failed'); } })
    .withPlugin({ id: 'second', initialize: async () => { assert.fail('must not run'); } }).build();
  await assert.rejects(sdk.initializeAndActivate(options), /plugin failed/);
});
