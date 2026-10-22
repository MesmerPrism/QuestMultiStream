import test from 'node:test';
import assert from 'node:assert/strict';
import {LiveOwnerController,liveModel,mediaGoalModel,canRequestStop,mountLiveOwner} from './live-owner.mjs';
const now=Date.now();
function status(){const utc=n=>new Date(n).toISOString();const peer={state:'active',observedUtc:utc(now),expiresUtc:utc(now+5000),receipt:'fixture',session:'native',handshake:true,socketOpen:true,frames:2,previousFrames:1,frameUtc:utc(now),previousFrameUtc:utc(now-100)};return {schema:'questms.owner.status.v1',ownerEpoch:'owner',revision:1,observedUtc:utc(now),expiresUtc:utc(now+5000),selection:{generation:'pair',session:'pair-session',members:[{role:'Q',deviceId:'deviceQ'},{role:'W',deviceId:'deviceW'}]},phase:'running',activeRequestId:null,reconciliationRequired:false,readiness:{canStart:false,canStop:true,reasonCodes:[]},native:{session:'native',runtimeGeneration:'53',actors:[{role:'Q',processEpoch:'Q-process',nativeGeneration:'50'},{role:'W',processEpoch:'W-process',nativeGeneration:'48'}]},observations:{'Q→W.peer':{...peer},'W→Q.peer':{...peer}},displays:[],receipt:'fixture'};}
function storage(){const values=new Map();return {getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};}
test('current app health requires both current native directions; display status cannot supply it',()=>{
 const value=status();assert.equal(liveModel(value,now).overall,'Both directions active');value.observations['W→Q.peer'].socketOpen=false;assert.equal(liveModel(value,now).overall,'Partial');value.observations={};value.displays=[{state:'streaming'}];assert.equal(liveModel(value,now).overall,'Unknown');assert.equal(liveModel(status(),now+6000).available,false);const pending=status();pending.reconciliationRequired=true;assert.equal(liveModel(pending,now).canStop,false);assert.equal(liveModel(pending,now).overall,'Uncertain');
});
test('all-media health requires both actual fresh display invocations independently of native action completion',()=>{
 const value=status();value.displays=['Q','W'].map(role=>({sourceId:role,deviceId:'device'+role,producerInvocationId:'actual-'+role,producerGeneration:'display-'+role,state:'streaming',packets:3,lastPtsUs:'20',lastPacketUtc:new Date(now).toISOString(),retirementPending:true}));
 assert.equal(mediaGoalModel(value,now).state,'Active');value.displays[0].state='unknown';assert.equal(liveModel(value,now).overall,'Both directions active');assert.equal(mediaGoalModel(value,now).active,false);assert.equal(mediaGoalModel(value,now).state,'Uncertain');
 value.displays[0].state='streaming';value.displays[0].lastPacketUtc=new Date(now-4000).toISOString();assert.equal(mediaGoalModel(value,now).state,'Partial');value.phase='stopped';assert.equal(mediaGoalModel(value,now).state,'Stopping');value.displays.forEach(d=>{d.state='retired';d.retirementPending=false;});assert.equal(mediaGoalModel(value,now).state,'Stopped');
 const baseline=status();assert.equal(mediaGoalModel(baseline,now).active,false);assert.equal(mediaGoalModel(baseline,now).state,'Display goal unconfirmed');
});
test('known healthy lane survives global partial failure without greening an uncertain operation',()=>{
 const partial=status();partial.phase='unknown';partial.reconciliationRequired=true;partial.observations['W→Q.peer'].state='failed';partial.observations['W→Q.peer'].socketOpen=false;
 const model=liveModel(partial,now);assert.equal(model.peers[0].state,'Active');assert.equal(model.peers[0].socketStatus,'Reported open');assert.equal(model.peers[1].state,'Failed');assert.equal(model.overall,'Partial');assert.equal(model.operation,'Operation uncertain');
 const both=status();both.reconciliationRequired=true;assert.equal(liveModel(both,now).peers.every(p=>p.active),true);assert.equal(liveModel(both,now).overall,'Uncertain');both.reconciliationRequired=false;both.phase='stopping';assert.equal(liveModel(both,now).overall,'Stopping');assert.equal(liveModel(both,now).peers[0].state,'Active');
});
test('old advancing report preserves known open socket without claiming current video freshness',()=>{
 const value=status();value.observations['Q→W.peer'].frameUtc=new Date(now-4000).toISOString();value.observations['Q→W.peer'].previousFrameUtc=new Date(now-5000).toISOString();
 const peer=liveModel(value,now).peers[0];assert.equal(peer.active,false);assert.equal(peer.socketStatus,'Reported open');assert.match(peer.state,/socket reported open/);assert.doesNotMatch(peer.state,/socket status unconfirmed/);
});
test('lost original POST survives reload and reconciles GET without a second mutation',async()=>{
 const retained=storage(),calls=[];let lost=true;const request={requestId:'12345678-1234-1234-1234-123456789012',action:'establish',generation:'pair',direction:'both'};
 const call=async(path,options)=>{calls.push({path,options});if(options?.method==='POST'&&lost)throw Error('lost reply after dispatch');return {requestId:request.requestId,state:'effective',actualEffectsCompleted:true};};
 const first=new LiveOwnerController(call,retained);await assert.rejects(first.execute(request));await assert.rejects(first.execute({...request,requestId:'22345678-1234-1234-1234-123456789012'}));
 const reload=new LiveOwnerController(call,retained);assert.equal(reload.pending.requestId,request.requestId);lost=false;await reload.reconcile();assert.equal(reload.pending,null);assert.equal(calls.filter(c=>c.options?.method==='POST').length,1);assert.equal(calls[1].path,'/api/hub/actions/'+request.requestId);
});
test('effective without actual completion remains pending across reload',async()=>{
 const retained=storage(),id='12345678-1234-1234-1234-123456789012';let complete=false,posts=0;
 const call=async(_path,options)=>{if(options?.method==='POST')posts++;return{requestId:id,state:'effective',actualEffectsCompleted:complete};};
 const first=new LiveOwnerController(call,retained);await assert.rejects(first.execute({requestId:id}),/completion proof/);assert.equal(first.pending.requestId,id);
 const reload=new LiveOwnerController(call,retained);await assert.rejects(reload.reconcile(),/completion proof/);assert.equal(reload.pending.requestId,id);complete=true;await reload.reconcile();assert.equal(reload.pending,null);assert.equal(posts,1);
});
test('actual panel callbacks select distinct pair, independent display intents and reload observes existing feeds',async()=>{
 const nodes=new Map(),calls=[],intervals=[];const oldInterval=globalThis.setInterval;globalThis.setInterval=f=>{intervals.push(f);return 1;};
 class Element{constructor(){this.options=[];this.children=[];this.dataset={};this.value='';this.hidden=false;this.disabled=false;}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];} }
 const element=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);};element('action').value='disconnect';element('action').options=[{},{}];
 const document={getElementById:element,createElement:tag=>Object.assign(new Element(),{tag})};let captures=[{id:'q',state:'connected',retirementPending:true,captureMode:'raw-two-lens'},{id:'w',state:'connected',retirementPending:true,captureMode:'raw-two-lens'}];
 const call=async(path,options)=>{calls.push({path,options});if(path==='/api/hub/current'||path==='/api/hub/selection')return status();if(path==='/api/hub/devices')return {ownerEpoch:'owner',revision:1,devices:[{deviceId:'deviceQ',label:'Q',ready:true},{deviceId:'deviceW',label:'W',ready:true}]};if(path==='/api/sources')return [{id:'q',deviceId:'deviceQ',captureModes:['raw-two-lens','square-left']},{id:'w',deviceId:'deviceW'}];if(path==='/api/captures')return captures;if(path.includes('/api/captures/')){if(path.endsWith('/stop'))captures=captures.map(c=>c.id==='q'?{...c,state:'retired',retirementPending:false}:c);return {state:'requested'};}throw Error(path);};
 try{const panel=await mountLiveOwner({document,call,storage:storage(),token:'observer'});assert.match(element('feedQ').src,/observe=1&source=q/);assert.match(element('feedW').src,/observe=1&source=w/);assert.equal(calls.some(c=>c.options?.method==='POST'),false,'reload starts no producers');
 assert.equal(element('captureModeQ').disabled,true);assert.equal(element('captureModeQ').value,'raw-two-lens');
 await element('stopDisplayQ').onclick();assert.equal(element('feedQ').src,'about:blank');assert.match(element('feedW').src,/source=w/);assert.equal(calls.filter(c=>c.path.includes('/api/captures/')&&c.options?.method==='POST').length,1);
 assert.equal(element('captureModeQ').disabled,false);element('captureModeQ').value='square-left';await panel.refresh();assert.equal(calls.filter(c=>c.path.includes('/api/captures/')&&c.options?.method==='POST').length,1,'choosing mode and refreshing never starts capture');
 await element('startDisplayQ').onclick();assert.equal(JSON.parse(calls.find(c=>c.path==='/api/captures/q/start').options.body).captureMode,'square-left');assert.equal(Object.hasOwn(JSON.parse(calls.find(c=>c.path==='/api/captures/q/stop').options.body),'captureMode'),false);
 captures[0]={id:'q',state:'unknown',retirementPending:true,captureMode:'raw-two-lens'};await panel.refresh();assert.equal(element('captureModeQ').disabled,true);assert.equal(element('captureModeQ').value,'raw-two-lens');assert.match(element('displayStateQ').textContent,/Raw two-lens/);
 element('deviceQ').value='deviceQ';element('deviceW').value='deviceQ';await element('selectPair').onclick();assert.match(element('actionResult').textContent,/distinct/);assert.equal(calls.some(c=>c.path==='/api/hub/selection'),false);panel.dispose();
 }finally{globalThis.setInterval=oldInterval;}
});

test('one explicit Stop retains unknown Start and lost or denied Stop originals across reload without replay',async()=>{
 for(const failure of ['lost reply','HTTP denial']){
  const retained=storage(),calls=[],start={requestId:'12345678-1234-1234-1234-123456789012',action:'establish',direction:'both',generation:'pair'},stop={...start,action:'disconnect',requestId:'22345678-1234-1234-1234-123456789012'};
  const pending=status();pending.phase='unknown';pending.reconciliationRequired=true;pending.readiness.canStop=false;
  const call=async(path,options)=>{calls.push({path,options});if(options?.method==='POST'){const id=JSON.parse(options.body).requestId;assert.ok(JSON.parse(retained.getItem('questms-owner-pending')).requests.some(r=>r.requestId===id),'persist before possible POST');throw Error(failure);}return {requestId:path.split('/').at(-1),state:path.endsWith(stop.requestId)?'failed':'unknown',actualEffectsCompleted:false};};
  const controller=new LiveOwnerController(call,retained);await assert.rejects(controller.execute(start));assert.equal(canRequestStop(pending),true);assert.equal(liveModel(pending).canStop,false);assert.notEqual(liveModel(pending).overall,'Both directions active');
  await assert.rejects(controller.requestStop(stop,pending));await assert.rejects(controller.requestStop({...stop,requestId:'32345678-1234-1234-1234-123456789012'},pending));assert.deepEqual(controller.pendingRequests.map(r=>r.requestId),[start.requestId,stop.requestId]);
  const reload=new LiveOwnerController(call,retained);await reload.reconcileAll();assert.deepEqual(reload.pendingRequests.map(r=>r.requestId),[start.requestId]);assert.equal(calls.filter(c=>c.options?.method==='POST').length,2);assert.equal(calls.filter(c=>!c.options).length,2);
 }
 const retained=storage(),id='12345678-1234-1234-1234-123456789012';retained.setItem('questms-owner-pending',JSON.stringify({requestId:id,action:'establish'}));assert.equal(new LiveOwnerController(()=>{},retained).pending.requestId,id,'migrate original single record');
});

test('actual Request Stop callback runs during an awaiting Start, fresh-checks pair and retains both originals',async()=>{
 const nodes=new Map(),calls=[],retained=storage(),oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;
 class Element{constructor(){this.options=[];this.children=[];this.dataset={};this.value='';}append(e){this.children.push(e);if(e.tag==='option'){this.options.push(e);if(!this.value)this.value=e.value;}}replaceChildren(){this.children=[];this.options=[];}}
 const element=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);},document={getElementById:element,createElement:tag=>Object.assign(new Element(),{tag})};element('action').value='establish';element('action').options=[{value:'establish'},{value:'disconnect'}];
 let live=status(),startId=null,stopId=null,releaseStart;live.phase='stopped';live.readiness={canStart:true,canStop:false};
 const call=async(path,options)=>{calls.push({path,options});if(path==='/api/hub/current')return structuredClone(live);if(path==='/api/hub/devices')return {devices:[]};if(['/api/captures','/api/sources'].includes(path))return [];
  if(path==='/api/hub/actions'){const body=JSON.parse(options.body);if(!body.execute)return {state:'prepared'};assert.ok(JSON.parse(retained.getItem('questms-owner-pending')).requests.some(r=>r.requestId===body.requestId));if(body.action==='establish'){startId=body.requestId;live.phase='unknown';live.activeRequestId=startId;live.reconciliationRequired=true;live.readiness={canStart:false,canStop:false};return await new Promise(resolve=>{releaseStart=()=>resolve({requestId:startId,state:'unknown',actualEffectsCompleted:false});});}stopId=body.requestId;throw Error('modeled lost Stop reply');}
  if(path.startsWith('/api/hub/actions/'))return {requestId:path.split('/').at(-1),state:'unknown',actualEffectsCompleted:false};throw Error(path);};
 try{const panel=await mountLiveOwner({document,call,storage:retained,token:'observer'});await element('actionForm').onsubmit({preventDefault(){}});const starting=element('execute').onclick();await Promise.resolve();assert.ok(startId);assert.equal(element('requestStop').disabled,false,'Start POST does not own Stop intent');
  await element('requestStop').onclick();assert.ok(stopId);assert.equal(element('requestStop').disabled,true);assert.equal(element('prepare').disabled,true);assert.equal(element('execute').disabled,true);assert.equal(liveModel(live).canStop,false);assert.notEqual(liveModel(live).overall,'Both directions active');assert.deepEqual(new LiveOwnerController(call,retained).pendingRequests.map(r=>r.requestId),[startId,stopId]);
  const before=calls.filter(c=>c.options?.method==='POST').length;await element('requestStop').onclick();await panel.refresh();assert.equal(calls.filter(c=>c.options?.method==='POST').length,before);releaseStart();await starting;panel.dispose();
 }finally{globalThis.setInterval=oldInterval;}
});

test('stale, unavailable or changed selected pair never sends a Stop and a missing capability hides intent',async()=>{
 const stale=status();stale.expiresUtc=new Date(now-1).toISOString();assert.equal(canRequestStop(stale),false);assert.equal(canRequestStop(null),false);const unselected=status();unselected.selection.members=[];assert.equal(canRequestStop(unselected),false);
 for(const change of ['pair','stale','denied','capability']){
  const nodes=new Map(),oldInterval=globalThis.setInterval;globalThis.setInterval=()=>1;let reads=0,posts=0;
  const element=id=>{if(!nodes.has(id))nodes.set(id,{options:[],children:[],dataset:{},append(){},replaceChildren(){}});return nodes.get(id);};element('action').value='establish';
  const document={getElementById:element,createElement:()=>({append(){}})};
  const call=async(path,options)=>{if(options?.method==='POST'){posts++;throw Error('Unexpected POST');}if(path==='/api/hub/current'){reads++;if(reads===1)return status();if(change==='denied')throw Error('Unavailable');const value=status();if(change==='pair')value.selection.generation='new-pair';else value.expiresUtc=new Date(now-1).toISOString();return value;}if(path==='/api/hub/devices')return {devices:[]};return [];};
  try{const panel=await mountLiveOwner({document,call,storage:storage(),token:'observer',actions:change==='capability'?['establish']:['establish','disconnect']});if(change==='capability')assert.equal(element('requestStop').hidden,true);await element('requestStop').onclick();assert.equal(posts,0);panel.dispose();}finally{globalThis.setInterval=oldInterval;}
 }
});

test('failed browser intent storage prevents Stop POST',async()=>{
 let posts=0;const retained=storage();retained.setItem=()=>{throw Error('Storage unavailable');};const controller=new LiveOwnerController(async()=>{posts++;},retained);
 await assert.rejects(controller.requestStop({requestId:'12345678-1234-1234-1234-123456789012',action:'disconnect',direction:'both',generation:'pair'},status()),/Storage unavailable/);assert.equal(posts,0);assert.equal(controller.pending,null);
});
