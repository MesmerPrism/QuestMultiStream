import test from 'node:test';
import assert from 'node:assert/strict';

test('actual nonlive hub hides duplicate mode controls and reports independent current capture modes',async()=>{
 const nodes=new Map(),calls=[],intervals=[];let captureAvailable=true;
 const element=id=>{if(!nodes.has(id))nodes.set(id,{value:'',options:[],children:[],parentElement:{hidden:false},append(value){this.children.push(value);},replaceChildren(){this.children=[];}});return nodes.get(id);};
 globalThis.document={getElementById:element,createElement:()=>({append(){},setAttribute(){}})};element('sessionPanel').ownerDocument=document;
 globalThis.location={hash:'#token=fixture',pathname:'/',search:'?q=sourceQ&w=sourceW'};
 globalThis.sessionStorage={getItem(){return null;},setItem(){}};globalThis.history={replaceState(){}};globalThis.setInterval=callback=>{intervals.push(callback);return 1;};
 globalThis.fetch=async(url,options)=>{
  calls.push({url,options});
  if(url.startsWith('/api/hub/capabilities'))return {ok:true,json:async()=>({liveSupported:false,bridgeSelected:false})};
  if(url.startsWith('/api/captures'))return {ok:captureAvailable,json:async()=>[{id:'sourceQ',state:'connected',retirementPending:true,captureMode:'square-left'},{id:'sourceW',state:'requested',retirementPending:true,captureMode:'raw-two-lens'}]};
  return {ok:false,status:503,json:async()=>({})};
 };
 await import('./hub.mjs?nonlive-capture-status');
 for(const role of ['Q','W'])assert.equal(element('captureMode'+role).parentElement.hidden,true);
 assert.match(element('feedQ').src,/source=sourceQ/);assert.match(element('feedW').src,/source=sourceW/);
 assert.equal(element('displayStateQ').textContent,'Display producer: connected · Native square · left eye');
 assert.equal(element('displayStateW').textContent,'Display producer: requested · Raw two-lens mirror');
 assert.equal(element('controller').textContent,'Controller readiness unknown · status unavailable','pair status failure must not erase actual capture observations');
 assert.equal(calls.find(c=>c.url.startsWith('/api/captures')).options.cache,'no-store');
 captureAvailable=false;for(const callback of intervals)await callback();
 assert.equal(element('displayStateQ').textContent,'Display producer: status unavailable');assert.equal(element('displayStateW').textContent,'Display producer: status unavailable');
 assert.equal(calls.some(c=>c.options?.method==='POST'),false,'loading and refreshing never starts or switches a capture');
});
