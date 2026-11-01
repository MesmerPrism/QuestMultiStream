import test from 'node:test';
import assert from 'node:assert/strict';
const app='https://controls.example/bilateral-control/',token='a'.repeat(48),operator=app+'#gateway=http%3A%2F%2F192.168.1.10%3A43118&token='+token;
function fixture({href=app,permission='prompt',storage}={}){
 const nodes=new Map(),loads=[],events=new Map(),queries=[],values=new Map();
 const element=id=>{if(!nodes.has(id))nodes.set(id,{hidden:false,value:'',checked:false,textContent:'',open:false});return nodes.get(id);};
 const location=new URL(href),history={replaceState(_a,_b,value){location.href=new URL(value,location.href).href;}};
 storage??={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};
 return {element,loads,events,queries,storage,history,document:{documentElement:{dataset:{publicGateway:'true'}},getElementById:element},window:{location,history,navigator:{onLine:true,permissions:{query:async({name})=>{queries.push(name);if(permission==='unsupported')throw Error();return {state:permission};}}},isSecureContext:true,addEventListener:(name,fn)=>events.set(name,fn)},loadHub:async()=>loads.push('loaded')};
}
test('public operator fragments are removed before waiting and cannot grant consent or trigger the local network',async()=>{
 const {mountPwa}=await import('./pwa.mjs'),f=fixture({href:operator,permission:'granted'});await mountPwa(f);
 assert.equal(f.window.location.hash,'');assert.equal(f.loads.length,0);assert.equal(f.element('operatorLink').value,'');assert.equal(f.element('trustedLanConsent').checked,false);assert.deepEqual(f.queries,[]);
 await f.element('operatorLinkForm').onsubmit({preventDefault(){}});assert.equal(f.loads.length,0);
 f.element('trustedLanConsent').checked=true;await f.element('operatorLinkForm').onsubmit({preventDefault(){}});assert.equal(f.loads.length,1);
 f.events.get('online')();assert.equal(f.loads.length,1,'online events never replay bootstrap or mutations');
});
test('reload restores only already-consented session with granted granular browser permission',async()=>{
 const {mountPwa}=await import('./pwa.mjs'),first=fixture({href:operator});await mountPwa(first);first.element('trustedLanConsent').checked=true;await first.element('operatorLinkForm').onsubmit({preventDefault(){}});
 for(const permission of ['prompt','denied','unsupported','granted']){
  const f=fixture({permission,storage:first.storage});await mountPwa(f);assert.equal(f.loads.length,permission==='granted'?1:0,permission);assert.deepEqual(f.queries,['local-network']);
 }
});
test('public link replacement clears masked input and rejects foreign origin or app without connecting',async()=>{
 const {mountPwa}=await import('./pwa.mjs'),f=fixture();await mountPwa(f);f.element('trustedLanConsent').checked=true;f.element('operatorLink').value=operator.replace('/bilateral-control/','/viscereality/');
 await f.element('operatorLinkForm').onsubmit({preventDefault(){}});assert.equal(f.loads.length,0);assert.equal(f.element('operatorLink').value,'');
});
