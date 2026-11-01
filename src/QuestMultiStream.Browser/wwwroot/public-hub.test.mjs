import test from 'node:test';
import assert from 'node:assert/strict';
import {rememberGateway} from './gateway.mjs';
test('public hub reads without mutation then sends exactly the explicitly reviewed and executed requests to bearer gateway',async()=>{
 const nodes=new Map(),calls=[],values=new Map(),intervals=[];
 const element=id=>{if(!nodes.has(id))nodes.set(id,{value:'',options:[],children:[],parentElement:{hidden:false},append(value){this.children.push(value);},replaceChildren(){this.children=[];}});return nodes.get(id);};
 globalThis.document={documentElement:{dataset:{publicGateway:'true'}},getElementById:element,createElement:()=>({append(){},setAttribute(){}})};element('sessionPanel').ownerDocument=document;
 const app='https://controls.example/bilateral-control/',gateway='http://192.168.1.10:43118',token='b'.repeat(48);
 globalThis.location=new URL(app);globalThis.history={replaceState(){}};
 globalThis.sessionStorage={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};
 rememberGateway(sessionStorage,{appUrl:app,gateway,token},true);globalThis.setInterval=fn=>{intervals.push(fn);return 1;};
 globalThis.fetch=async(url,options)=>{
  calls.push({url,options});
  const path=new URL(url).pathname;
  return {ok:true,json:async()=>path==='/api/hub/capabilities'?{liveSupported:false,bridgeSelected:true}:path==='/api/captures'?[]:path==='/api/hub'?{generation:7,observations:[]}:path==='/api/hub/actions'?{state:'unknown',actualEffectsCompleted:false}:null};
 };
 await import('./hub.mjs?public-reviewed-actions');assert.equal(calls.filter(c=>c.options.method==='POST').length,0);
 assert.match(element('feedQ').src,/^https:\/\/controls.example\/bilateral-control\/viewer.html/);
 element('action').value='establish';await element('actionForm').onsubmit({preventDefault(){}});
 assert.equal(calls.filter(c=>c.options.method==='POST').length,1);assert.equal(element('execute').disabled,false);
 await element('execute').onclick();const posts=calls.filter(c=>c.options.method==='POST');assert.equal(posts.length,2);
 assert.equal(JSON.parse(posts[0].options.body).execute,false);assert.equal(JSON.parse(posts[1].options.body).execute,true);
 for(const callback of intervals)await callback();assert.equal(calls.filter(c=>c.options.method==='POST').length,2);
 for(const {url,options} of calls){assert.equal(new URL(url).origin,gateway);assert.equal(new URL(url).search,'');assert.equal(options.headers.Authorization,'Bearer '+token);}
});
