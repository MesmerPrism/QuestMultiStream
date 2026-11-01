import test from 'node:test';
import assert from 'node:assert/strict';
import {rememberGateway} from './gateway.mjs';
test('public viewer fragment alone performs no requests; approved secure observer uses bearer reads and WS query without Start or replay',async()=>{
 const nodes=new Map(),calls=[],sockets=[],values=new Map(),context={drawImage(){},clearRect(){},putImageData(){}};
 const element=id=>{if(!nodes.has(id))nodes.set(id,{value:id==='source'?'q':id==='view'?'full':'',textContent:'',style:{},options:[{}],files:[],hidden:false,append(o){this.options.push(o);},getContext(){return context;},pause(){},removeAttribute(){}});return nodes.get(id);};
 globalThis.document={documentElement:{dataset:{publicGateway:'true'}},hidden:false,body:{classList:{add(){}}},getElementById:element,createElement:()=>({getContext:()=>context}),addEventListener(){}};
 const app='https://controls.example/bilateral-control/',token='a'.repeat(48),gateway='http://192.168.1.10:43118';
 const viewer=app+'viewer.html?pane=Q&observe=1&source=q#gateway='+encodeURIComponent(gateway)+'&token='+token;
 globalThis.location=new URL(viewer);globalThis.history={replaceState(_a,_b,value){location.href=new URL(value,location.href).href;}};
 globalThis.sessionStorage={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};
 globalThis.setInterval=()=>1;globalThis.window={VideoDecoder:true};
 globalThis.fetch=async(url,options)=>{calls.push({url,options});return {ok:true,json:async()=>url.includes('/api/sources')?[{id:'q',label:'Q',displayId:0}]:url.includes('/api/captures')?[]:{available:false}};};
 globalThis.WebSocket=class{static OPEN=1;constructor(url){this.url=url;this.readyState=1;sockets.push(this);}close(){this.readyState=3;this.onclose?.();}};
 await import('./app.mjs?unapproved-public-viewer');
 assert.equal(location.hash,'');assert.equal(calls.length,0);assert.equal(sockets.length,0);assert.match(element('status').textContent,/Connect in the control app/);
 rememberGateway(sessionStorage,{appUrl:app,gateway,token},true);location.href=viewer;nodes.clear();
 await import('./app.mjs?approved-public-viewer');
 assert.equal(location.hash,'');assert.equal(location.search,'?pane=Q&observe=1&source=q');assert.equal(sockets.length,1);
 assert.equal(sockets[0].url,'ws://192.168.1.10:43118/observe/q?token='+token);
 assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
 for(const call of calls){assert.equal(new URL(call.url).origin,gateway);assert.equal(new URL(call.url).search,'');assert.equal(call.options.headers.Authorization,'Bearer '+token);}
 element('stop').onclick();assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
});
