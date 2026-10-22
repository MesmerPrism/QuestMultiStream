import test from 'node:test';
import assert from 'node:assert/strict';

test('actual embedded viewer reload subscribes without a producer Start; viewer close sends no Stop',async()=>{
 const calls=[],sockets=[],nodes=new Map(),context={drawImage(){},clearRect(){},putImageData(){}};
 const element=id=>{if(!nodes.has(id))nodes.set(id,{value:id==='source'?'q':id==='view'?'full':'',textContent:'',style:{},options:[{}],files:[],hidden:false,append(o){this.options.push(o);},getContext(){return context;},pause(){},removeAttribute(){}});return nodes.get(id);};
 globalThis.document={hidden:false,body:{classList:{add(){}}},getElementById:element,createElement:()=>({getContext:()=>context}),addEventListener(){}};
 globalThis.location={hash:'#token=observer',pathname:'/viewer.html',search:'?pane=Q&observe=1&source=q',host:'127.0.0.1:43118'};
 globalThis.history={replaceState(){}};globalThis.sessionStorage={getItem(){return null;},setItem(){}};globalThis.setInterval=()=>1;globalThis.window={VideoDecoder:true};
 globalThis.fetch=async(url,options)=>{calls.push({url,options});return{ok:true,json:async()=>url.startsWith('/api/sources')?[{id:'q',label:'Q',displayId:0}]:{available:false}};};
 globalThis.WebSocket=class{static OPEN=1;constructor(url){this.url=url;this.readyState=1;sockets.push(this);}close(){this.readyState=3;this.onclose?.();}};
 await import('./app.mjs?observer-load');assert.equal(sockets.length,1);assert.match(sockets[0].url,/\/observe\/q/);element('stop').onclick();
 nodes.clear();await import('./app.mjs?observer-reload');assert.equal(sockets.length,2);assert.match(sockets[1].url,/\/observe\/q/);assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
});
