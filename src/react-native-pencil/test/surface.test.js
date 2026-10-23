const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const {createPencilRuntime} = require('../../pencil-runtime');

test('hook delivers to existing callbacks, retains real handlers and disposes only its pointer', async () => {
  const received = [], realReceived = [], captured = [];
  let cleanup, covered = false;
  const ref = {current: {measureInWindow: callback => callback(0,0,800,600)}};
  const runtime = createPencilRuntime({checkPlayback: async()=>{},capture:async sample=>{captured.push(sample);}});
  const module = {exports:{}};
  vm.runInNewContext(fs.readFileSync(require.resolve('../surface'),'utf8'),{module,exports:module.exports,setTimeout,clearTimeout,console,
    require(id) {
      if(id==='react')return {useRef:value=>({current:value}),useEffect:effect=>{cleanup=effect();}};
      if(id==='@ansight/pencil-runtime')return require('../../pencil-runtime');
      if(id==='react-native')return {Dimensions:{get:()=>({width:800,height:600})},PixelRatio:{get:()=>2},findNodeHandle:()=>42,
        NativeModules:{AnsightReactNativePencil:{hitTest:async()=>!covered}}};
      throw new Error(id);
    }});
  const handler=event=>received.push(event);
  const bindings=module.exports.usePencilSurface(runtime,'canvas',ref,{onPointerDown:handler,onPointerMove:handler,onPointerUp:handler,onPointerCancel:handler,onTouchStart:event=>realReceived.push(event)});
  assert.equal(bindings.testID,'canvas');assert.equal(bindings.collapsable,false);
  const call=command=>runtime.handle({command:JSON.stringify({runId:'run',...command})});
  const down={phase:'down',x:.25,y:.5,pressure:.8,rollRadians:1};const up={phase:'up',x:.25,y:.5};
  const realEvent={nativeEvent:{changedTouches:[{identifier:7}]}};
  bindings.onTouchStart(realEvent);assert.equal(realReceived[0],realEvent);
  assert.equal((await call({action:'prepare',target:'canvas',samples:[down,up]})).success,false);
  bindings.onTouchEnd(realEvent);
  assert.equal((await call({action:'prepare',target:'canvas',samples:[down,up]})).success,true);
  assert.equal((await call({action:'sample',sample:down})).success,true);
  assert.equal(received[0].nativeEvent.pressure,.8);assert.equal(received[0].nativeEvent.locationX,200);
  assert.equal(received[0].nativeEvent.ansightPencil.rollRadians,1);
  covered=true;
  assert.equal((await call({action:'sample',sample:up})).success,false);
  assert.equal(received.at(-1).nativeEvent.ansightPencil.phase,'cancel');
  assert.equal(captured.at(-1).phase,'cancel');cleanup();
});
