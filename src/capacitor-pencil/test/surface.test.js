const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
class TestPointerEvent extends Event {
  constructor(type, options={}) { super(type,options); for(const [key,value] of Object.entries(options)) if(!['bubbles','cancelable','composed'].includes(key))this[key]=value; }
}
test('DOM binding invokes existing listeners and scopes capture shims to its synthetic pointer', async()=>{
  let commandHandler;
  const captured=[], nativeCaptureCalls=[];
  const window = new EventTarget();Object.assign(window,{innerWidth:800,innerHeight:600,devicePixelRatio:2});
  const element = new EventTarget();
  const document={visibilityState:'visible',elementFromPoint:()=>element};
  Object.assign(element,{ownerDocument:document,isConnected:true,contains:other=>other===element,getBoundingClientRect:()=>({x:0,y:0,width:800,height:600}),
    setPointerCapture:id=>nativeCaptureCalls.push(id),releasePointerCapture:()=>{},hasPointerCapture:()=>false});
  const original=element.setPointerCapture;
  const module={exports:{}};
  vm.runInNewContext(fs.readFileSync(require.resolve('../dist/cjs/index.js'),'utf8'),{module,exports:module.exports,window,document,PointerEvent:TestPointerEvent,console,
    require(id){
      if(id==='@ansight/pencil-runtime')return require('../../pencil-runtime');
      if(id==='@ansight/capacitor')return{registerTool:(definition,handler)=>{assert.equal(definition.policy,'write');commandHandler=handler;return{ready:Promise.resolve(),unregister:async()=>{}};}};
      if(id==='@capacitor/core')return{Capacitor:{getPlatform:()=> 'ios'},registerPlugin:()=>({register:async()=>({value:true}),requirePlayback:async()=>{},hitTest:async()=>({value:true}),capture:async({json})=>captured.push(JSON.parse(json))})};
      throw new Error(id);
    }});
  const pencil=module.exports.AnsightPencil;
  await pencil.initialize();
  const dispose=pencil.bindSurface('canvas',element);
  const events=[];
  element.addEventListener('pointerdown',event=>{events.push(event);element.setPointerCapture(event.pointerId);assert.equal(element.hasPointerCapture(event.pointerId),true);element.setPointerCapture(7);});
  element.addEventListener('pointerup',event=>events.push(event));
  const call=command=>commandHandler({command:JSON.stringify({runId:'run',...command})});
  const down={phase:'down',x:.25,y:.5,pressure:.8,rollRadians:1.2};const up={phase:'up',x:.25,y:.5};
  assert.equal((await call({action:'prepare',target:'canvas',samples:[down,up]})).success,true);
  assert.equal((await call({action:'sample',sample:down})).success,true);
  assert.equal((await call({action:'sample',sample:up})).success,true);
  assert.equal(events.length,2);assert.equal(events[0].pressure,.8);assert.equal(events[0].ansightPencil.rollRadians,1.2);
  assert.deepEqual(nativeCaptureCalls,[7]);assert.equal(element.setPointerCapture,original);
  assert.equal(captured[0].sample.pressure,.8);
  await call({action:'finish'});await dispose();
});
