import test from 'node:test';
import assert from 'node:assert/strict';
import {BilateralController,bilateralModel,makeAction,mountBilateralOwner} from './bilateral-owner.mjs';
const selectionId='11111111-1111-1111-1111-111111111111',requestId='22222222-2222-2222-2222-222222222222';
const member=role=>({role,deviceId:'device'+role});
const peer=role=>({...member(role),appProcessGeneration:'50',processEpochId:'app-process.50.'+role,nativeGeneration:role==='Q'?'854449628012777927':'854449628012777928',runtimeConfigId:(role==='Q'?'a':'b').repeat(64),nativeSession:'session.duplex.'+role,runId:'c'.repeat(32),mode:'own',nativeFreshness:{state:'retained',observedMonotonicNs:'100',expiresMonotonicNs:'200'},receipt:'fixture'});
const status=()=>({schema:'questms.owner.status.v2',ownerEpoch:'fixture',revision:1,observedUtc:new Date().toISOString(),selectionId,members:['Q','W'].map(member),phase:'running',activeRequestId:null,reconciliationRequired:false,readiness:{canEstablish:true,canNormalStop:true,canWhole:false,canMode:true,reasonCodes:[]},peers:['Q','W'].map(peer),receipt:'fixture'});
const result=(request,state='effective')=>({schema:'questms.owner.result.v2',requestId:request.requestId,ownerEpoch:request.ownerEpoch,selectionId:request.selectionId,action:request.action,state,actualEffectsCompleted:state==='effective',peers:state==='effective'?['Q','W'].map(role=>({...peer(role),state:'effective'})):[],receipt:'fixture'});
const storage=()=>{const values=new Map();return {getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};};
test('explicit run choices and modes never fabricate actual native or run identities',()=>{
 const current=status(),request=makeAction(current,'establish','new',null,'peer',requestId);
 assert.deepEqual(request.runChoice,{kind:'new',runId:null});assert.equal(request.mode,'peer');assert.equal(Object.hasOwn(request,'runtimeGeneration'),false);
 assert.throws(()=>makeAction(current,'establish','resume',null,'own',requestId));
 assert.equal(makeAction(current,'establish','resume','c'.repeat(32),'blend',requestId).runChoice.runId,'c'.repeat(32));
 assert.deepEqual(makeAction(current,'normalStop',null,null,null,requestId).runChoice,null);
});
test('PC publication age stays advisory; current publication cannot freshen native facts or claim video',()=>{
 const current=status();current.observedUtc=new Date(Date.now()-86400000).toISOString();
 const model=bilateralModel(current);assert.equal(model.available,true);assert.equal(model.stalePublication,true);assert.equal(model.canMode,true);
 assert.match(model.peers[0].state,/Retained/);assert.equal(model.peers[0].active,undefined);assert.notEqual(model.peers[0].nativeSession,model.peers[1].nativeSession);
});
test('lost POST survives reload; reconciliation joins original request and never sends a second mutation',async()=>{
 const retained=storage(),calls=[],request=makeAction(status(),'mode',null,null,'blend',requestId);
 const call=async(path,options)=>{calls.push({path,options});if(options?.method==='POST')throw Error('lost after effect');return result(request);};
 const first=new BilateralController(call,retained);await assert.rejects(first.execute(request));await assert.rejects(first.execute({...request,requestId:crypto.randomUUID()}));
 const reload=new BilateralController(call,retained);assert.equal(reload.pending.requestId,requestId);await reload.reconcile();assert.equal(reload.pending,null);assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);
});
test('changed owner/action/selection and fabricated effective completion retain original',async()=>{
 for(const change of ['ownerEpoch','selectionId','action','peers','actualEffectsCompleted']){
  const request=makeAction(status(),'mode',null,null,'blend',requestId),retained=storage();
  const call=async()=>{const value=result(request);value[change]=change==='peers'?[]:change==='actualEffectsCompleted'?false:'changed';return value;};
  const controller=new BilateralController(call,retained);await assert.rejects(controller.execute(request));assert.equal(controller.pending.requestId,requestId);
 }
});

test('initial capability503 keeps read-only polling alive through auth denial, readiness and disposal',async()=>{
 const nodes=new Map(),calls=[],timers=new Map(),cleared=[],oldInterval=globalThis.setInterval,oldClear=globalThis.clearInterval;
 globalThis.setInterval=callback=>{timers.set(1,callback);return 1;};globalThis.clearInterval=id=>{cleared.push(id);timers.delete(id);};
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:el,createElement:tag=>new Element(tag)};
 let phase='unready',panel;
 const call=async(path,options)=>{
  calls.push({path,options});
  if(phase==='unready')throw Object.assign(Error('Owner backend unavailable (503)'),{status:503});
  if(phase==='denied')throw Object.assign(Error('Owner authentication denied (401)'),{status:401});
  if(path.endsWith('/status'))return {...status(),selectionId:null,members:[],peers:[],phase:'unselected',readiness:{canEstablish:false,canNormalStop:false,canWhole:false,canMode:false,reasonCodes:['unselected']}};
  if(path.endsWith('/devices'))return {ownerEpoch:'fixture',revision:1,devices:['Q','W'].map(role=>({...member(role),label:'Headset '+role,ready:true}))};
  if(path.endsWith('/runs'))return {selectionId:null,revision:1,runs:[]};
  return [];
 };
 try{
  panel=await mountBilateralOwner({document,call,storage:storage(),token:'fixture'});
  assert.equal(timers.size,1,'initial read failure must not abort interval setup');
  assert.match(el('status').textContent,/unavailable/);assert.match(el('actionResult').textContent,/503/);
  phase='denied';await timers.get(1)();
  assert.match(el('status').textContent,/unavailable/);assert.equal(el('prepare').disabled,true);assert.equal(el('execute').disabled,true);assert.equal(el('selectPair').disabled,true);
  phase='ready';await timers.get(1)();
  assert.equal(el('status').textContent,'Owner connected');
  assert.equal(el('deviceQ').options.length,2);assert.equal(el('deviceW').options.length,2);
  assert.equal(el('deviceQ').value,'deviceQ');assert.equal(el('deviceW').value,'deviceW');
  assert.equal(el('selectPair').disabled,false,'unselected startup must recover useful inventory without a click');
  assert.equal(el('prepare').disabled,true,'inventory recovery does not select or start the pair');
  const deviceReads=calls.filter(c=>c.path.endsWith('/devices')).length;
  assert.equal(deviceReads,2,'one failed startup read and one successful recovery read');
  await timers.get(1)();assert.equal(calls.filter(c=>c.path.endsWith('/devices')).length,deviceReads,'available inventory does not add a recurring catalog request');
  assert.equal(calls.filter(c=>c.path.endsWith('/capabilities')).length,2,'one optional capability recovery read after startup failure, with no mutation or extra timer');
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
  const count=calls.length;panel.dispose();panel=null;
  assert.deepEqual(cleared,[1]);assert.equal(timers.size,0);
  for(const callback of timers.values())await callback();
  assert.equal(calls.length,count,'disposed interval cannot keep polling');
 }finally{panel?.dispose();globalThis.setInterval=oldInterval;globalThis.clearInterval=oldClear;}
});
test('actual UI callbacks keep mirror effects independent, hide identities and require explicit run choice',async()=>{
 const nodes=new Map(),calls=[],oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.hidden=false;this.disabled=false;this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:el,createElement:tag=>new Element(tag)};
 el('action').value='establish';el('runKind').value='';el('viewMode').value='own';
 let captures=[];
 const call=async(path,options)=>{calls.push({path,options});if(path.endsWith('/capabilities'))return {protocolVersion:2};if(path.endsWith('/status'))return status();if(path.endsWith('/devices'))return {ownerEpoch:'fixture',revision:1,devices:['Q','W'].map(role=>({...member(role),label:'Headset '+role,ready:true}))};if(path.endsWith('/runs'))return {selectionId,revision:1,runs:[{runId:'c'.repeat(32),label:'Existing run',resumable:true}]};if(path==='/api/sources')return ['q','w'].map(id=>({id,deviceId:'device'+id.toUpperCase()}));if(path==='/api/captures')return captures;if(path.startsWith('/api/captures/')){captures=[{id:'q',state:'connected',retirementPending:true}];return {state:'requested'};}if(path.endsWith('/actions'))return result(JSON.parse(options.body),'queued');if(path.includes('/actions/'))return result(makeAction(status(),'establish','new',null,'own',path.split('/').at(-1)),'running');throw Error(path);};
 try{const panel=await mountBilateralOwner({document,call,storage:storage(),token:'fixture'});assert.equal(calls.some(c=>c.options?.method==='POST'),false);assert.equal(el('prepare').disabled,true);
  el('runKind').value='new';el('runKind').onchange();await el('actionForm').onsubmit({preventDefault(){}});assert.equal(el('execute').disabled,false);
  assert.doesNotMatch(el('sessionPanel').children.map(v=>v.textContent).join(' '),/854449|app-process|session.duplex|11111111/);
  await el('startDisplayQ').onclick();assert.equal(calls.filter(c=>c.path==='/api/captures/q/start').length,1);assert.equal(calls.some(c=>c.path.endsWith('/actions')),false);
  assert.match(el('feedQ').src,/observe=1/);await el('disconnectDisplayQ').onclick();await panel.refresh();assert.equal(el('feedQ').src,'about:blank');assert.equal(calls.filter(c=>c.options?.method==='POST').length,1,'observer disconnect and polling issue no mutations');await el('disconnectDisplayQ').onclick();assert.match(el('feedQ').src,/observe=1/);
  assert.equal(calls.filter(c=>c.path.endsWith('/runs')).length,1,'unchanged owner revision keeps actual run choices without replacing the focused selector');
  await el('execute').onclick();const posted=JSON.parse(calls.find(c=>c.path.endsWith('/actions')).options.body);assert.equal(posted.runChoice.kind,'new');assert.equal(posted.runChoice.runId,null);assert.equal(posted.mode,'own');assert.equal(el('execute').disabled,true);
  await panel.refresh();assert.equal(calls.filter(c=>c.path.endsWith('/actions')).length,1);panel.dispose();
 }finally{globalThis.setInterval=oldInterval;}
});
test('uncertain pair selection survives reload and only an explicit current-status check reconciles it',async()=>{
 const retained=storage(),nodes=new Map(),calls=[],oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:el,createElement:tag=>new Element(tag)};
 const call=async(path,options)=>{calls.push({path,options});if(path.endsWith('/selection'))throw Error('lost after owner selection');if(path.endsWith('/capabilities'))return {protocolVersion:2};if(path.endsWith('/status'))return status();if(path.endsWith('/devices'))return {ownerEpoch:'fixture',revision:1,devices:['Q','W'].map(role=>({...member(role),label:role,ready:true}))};if(path.endsWith('/runs'))return {selectionId,revision:1,runs:[]};return [];};
 try{
  let panel=await mountBilateralOwner({document,call,storage:retained,token:'fixture'});await el('selectPair').onclick();assert.ok(retained.getItem('questms-owner-v2-selection'));assert.equal(el('selectPair').disabled,true);panel.dispose();
  panel=await mountBilateralOwner({document,call,storage:retained,token:'fixture'});await panel.refresh();assert.equal(el('selectPair').disabled,true);assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);
  await el('refreshSession').onclick();assert.equal(retained.getItem('questms-owner-v2-selection'),null);assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);panel.dispose();
 }finally{globalThis.setInterval=oldInterval;}
});

test('a pending app readback never delays independent mirror status and controls',async()=>{
 const nodes=new Map(),oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:el,createElement:tag=>new Element(tag)};
 const retained=storage(),request=makeAction(status(),'mode',null,null,'peer',requestId);
 let releaseReadback,readbacks=0,captureReads=0,captures=[];
 const blocked=new Promise(resolve=>releaseReadback=resolve);
 const call=async path=>{
  if(path.includes('/actions/')){readbacks++;await blocked;return result(request,'running');}
  if(path.endsWith('/status'))return status();
  if(path.endsWith('/devices'))return {ownerEpoch:'fixture',revision:1,devices:[]};
  if(path.endsWith('/runs'))return {selectionId,revision:1,runs:[]};
  if(path==='/api/sources')return [{id:'q',deviceId:'deviceQ'}];
  if(path==='/api/captures'){captureReads++;return captures;}
  return {protocolVersion:2};
 };
 let panel,mounting;
 try{
  panel=await mountBilateralOwner({document,call,storage:retained,token:'fixture'});
  retained.setItem('questms-owner-v2-pending',JSON.stringify(request));
  panel.dispose();
  mounting=mountBilateralOwner({document,call,storage:retained,token:'fixture'});
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(readbacks,1);assert.ok(captureReads>=2,'mirror endpoint is read while the app original remains pending');
  assert.equal(el('startDisplayQ').disabled,false);
  releaseReadback();panel=await mounting;
  captures=[{id:'q',state:'stopping',retirementPending:true}];
  await panel.refresh();assert.match(el('displayStateQ').textContent,/stopping/);
  assert.equal(el('startDisplayQ').disabled,true);
 }finally{releaseReadback();if(mounting)panel=await mounting;panel?.dispose();globalThis.setInterval=oldInterval;}
});

test('mode shortcut reviews Peer without POST, invalidates changed status and retains one lost Execute Original across reload',async()=>{
 const nodes=new Map(),calls=[],retained=storage(),oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.hidden=false;this.disabled=false;this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:el,createElement:tag=>new Element(tag)};
 let current=status(),posted,panel;
 const call=async(path,options)=>{
  calls.push({path,options});
  if(options?.method==='POST'){posted=JSON.parse(options.body);throw Error('fixture lost reply after original retained');}
  if(path.includes('/actions/'))return result(posted,'running');
  if(path.endsWith('/status'))return current;
  if(path.endsWith('/devices'))return {ownerEpoch:current.ownerEpoch,revision:current.revision,devices:[]};
  if(path.endsWith('/runs'))return {selectionId:current.selectionId,revision:current.revision,runs:[]};
  return [];
 };
 try{
  panel=await mountBilateralOwner({document,call,storage:retained,token:'fixture'});
  assert.equal(el('modeShortcuts').hidden,false);assert.equal(el('modePeer').disabled,false);assert.equal(typeof el('modePeer').onclick,'function','Peer shortcut must prepare through an actual UI callback');
  await el('modePeer').onclick();
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
  const reviewed=JSON.parse(el('envelope').textContent);
  assert.equal(reviewed.action,'mode');assert.equal(reviewed.mode,'peer');assert.equal(reviewed.ownerEpoch,'fixture');assert.equal(reviewed.selectionId,selectionId);assert.equal(reviewed.expectedRevision,1);assert.equal(reviewed.runChoice,null);
  assert.match(el('actionResult').textContent,/Peer/);assert.match(el('execute').textContent,/Peer/);assert.equal(el('execute').disabled,false);assert.equal(el('modePeer').ariaPressed,'true');
  current={...current,ownerEpoch:'fixture-next',revision:2};await panel.refresh();
  assert.equal(el('execute').disabled,true);assert.equal(el('envelope').textContent,'No request.');
  await el('modePeer').onclick();await el('execute').onclick();
  assert.equal(posted.mode,'peer');assert.equal(posted.ownerEpoch,'fixture-next');assert.equal(posted.expectedRevision,2);
  const original=JSON.parse(retained.getItem('questms-owner-v2-pending'));assert.equal(original.requestId,posted.requestId);assert.equal(original.mode,'peer');
  for(const id of ['modeOwn','modePeer','modeBlend'])assert.equal(el(id).disabled,true);
  await el('modeOwn').onclick();await el('execute').onclick();await panel.refresh();
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,1,'shortcuts and polling never replay a retained mutation');
  panel.dispose();panel=await mountBilateralOwner({document,call,storage:retained,token:'fixture'});
  assert.equal(el('modePeer').disabled,true);assert.equal(JSON.parse(retained.getItem('questms-owner-v2-pending')).requestId,original.requestId);
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,1,'relaunch reads the same Original');
 }finally{panel?.dispose();globalThis.setInterval=oldInterval;}
});

test('numeric Blend keeps exact Float32 intent through lost/legacy reply and capability fallback without a pending orphan',async()=>{
 const module=await import('./bilateral-owner.mjs');assert.equal(typeof module.blendAmountBits,'function','numeric draft must use one Float32 conversion');
 assert.equal(module.blendAmountBits('0.25'),1048576000);assert.equal(module.blendAmountBits('0.5'),1056964608);assert.equal(module.blendAmountValue(module.blendAmountBits('0.1')),0.10000000149011612);
 assert.throws(()=>module.blendAmountBits('-0'));assert.throws(()=>module.blendAmountBits(''));
 const request=makeAction(status(),'mode',null,null,'blend',requestId,1048576000),retained=storage(),calls=[];
 assert.equal(request.blendAmountBits,1048576000);let supported=false;
 const observation={amountBits:1048576000,controlRevision:'7',retiredFrameOrdinal:'42',qualification:'retired_configuration',receipt:'qualified fixture'};
 const call=async(path,options)=>{
  calls.push({path,options});
  if(path.endsWith('/capabilities'))return {schema:'questms.owner.capabilities.v2',ownerEpoch:'fixture',protocolVersion:2,features:supported?['blend_amount_bits_v1']:[]};
  const value=result(request);if(path.includes('/actions/'))value.peers=value.peers.map(p=>({...p,mode:'blend',blendAmount:observation}));return value;
 };
 const first=new BilateralController(call,retained);
 await assert.rejects(first.execute(request));assert.equal(retained.getItem('questms-owner-v2-pending'),null);assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);
 supported=true;await assert.rejects(first.execute(request),'plain effective without qualified numeric observations must remain pending');
 assert.equal(JSON.parse(retained.getItem('questms-owner-v2-pending')).blendAmountBits,1048576000);request.blendAmountBits=1056964608;
 const reload=new BilateralController(call,retained);await reload.reconcile();assert.equal(reload.pending,null);
 assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);assert.match(calls.find(c=>c.path.includes('/actions/')).options.headers.Accept,/questms.blend-amount-bits.v1/);
});

test('actual numeric field reviews before one Execute, recovers capability503 and keeps retained peer readback/core-only mixed shell truthful',async()=>{
 const nodes=new Map(),calls=[],oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(tag){this.tag=tag;this.options=[];this.children=[];this.dataset={};this.value='';this.hidden=false;this.disabled=false;this.parentElement={};}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];this.value='';}}
 let missingNumeric=false;
 const el=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:id=>missingNumeric&&id.startsWith('blendAmount')?null:el(id),createElement:tag=>new Element(tag)};
 const current={...status(),peers:['Q','W'].map(role=>({...peer(role),mode:'blend'}))};let capReads=0,posted,releaseCapability;const capabilityRecovery=new Promise(resolve=>releaseCapability=resolve);
 const observed={amountBits:1048576000,controlRevision:'7',retiredFrameOrdinal:'42',qualification:'retired_configuration',receipt:'retired fixture'};
 const call=async(path,options)=>{
  calls.push({path,options});
  if(path.endsWith('/capabilities')){if(++capReads===1)throw Error('503 startup');if(capReads===2)await capabilityRecovery;return {schema:'questms.owner.capabilities.v2',ownerEpoch:'fixture',protocolVersion:2,features:['blend_amount_bits_v1']};}
  if(path.endsWith('/status'))return current;
  if(path.endsWith('/devices'))return {ownerEpoch:'fixture',revision:1,devices:[]};if(path.endsWith('/runs'))return {selectionId,revision:1,runs:[]};
  if(options?.method==='POST'){posted=JSON.parse(options.body);throw Error('lost after accepted numeric intent');}
  if(path.includes('/actions/'))return result(posted,'running');return [];
 };
 let panel;
 try{
  const mounting=mountBilateralOwner({document,call,storage:storage(),token:'fixture'});await new Promise(resolve=>setImmediate(resolve));assert.equal(el('status').textContent,'Owner connected','optional slow capability must not hide useful core status');releaseCapability();panel=await mounting;assert.equal(capReads,2);
  el('modeBlend').onclick();assert.equal(el('blendAmount').disabled,false,'retained native freshness is not a numeric admission gate');
  el('blendAmount').value='0.25';el('blendAmount').oninput();assert.equal(el('execute').disabled,true);
  el('actionForm').onsubmit({preventDefault(){}});assert.equal(JSON.parse(el('envelope').textContent).blendAmountBits,1048576000);assert.match(el('execute').textContent,/0.25/);
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,0);await el('execute').onclick();assert.equal(posted.blendAmountBits,1048576000);assert.equal(el('blendAmount').disabled,true);
  current.peers[0].blendAmount=observed;await panel.refresh();assert.match(el('observations').children[0].children.map(c=>c.textContent).join(' '),/0.25.*retained/);assert.doesNotMatch(el('observations').children[1].children.map(c=>c.textContent).join(' '),/0.25/);
  assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);panel.dispose();
  missingNumeric=true;panel=await mountBilateralOwner({document,call,storage:storage(),token:'fixture'});el('modeBlend').onclick();assert.equal(el('execute').disabled,false);assert.equal('blendAmountBits' in JSON.parse(el('envelope').textContent),false,'missing new HTML remains usable preset-only');
 }finally{panel?.dispose();globalThis.setInterval=oldInterval;}
});

test('overlapping numeric capability preflights retain the first Original and send only one POST',async()=>{
 const retained=storage(),calls=[];let releaseCapabilities,releasePost;
 const capabilities=new Promise(resolve=>releaseCapabilities=resolve),postReply=new Promise(resolve=>releasePost=resolve);
 const firstRequest=makeAction(status(),'mode',null,null,'blend',requestId,1048576000),secondRequest=makeAction(status(),'mode',null,null,'blend','33333333-3333-3333-3333-333333333333',1056964608);
 const call=async(path,options)=>{
  if(path.endsWith('/capabilities')){await capabilities;return {schema:'questms.owner.capabilities.v2',ownerEpoch:'fixture',protocolVersion:2,features:['blend_amount_bits_v1']};}
  const request=JSON.parse(options.body);calls.push(request);await postReply;return result(request,'running');
 };
 const controller=new BilateralController(call,retained),first=controller.execute(firstRequest),second=controller.execute(secondRequest),settled=Promise.allSettled([first,second]);
 try{
  releaseCapabilities();await new Promise(resolve=>setImmediate(resolve));
  assert.equal(calls.length,1,'a second preflight completion must not overwrite or dispatch past the retained Original');
  assert.equal(controller.pending.requestId,requestId);assert.equal(controller.pending.blendAmountBits,1048576000);
  assert.equal(JSON.parse(retained.getItem('questms-owner-v2-pending')).requestId,requestId);
  releasePost();const outcomes=await settled;assert.equal(outcomes[0].status,'fulfilled');assert.equal(outcomes[1].status,'rejected');
 }finally{releaseCapabilities();releasePost();await settled;}
});
