// Current owner status is separate from mirror packets and observer decode state.
import {captureModeLabel,populateCaptureModes} from './capture-mode.mjs';
export function liveModel(value,now=Date.now()) {
  if(value?.schema!=='questms.owner.status.v1'||!value.ownerEpoch||!Number.isSafeInteger(value.revision)||!Number.isFinite(Date.parse(value.observedUtc))||Date.parse(value.observedUtc)>now||Date.parse(value.expiresUtc)<=now||now-Date.parse(value.observedUtc)>10000||!Number.isFinite(Date.parse(value.expiresUtc)))return {available:false,overall:'Unknown',canStart:false,canStop:false,peers:[]};
  const peers=['Q→W.peer','W→Q.peer'].map(lane=>{
    const p=value.observations?.[lane],age=p?.frameUtc?now-Date.parse(p.frameUtc):Infinity;
    const current=p&&Date.parse(p.observedUtc)<=now&&Date.parse(p.expiresUtc)>now;
    const advancing=current&&Number.isSafeInteger(p.frames)&&Number.isSafeInteger(p.previousFrames)&&p.frames>p.previousFrames&&Date.parse(p.previousFrameUtc)<Date.parse(p.frameUtc)&&age>=0;
    const active=advancing&&age<=3000&&p.state==='active'&&p.handshake===true&&p.socketOpen===true&&p.session===value.native?.session&&value.native?.actors?.length===2;
    const socketStatus=current&&p.socketOpen===true?'Reported open':'Unknown';
    return {lane,state:active?'Active':current&&p.state==='failed'?'Failed':current&&p.state==='disconnected'?'Stopped':advancing&&p.handshake===true?(socketStatus==='Reported open'?'Advancing reports · socket reported open; current freshness unconfirmed':'Advancing reports · socket status unconfirmed'):'Unknown',active,socketStatus};
  });
  const unsettled=value.reconciliationRequired||value.activeRequestId!=null;
  const both=peers.every(p=>p.active),some=peers.some(p=>p.active),certain=value.phase==='running'&&!value.reconciliationRequired;
  const operation=value.reconciliationRequired?'Operation uncertain':value.phase==='stopping'?'Stopping':value.phase==='starting'||value.phase==='preparing'?'Starting':value.phase==='unknown'?'Current session uncertain':value.phase;
  return {available:true,overall:both&&certain?'Both directions active':some&&!both?'Partial':both?(value.phase==='stopping'?'Stopping':'Uncertain'):'Unknown',operation,canStart:!unsettled&&value.readiness?.canStart===true,canStop:!unsettled&&value.readiness?.canStop===true,peers};
}
export function mediaGoalModel(value,now=Date.now()) {
  const app=liveModel(value,now),members=value?.selection?.members??[],feeds=value?.displays??[];
  if(!app.available)return {active:false,state:'Unknown'};
  if(feeds.length!==2||members.length!==2||new Set(feeds.map(f=>f.sourceId)).size!==2||new Set(feeds.map(f=>f.deviceId)).size!==2||feeds.some(f=>!members.some(m=>m.deviceId===f.deviceId)))return {active:false,state:'Display goal unconfirmed'};
  const active=feeds.map(f=>{const stamp=Date.parse(f.lastPacketUtc),age=now-stamp;return f.state==='streaming'&&!!f.producerInvocationId&&!!f.producerGeneration&&Number.isSafeInteger(f.packets)&&f.packets>=3&&typeof f.lastPtsUs==='string'&&/^\d+$/.test(f.lastPtsUs)&&Number.isSafeInteger(Number(f.lastPtsUs))&&Number.isFinite(stamp)&&age>=0&&age<=3000;});
  if(app.overall==='Both directions active'&&active.every(Boolean))return {active:true,state:'Active'};
  if(value.reconciliationRequired||feeds.some(f=>f.state==='unknown'))return {active:false,state:'Uncertain'};
  if(value.phase==='stopped'&&feeds.every(f=>['retired','failed'].includes(f.state)&&f.retirementPending===false))return {active:false,state:'Stopped'};
  if(value.phase==='stopping'||value.phase==='stopped'&&feeds.some(f=>f.retirementPending))return {active:false,state:'Stopping'};
  return {active:false,state:active.some(Boolean)||app.peers.some(p=>p.active)?'Partial':'Pending'};
}
export function canRequestStop(value,now=Date.now()) {
  const pair=value?.selection,members=pair?.members;
  return liveModel(value,now).available&&typeof pair?.generation==='string'&&pair.generation.length>0&&typeof pair?.session==='string'&&pair.session.length>0&&Array.isArray(members)&&members.length===2&&members.map(m=>m?.role).sort().join(',')==='Q,W'&&members.every(m=>typeof m?.deviceId==='string'&&m.deviceId.length>0)&&members[0].deviceId!==members[1].deviceId&&['preparing','starting','running','stopping','stopped','reconciling','unknown','failed'].includes(value.phase);
}
export class LiveOwnerController {
  constructor(call,storage){this.call=call;this.storage=storage;this.originals=[];try{const p=JSON.parse(storage.getItem('questms-owner-pending'));const values=p?.schema==='questms.owner.pending.v2'?p.requests:p?[p]:[];if(Array.isArray(values)&&values.length<=9&&values.every(v=>v&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(v.requestId))&&new Set(values.map(v=>v.requestId)).size===values.length)this.originals=values;}catch{}}
  get pending(){return this.originals[0]??null;}
  get pendingRequests(){return this.originals.map(v=>({...v}));}
  get stopPending(){return this.originals.some(v=>v.action==='disconnect');}
  persist(values){if(values.length)this.storage.setItem('questms-owner-pending',JSON.stringify({schema:'questms.owner.pending.v2',requests:values}));else this.storage.removeItem('questms-owner-pending');this.originals=values;}
  remember(value){const old=this.originals.find(v=>v.requestId===value.requestId);if(old){if(!old.action&&value.action)this.persist(this.originals.map(v=>v===old?{...v,...value}:v));return;}if(this.originals.length>=9)throw Error('Original operation capacity reached; reconcile retained requests');this.persist([...this.originals,{...value}]);}
  async execute(request){if(this.pending)throw Error('An original operation still requires reconciliation');this.remember(request);return this.result(await this.call('/api/hub/actions',{method:'POST',body:JSON.stringify({...request,execute:true})}));}
  async requestStop(request,current){if(request.action!=='disconnect'||request.direction!=='both'||request.generation!==current?.selection?.generation||!canRequestStop(current)||this.stopPending)throw Error('Fresh selected pair and one new Stop intent required');this.remember(request);return this.result(await this.call('/api/hub/actions',{method:'POST',body:JSON.stringify({...request,execute:true})}));}
  result(value){if(!this.originals.some(v=>v.requestId===value.requestId))throw Error('Original request identity differs');if(value.state==='effective'&&value.actualEffectsCompleted!==true)throw Error('Effective result lacks actual completion proof');if(value.state==='effective'||value.state==='failed')this.persist(this.originals.filter(v=>v.requestId!==value.requestId));return value;}
  async reconcile(){if(!this.pending)return null;return this.result(await this.call('/api/hub/actions/'+encodeURIComponent(this.pending.requestId)));}
  async reconcileAll(){return Promise.all(this.pendingRequests.map(async original=>{try{return {requestId:original.requestId,result:this.result(await this.call('/api/hub/actions/'+encodeURIComponent(original.requestId)))};}catch{return {requestId:original.requestId,result:null};}}));}
}

export async function mountLiveOwner({document,call,storage,token,actions=['establish','disconnect']}) {
  const $=id=>document.getElementById(id),controller=new LiveOwnerController(call,storage);
  let current=null,inventory=null,prepared=null,busy=false,stopSending=false,refreshing=false;
  const say=text=>$('actionResult').textContent=text;
  $('pairSelection').hidden=false;$('sessionTitle').textContent='Current pair owner';$('refreshSession').textContent='Refresh current status';
  for(const option of $('action').options)option.textContent=option.value==='establish'?'Start':'Stop';$('prepare').textContent='Review request';$('execute').textContent='Select pair first';$('requestStop').hidden=!actions.includes('disconnect');
  function controls(){const m=liveModel(current);$('prepare').disabled=busy||!!controller.pending||!m.available||!current?.selection?.generation||!($('action').value==='establish'?m.canStart:m.canStop);$('execute').disabled=busy||!!controller.pending||!prepared||prepared.generation!==current?.selection?.generation||!($('action').value==='establish'?m.canStart:m.canStop);$('selectPair').disabled=busy||!!controller.pending||!inventory||!!current?.activeRequestId||current?.reconciliationRequired===true;$('requestStop').disabled=stopSending||controller.stopPending||!actions.includes('disconnect')||!canRequestStop(current);}
  $('action').onchange=()=>{prepared=null;controls();};
  function render(){
    const model=liveModel(current),goal=mediaGoalModel(current);$('status').textContent=model.available?'Owner connected':'Owner unavailable';$('controller').textContent=model.available?`App peer video: ${model.overall} · ${model.operation} · All media: ${goal.state}`:'Current authority unknown';
    $('sessionPanel').replaceChildren();const summary=document.createElement('p');summary.textContent=model.available?`Selection ${current.selection?.generation??'none'} · native runtime ${current.native?.runtimeGeneration??'unavailable'} · ${current.readiness?.reasonCodes?.join(', ')||'No additional readiness report'}`:'No current authenticated owner report.';$('sessionPanel').append(summary);
    $('observations').replaceChildren();for(const peer of model.peers){const row=document.createElement('tr');for(const text of [peer.lane,peer.state,'Current owner report','Display decoding is separate']){const cell=document.createElement('td');cell.textContent=text;row.append(cell);}$('observations').append(row);$('link'+(peer.lane.startsWith('Q')?'QW':'WQ')).textContent=peer.lane.replace('.peer','')+': '+peer.state;}
    if(!model.available){$('linkQW').textContent='Q → W: Unknown';$('linkWQ').textContent='W → Q: Unknown';}
    $('reports').textContent=JSON.stringify(current,null,2);controls();
  }
  async function refresh(){if(refreshing)return;refreshing=true;try{
    if(controller.pending){const originals=await controller.reconcileAll();say(originals.some(v=>!v.result)?'Original outcome unknown · reconciliation required':controller.pending?'Original requests retained · completion unconfirmed':'Original requests reconciled');}
    current=await call('/api/hub/current');if(current.activeRequestId)controller.remember({requestId:current.activeRequestId,generation:current.selection?.generation,direction:'both',...(current.phase==='stopping'?{action:'disconnect'}:{})});render();
    const captures=await call('/api/captures');const sources=await call('/api/sources');
    for(const role of ['Q','W']){const member=current.selection?.members?.find(m=>m.role===role),source=sources.find(s=>s.deviceId===member?.deviceId),capture=captures.find(c=>c.id===source?.id),feed=$('feed'+role);populateCaptureModes(document,$('captureMode'+role),source,capture);if(!source)$('captureMode'+role).disabled=true;$('displayState'+role).textContent='Display producer: '+(capture?.state??'not started')+(capture?.captureMode?' · '+captureModeLabel(capture.captureMode):'');$('startDisplay'+role).disabled=!liveModel(current).available||current.reconciliationRequired||!['starting','running'].includes(current.phase)||!source||capture?.retirementPending===true||['requested','connected','unknown','stopping'].includes(capture?.state);$('stopDisplay'+role).disabled=!source||!capture?.retirementPending||capture.state==='unknown';if(source&&['requested','connected'].includes(capture?.state)){const url='viewer.html?pane='+role+'&observe=1&source='+encodeURIComponent(source.id)+'#token='+encodeURIComponent(token);if(feed.dataset.source!==source.id){feed.dataset.source=source.id;feed.src=url;}}else if(feed.dataset.source){delete feed.dataset.source;feed.src='about:blank';}}
  }catch{current=null;prepared=null;render();for(const role of ['Q','W'])$('captureMode'+role).disabled=true;}finally{refreshing=false;}}
  async function devices(){try{inventory=await call('/api/hub/devices');for(const role of ['Q','W']){const node=$('device'+role),previous=node.value;node.replaceChildren();for(const d of inventory.devices){const option=document.createElement('option');option.value=d.deviceId;option.textContent=d.label+(d.ready?'':' · unavailable');option.disabled=!d.ready;node.append(option);}if([...node.options].some(o=>o.value===previous))node.value=previous;}if($('deviceW').value===$('deviceQ').value&&$('deviceW').options.length>1)$('deviceW').value=$('deviceW').options[1].value;}catch{inventory=null;}controls();}
  $('selectPair').onclick=async()=>{if(!inventory||busy)return;const q=$('deviceQ').value,w=$('deviceW').value;if(!q||q===w){say('Select two distinct headsets');return;}busy=true;controls();try{await call('/api/hub/selection',{method:'POST',body:JSON.stringify({schema:'questms.owner.selection.v1',requestId:crypto.randomUUID(),ownerEpoch:inventory.ownerEpoch,expectedRevision:inventory.revision,members:[{role:'Q',deviceId:q},{role:'W',deviceId:w}]})});prepared=null;say('Pair selected · no media started');}catch{say('Selection unresolved or denied · refresh owner status');}finally{busy=false;await refresh();}};
  $('refreshDevices').onclick=devices;
  $('actionForm').onsubmit=async event=>{event.preventDefault();if(busy||controller.pending||!current)return;busy=true;controls();try{const request={action:$('action').value,direction:'both',generation:current.selection.generation,requestId:crypto.randomUUID(),execute:false};await call('/api/hub/actions',{method:'POST',body:JSON.stringify(request)});prepared=request;$('envelope').textContent=JSON.stringify(request,null,2);$('execute').textContent=request.action==='establish'?'Start':'Stop';say('Request reviewed · owner rechecks admission on execution');}catch(error){prepared=null;say(error.message);}finally{busy=false;controls();}};
  $('execute').onclick=async()=>{if(!prepared||busy||controller.pending)return;busy=true;controls();try{const result=await controller.execute(prepared);say(`Request ${result.state} · ${result.actualEffectsCompleted?'completed':'completion unconfirmed'}`);prepared=null;}catch{say('Outcome unknown · original request retained, no retry');}finally{busy=false;await refresh();}};
  $('requestStop').onclick=async()=>{
    if(stopSending||controller.stopPending||!actions.includes('disconnect')||!canRequestStop(current))return;
    const expected=JSON.stringify([current.ownerEpoch,current.selection]);let request=null;stopSending=true;controls();
    try{const fresh=await call('/api/hub/current');if(!canRequestStop(fresh)||JSON.stringify([fresh.ownerEpoch,fresh.selection])!==expected)throw Error('Current selected pair changed or unavailable');current=fresh;request={action:'disconnect',direction:'both',generation:fresh.selection.generation,requestId:crypto.randomUUID()};const result=await controller.requestStop(request,fresh);say(`Stop request ${result.state} · ${result.actualEffectsCompleted?'owner reports completion':'actual cleanup unconfirmed'}`);}
    catch{say(request?'Stop outcome unconfirmed · retained original IDs reconcile by GET; no retry':'Fresh selected pair unavailable or changed · no Stop sent');}
    finally{stopSending=false;await refresh();}
  };
  for(const role of ['Q','W'])for(const action of ['start','stop']){$(action+'Display'+role).hidden=false;$(action+'Display'+role).onclick=async()=>{try{const sources=await call('/api/sources'),device=current?.selection?.members?.find(m=>m.role===role)?.deviceId,source=sources.find(s=>s.deviceId===device);if(!source)throw Error('Selected display unavailable');const body={requestId:crypto.randomUUID()};if(action==='start'){body.captureMode=$('captureMode'+role).value||'raw-two-lens';$('captureMode'+role).disabled=true;}await call('/api/captures/'+encodeURIComponent(source.id)+'/'+action,{method:'POST',body:JSON.stringify(body)});}catch(error){$('displayState'+role).textContent=error.message;}await refresh();};}
  $('refreshSession').onclick=refresh;$('stopMonitor').onclick=async()=>{try{await call('/operator-stop',{method:'POST'});$('status').textContent='Monitor stop requested · producer retirement pending';}catch{$('status').textContent='Monitor stop outcome unknown';}};
  await devices();await refresh();const timer=setInterval(refresh,1000);return {refresh,dispose(){clearInterval(timer);}};
}
