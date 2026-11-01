import test from 'node:test';
import assert from 'node:assert/strict';
const module=()=>import('./gateway.mjs');
const app='https://controls.example/bilateral-control/';
const token='0123456789abcdef0123456789abcdef0123456789abcdef';
const link=app+'#gateway=http%3A%2F%2F192.168.1.10%3A43118&token='+token;
function memory(){const values=new Map();return {values,getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};}

test('operator gateway links admit only exact HTTPS app scope, canonical private IPv4 HTTP and one ephemeral token',async()=>{
 const {parseGatewayLink}=await module();
 assert.deepEqual(parseGatewayLink(link,app),{appUrl:app,gateway:'http://192.168.1.10:43118',token});
 assert.equal(parseGatewayLink(link.replace('192.168.1.10','127.0.0.1'),app).gateway,'http://127.0.0.1:43118');
 for(const bad of [link.replace('https:','http:'),link.replace('/bilateral-control/','/other/'),link.replace('controls.example','evil.example'),link.replace('192.168.1.10','8.8.8.8'),link.replace('192.168.1.10','localhost'),link.replace('192.168.1.10','192.168.001.10'),link.replace('http%3A','https%3A'),link+'&token='+token,link+'&extra=x',link.replace(token,'short'),link.replace('#','?x=1#'),link.replace('192.168.1.10','u%3Ap%40192.168.1.10'),link.replace('%3A43118','%3A43118%2Fapi')])assert.throws(()=>parseGatewayLink(bad,app),undefined,bad);
});

test('REST uses one bearer with safe fetch options, WS alone carries query token, viewer stays at secure app',async()=>{
 const {parseGatewayLink,createTransport}=await module(),calls=[];
 const transport=createTransport(parseGatewayLink(link,app),async(url,options)=>{calls.push({url,options});return {ok:true};});
 await transport.fetch('/api/hub/v2/status',{headers:{Accept:'application/json; profile="questms.blend-amount-bits.v1"'}});
 assert.equal(calls[0].url,'http://192.168.1.10:43118/api/hub/v2/status');
 assert.deepEqual(calls[0].options,{headers:{Accept:'application/json; profile="questms.blend-amount-bits.v1"',Authorization:'Bearer '+token},credentials:'omit',cache:'no-store',redirect:'error',referrerPolicy:'no-referrer',targetAddressSpace:'local'});
 assert.equal(transport.webSocket('/observe/q'),'ws://192.168.1.10:43118/observe/q?token='+token);
 assert.equal(transport.viewer('Q','q',true),'https://controls.example/bilateral-control/viewer.html?pane=Q&source=q&observe=1#gateway=http%3A%2F%2F192.168.1.10%3A43118&token='+token);
 for(const path of ['https://evil.example/api','//evil.example/api','/api/x?token=leak','/api/../owner','#x'])assert.throws(()=>transport.webSocket(path));
 await assert.rejects(()=>transport.fetch('/api/hub',{headers:{authorization:'other'}}));
 assert.equal(calls.length,1);
});

test('credentials, consent and pending originals are session/app/gateway scoped without touching sibling keys',async()=>{
 const {parseGatewayLink,rememberGateway,restoreGateway,scopedStorage,approvedViewer}=await module(),store=memory(),context=parseGatewayLink(link,app);
 store.setItem('viscereality-token','sibling');store.setItem('questms-owner-v2-pending','legacy');
 rememberGateway(store,context,true);
 assert.deepEqual(restoreGateway(store,app),{...context,consent:true});
 const a=scopedStorage(store,context),b=scopedStorage(store,{...context,gateway:'http://192.168.1.11:43118'});
 a.setItem('questms-owner-v2-pending','original');assert.equal(b.getItem('questms-owner-v2-pending'),null);
 assert.equal(store.getItem('questms-owner-v2-pending'),'legacy');assert.equal(store.getItem('viscereality-token'),'sibling');
 assert.equal(approvedViewer(store,context),true);assert.equal(approvedViewer(memory(),context),false);assert.equal(approvedViewer(store,{...context,token:'a'.repeat(48)}),false);
 assert.equal(restoreGateway(store,'https://controls.example/sibling/'),null);
});

test('reload permission checks use only granular names and fail closed on unsupported, prompt or denial',async()=>{
 const {gatewayPermissionGranted}=await module(),names=[];
 for(const state of ['prompt','denied','granted'])assert.equal(await gatewayPermissionGranted({permissions:{query:async({name})=>{names.push(name);return {state};}}},'http://192.168.1.10:43118'),state==='granted');
 assert.equal(await gatewayPermissionGranted({permissions:{query:async({name})=>{names.push(name);throw Error('unsupported');}}},'http://127.0.0.1:43118'),false);
 assert.deepEqual(names,['local-network','local-network','local-network','loopback-network']);
});

test('a lost public Execute retains its scoped Original across reload and only reconciles GET, never a second mutation',async()=>{
 const {parseGatewayLink,scopedStorage}=await module(),{BilateralController}=await import('./bilateral-owner.mjs');
 const context=parseGatewayLink(link,app),store=memory(),scope=scopedStorage(store,context),calls=[];
 const original={schema:'questms.owner.action.v2',requestId:'11111111-1111-4111-8111-111111111111',selectionId:'22222222-2222-4222-8222-222222222222',ownerEpoch:'owner-epoch',expectedRevision:1,action:'mode',runChoice:null,mode:'peer'};
 const before=new BilateralController(async(path,options)=>{calls.push({path,options});throw Error('lost response');},scope);
 await assert.rejects(()=>before.execute(original));
 const reloaded=new BilateralController(async(path,options)=>{calls.push({path,options});return {schema:'questms.owner.result.v2',requestId:original.requestId,selectionId:original.selectionId,ownerEpoch:original.ownerEpoch,action:'mode',state:'unknown',actualEffectsCompleted:false};},scopedStorage(store,context));
 assert.deepEqual(reloaded.pending,original);await reloaded.reconcile();assert.deepEqual(reloaded.pending,original);
 assert.equal(calls.filter(call=>call.options?.method==='POST').length,1);assert.equal(calls[1].path,'/api/hub/v2/actions/11111111-1111-4111-8111-111111111111');
 assert.equal(new BilateralController(()=>{},scopedStorage(store,{...context,gateway:'http://192.168.1.11:43118'})).pending,null);
 assert.equal(store.getItem('questms-owner-v2-pending'),null);
});
