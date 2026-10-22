import test from 'node:test';
import assert from 'node:assert/strict';
import {captureModes,captureHeld,frameLayout,presentationView,populateCaptureModes} from './capture-mode.mjs';

test('closed capture choices preserve raw default and explicit mono presentation',()=>{
  assert.deepEqual(captureModes({}),['raw-two-lens']);
  assert.deepEqual(captureModes({captureModes:['square-left','unexpected','raw-two-lens']}),['raw-two-lens','square-left']);
  assert.equal(frameLayout({}),'stereo-sbs');assert.equal(frameLayout({captureMode:'square-left',frameLayout:'mono'}),'mono');
  for(const view of ['left','right','composite'])assert.equal(presentationView(view,'mono'),'full');
  assert.equal(presentationView('crop','mono'),'crop');assert.equal(presentationView('left','stereo-sbs'),'left');
});
test('mode selection follows actual held invocation and unlocks only after retirement',()=>{
  const document={createElement:()=>({})},node={value:'square-left',options:[],replaceChildren(){this.options=[];},append(option){this.options.push(option);}};
  const source={captureModes:['raw-two-lens','square-left']};
  populateCaptureModes(document,node,source,{state:'connected',retirementPending:true,captureMode:'raw-two-lens'});
  assert.equal(node.value,'raw-two-lens');assert.equal(node.disabled,true);
  for(const state of ['requested','unknown','stopping'])assert.equal(captureHeld({state}),true);
  populateCaptureModes(document,node,source,{state:'retired',retirementPending:false,captureMode:'raw-two-lens'});
  assert.equal(node.disabled,false);node.value='square-left';
  populateCaptureModes(document,node,source,{state:'retired',retirementPending:false});assert.equal(node.value,'square-left');
});
test('actual viewer starts selected native mode and renders full square; Stop remains mode-free',async()=>{
  const calls=[],nodes=new Map(),draws=[],context={drawImage(...args){draws.push(args);},clearRect(){},putImageData(){}};let socket,decoder;
  const element=id=>{if(!nodes.has(id))nodes.set(id,{value:id==='source'?'q':id==='view'?'left':'',textContent:'',style:{},options:id==='view'?['full','left','right','composite','crop'].map(value=>({value})):[{}],files:[],hidden:false,append(o){this.options.push(o);},replaceChildren(){this.options=[];},getContext(){return context;},pause(){},removeAttribute(){}});return nodes.get(id);};
  globalThis.document={hidden:false,getElementById:element,createElement:()=>({getContext:()=>context}),addEventListener(){}};
  globalThis.location={hash:'#token=fixture',pathname:'/viewer.html',search:'',host:'127.0.0.1:43118'};
  globalThis.sessionStorage={getItem(){return null;},setItem(){}};globalThis.history={replaceState(){}};globalThis.setInterval=()=>1;
  globalThis.window={VideoDecoder:true};
  globalThis.fetch=async(url,options)=>{calls.push({url,options});return{ok:true,json:async()=>url.startsWith('/api/sources')?[{id:'q',label:'Q',captureModes:['raw-two-lens','square-left']}]:url.startsWith('/api/captures?')?[{id:'q',state:'retired',retirementPending:false}]:url.includes('/start')?{state:'requested',captureMode:'square-left'}:{available:false}};};
  globalThis.WebSocket=class{static OPEN=1;constructor(){socket=this;this.readyState=1;}close(){this.readyState=3;}};
  globalThis.VideoDecoder=class{constructor(options){decoder=this;this.options=options;this.state='configured';this.decodeQueueSize=0;}configure(){}close(){this.state='closed';}decode(){this.options.output({displayWidth:1280,displayHeight:1280,close(){}});}};
  globalThis.EncodedVideoChunk=class{constructor(options){Object.assign(this,options);}};
  await import('./app.mjs?native-mode-test');assert.equal(calls.some(c=>c.options?.method==='POST'),false);
  element('captureMode').value='square-left';await element('connect').onclick();
  const start=JSON.parse(calls.find(c=>c.url.includes('/start')).options.body);assert.equal(start.captureMode,'square-left');
  await socket.onmessage({data:JSON.stringify({kind:'video',Width:1280,Height:1280,codec:'h264',captureMode:'square-left',frameLayout:'mono'})});
  assert.equal(element('view').value,'full');assert.equal(element('view').options.find(o=>o.value==='left').disabled,true);
  const packet=(config,key,pts,data)=>{const b=new Uint8Array(12+data.length),v=new DataView(b.buffer);v.setBigUint64(0,(config?1n<<63n:0n)|(key?1n<<62n:0n)|BigInt(pts));v.setUint32(8,data.length);b.set(data,12);return b.buffer;};
  await socket.onmessage({data:packet(true,false,0,[0,0,0,1,0x67,0x64,0,0x1f])});
  await socket.onmessage({data:packet(false,true,1,[0,0,1,0x65])});
  assert.ok(decoder);assert.deepEqual(draws.at(-1).slice(1),[0,0,1280,1280,0,0,1280,1280]);
  assert.equal(element('screen').width,1280);assert.equal(element('screen').height,1280);
  await element('stopCapture').onclick();assert.equal(Object.hasOwn(JSON.parse(calls.find(c=>c.url.includes('/stop')).options.body),'captureMode'),false);
  element('stop').onclick();assert.equal(calls.filter(c=>c.options?.method==='POST').length,2,'viewer disconnect sends no capture mutation');
});
