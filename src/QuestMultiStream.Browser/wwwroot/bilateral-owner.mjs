import {captureModeLabel,populateCaptureModes} from './capture-mode.mjs';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const runId=/^[0-9a-f]{32}$/;
const actions=['establish','normalStop','whole','mode'],modes=['own','peer','blend'];
const modeLabels={own:'Own',peer:'Peer',blend:'Blend'};
const blendAccept='application/json; profile="questms.blend-amount-bits.v1"';
const blendRead={headers:{Accept:blendAccept}};
const validAmount=bits=>Number.isInteger(bits)&&bits>=0&&bits<=1065353216;
const amountSupported=(caps,epoch)=>caps?.schema==='questms.owner.capabilities.v2'&&caps.ownerEpoch===epoch&&caps.protocolVersion===2&&Array.isArray(caps.features)&&caps.features.includes('blend_amount_bits_v1');
export function blendAmountBits(text){
 if(!/^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$/.test(String(text).trim()))throw Error('Enter a strength from 0 to 1');
 const value=Number(text);if(!Number.isFinite(value)||value<0||value>1||Object.is(value,-0))throw Error('Enter a strength from 0 to 1');
 const data=new DataView(new ArrayBuffer(4));data.setFloat32(0,Math.fround(value),true);return data.getUint32(0,true);
}
export function blendAmountValue(bits){if(!validAmount(bits))throw Error('Invalid Float32 strength');const data=new DataView(new ArrayBuffer(4));data.setUint32(0,bits,true);return data.getFloat32(0,true);}
function amountObservation(peer){
 const value=peer?.blendAmount,positive=text=>typeof text==='string'&&/^[1-9][0-9]{0,19}$/.test(text)&&BigInt(text)<=18446744073709551615n;
 if(peer?.mode!=='blend'||!value||Object.keys(value).sort().join(',')!=='amountBits,controlRevision,qualification,receipt,retiredFrameOrdinal'||!validAmount(value.amountBits)||!positive(value.controlRevision)||!positive(value.retiredFrameOrdinal)||value.qualification!=='retired_configuration'||typeof value.receipt!=='string'||!value.receipt.length||value.receipt.length>4096)return null;
 if(!peer.appProcessGeneration||!peer.processEpochId||!peer.nativeGeneration||!peer.runtimeConfigId||!peer.nativeSession||!peer.runId)return null;
 return value;
}
const labels={establish:'Start app streaming',normalStop:'Stop app streaming',whole:'Close app session',mode:'Change view mode'};
export function bilateralModel(value,now=Date.now()){
 const unavailable={available:false,canEstablish:false,canNormalStop:false,canWhole:false,canMode:false,peers:[]};
 if(value?.schema!=='questms.owner.status.v2'||!value.ownerEpoch||!Number.isSafeInteger(value.revision)||value.revision<0||!Array.isArray(value.members)||!Array.isArray(value.peers)||!value.readiness||!Number.isFinite(Date.parse(value.observedUtc)))return unavailable;
 const members=value.members;
 if(members.length!==0&&(members.length!==2||members.map(m=>m?.role).sort().join(',')!=='Q,W'||new Set(members.map(m=>m.deviceId)).size!==2||!uuid.test(value.selectionId)))return unavailable;
 if(value.peers.length!==0&&(value.peers.length!==2||value.peers.map(p=>p?.role).sort().join(',')!=='Q,W'||value.peers.some(p=>!members.some(m=>m.role===p.role&&m.deviceId===p.deviceId))))return unavailable;
 const blocked=value.reconciliationRequired||value.activeRequestId!=null||['unknown','reconciling','unselected'].includes(value.phase);
 return {available:true,stalePublication:now-Date.parse(value.observedUtc)>10000,phase:value.phase,
  ...Object.fromEntries(['canEstablish','canNormalStop','canWhole','canMode'].map(key=>[key,!blocked&&value.readiness[key]===true])),
  peers:value.peers.map(p=>({...p,state:({unknown:'Unknown native status',retained:'Retained native report',current:'Owner-qualified native report',stale:'Stale native report'})[p.nativeFreshness?.state]??'Unknown native status'}))};
}
export function makeAction(current,action,kind,resumeId,mode,requestId,amountBits){
 if(!bilateralModel(current).available||!uuid.test(current.selectionId)||!uuid.test(requestId)||!actions.includes(action))throw Error('Current selected pair required');
 let runChoice=null;
 if(action==='establish'){
  if(kind==='new')runChoice={kind:'new',runId:null};
  else if(kind==='resume'&&runId.test(resumeId))runChoice={kind:'resume',runId:resumeId};
  else throw Error('Choose New run or an available Resume run');
 }
 if(['establish','mode'].includes(action)&&!modes.includes(mode))throw Error('Choose Own, Peer or Blend');
 if(amountBits!==undefined&&(!validAmount(amountBits)||action!=='mode'||mode!=='blend'))throw Error('Numeric strength belongs to a Blend mode edit');
 return {schema:'questms.owner.action.v2',requestId,ownerEpoch:current.ownerEpoch,selectionId:current.selectionId,expectedRevision:current.revision,action,runChoice,mode:['establish','mode'].includes(action)?mode:null,...(amountBits===undefined?{}:{blendAmountBits:amountBits})};
}
export class BilateralController{
 constructor(call,storage){this.call=call;this.storage=storage;this.pending=null;try{const value=JSON.parse(storage.getItem('questms-owner-v2-pending'));if(value?.schema==='questms.owner.action.v2'&&uuid.test(value.requestId)&&uuid.test(value.selectionId)&&actions.includes(value.action)&&value.ownerEpoch&&(!Object.hasOwn(value,'blendAmountBits')||validAmount(value.blendAmountBits)&&value.action==='mode'&&value.mode==='blend'))this.pending=Object.freeze(value);}catch{}}
 async execute(request){
  if(this.pending)throw Error('Original request retained; read its outcome first');
  if(Object.hasOwn(request,'blendAmountBits')){
   if(!validAmount(request.blendAmountBits)||request.action!=='mode'||request.mode!=='blend')throw Error('Invalid numeric Blend request');
   let caps;try{caps=await this.call('/api/hub/v2/capabilities',blendRead);}catch{throw Object.assign(Error('Numeric capability unavailable; no request sent'),{blendCapability:'unknown'});}
   if(!amountSupported(caps,request.ownerEpoch))throw Object.assign(Error('Numeric Blend unsupported; no request sent'),{blendCapability:'unsupported'});
  }
  if(this.pending)throw Error('Original request retained; read its outcome first'); // Capability reads may have overlapped.
  const retained=JSON.parse(JSON.stringify(request));this.storage.setItem('questms-owner-v2-pending',JSON.stringify(retained));this.pending=Object.freeze(retained);
  return this.result(await this.call('/api/hub/v2/actions',{method:'POST',body:JSON.stringify(retained)}));
 }
 result(value){
  const original=this.pending;
  if(!original||value?.schema!=='questms.owner.result.v2'||value.requestId!==original.requestId||value.ownerEpoch!==original.ownerEpoch||value.selectionId!==original.selectionId||value.action!==original.action||!['queued','running','effective','failed','unknown'].includes(value.state))throw Error('Original result identity differs');
  if(value.actualEffectsCompleted!==(value.state==='effective'))throw Error('Effective result lacks actual completion');
  if(value.state==='effective'&&(!Array.isArray(value.peers)||value.peers.length!==2||value.peers.map(p=>p?.role).sort().join(',')!=='Q,W'||value.peers.some(p=>p.state!=='effective'||!p.appProcessGeneration||!p.processEpochId||!p.nativeGeneration||!p.runtimeConfigId||!p.nativeSession||!runId.test(p.runId)||!p.receipt)))throw Error('Effective result lacks both actual peers');
  if(original.blendAmountBits!==undefined&&value.state==='effective'&&value.peers.some(p=>amountObservation(p)?.amountBits!==original.blendAmountBits))throw Error('Numeric completion awaits both qualified strength readbacks');
  if(['effective','failed'].includes(value.state)){this.storage.removeItem('questms-owner-v2-pending');this.pending=null;}
  return value;
 }
 async reconcile(){if(!this.pending)return null;return this.result(await this.call('/api/hub/v2/actions/'+encodeURIComponent(this.pending.requestId),this.pending.blendAmountBits===undefined?undefined:blendRead));}
}
export async function mountBilateralOwner({document,call,storage,token}){
 const $=id=>document.getElementById(id),controller=new BilateralController(call,storage);
 let current=null,inventory=null,runs=null,runInventoryStamp=null,prepared=null,busy=false,refreshingOwner=false,refreshingMirrors=false,sources=[],captures=[],selectionIntent=null;
 const disconnectedObservers=new Set();
 const amountInput=$('blendAmount'),amountEditor=$('blendAmountEditor'),amountHelp=$('blendAmountHelp'),amountRounding=$('blendAmountRounding');
 let amountEdited=false,capabilities=null,capabilityEpoch=null,capabilityRead=null;
 if(amountEditor)amountEditor.hidden=false;if(amountInput)amountInput.value='0.5';
 try{selectionIntent=JSON.parse(storage.getItem('questms-owner-v2-selection'));}catch{}
 $('pairSelection').hidden=false;$('runControls').hidden=false;$('modeShortcuts').hidden=false;$('sessionTitle').textContent='App session';$('refreshSession').textContent='Refresh status';
 $('actionLabel').textContent='App action';$('observationDetailTitle').textContent='View mode';
 $('action').replaceChildren();for(const action of actions){const option=document.createElement('option');option.value=action;option.textContent=labels[action];$('action').append(option);}
 const say=text=>$('actionResult').textContent=text;
 const requestLabel=request=>labels[request.action]+(request.mode?' · '+modeLabels[request.mode]:'')+(request.blendAmountBits===undefined?'':' · strength '+blendAmountValue(request.blendAmountBits));
 const locked=()=>busy||!!controller.pending||!!selectionIntent;
 const numericRequested=()=>!!amountInput&&amountEdited&&$('action').value==='mode'&&$('viewMode').value==='blend';
 const activeBlend=()=>current?.phase==='running'&&bilateralModel(current).available&&current.peers.length===2&&current.peers.every(p=>p.mode==='blend'&&p.appProcessGeneration&&p.processEpochId&&p.nativeGeneration&&p.runtimeConfigId&&p.nativeSession&&p.runId);
 const numericAvailable=()=>!!amountInput&&amountSupported(capabilities,current?.ownerEpoch)&&activeBlend()&&bilateralModel(current).canMode===true;
 function resetAmount(){amountEdited=false;if(amountInput)amountInput.value='0.5';}
 function amountValid(){try{blendAmountBits(amountInput?.value);return true;}catch{return false;}}
 function readCapabilities(){
  if(capabilityRead)return capabilityRead;
  capabilityRead=(async()=>{try{capabilities=await call('/api/hub/v2/capabilities',blendRead);capabilityEpoch=capabilities?.ownerEpoch??current?.ownerEpoch??null;}catch(error){capabilities=null;capabilityEpoch=null;throw error;}})().finally(()=>{capabilityRead=null;controls();});
  return capabilityRead;
 }
 function ready(){const model=bilateralModel(current);return model[({establish:'canEstablish',normalStop:'canNormalStop',whole:'canWhole',mode:'canMode'})[$('action').value]]===true;}
 function choiceReady(){if(numericRequested()&&(!numericAvailable()||!amountValid()))return false;return $('action').value!=='establish'||$('runKind').value==='new'||$('runKind').value==='resume'&&runId.test($('resumeRun').value)&&runs?.selectionId===current?.selectionId&&runs.runs.some(r=>r.runId===$('resumeRun').value&&r.resumable);}
 function controls(){
  if(prepared&&(prepared.ownerEpoch!==current?.ownerEpoch||prepared.selectionId!==current?.selectionId||prepared.expectedRevision!==current?.revision)){
   prepared=null;$('envelope').textContent='No request.';if(!controller.pending)say('Owner status changed · review the request again');
  }
  if(amountInput){
   amountInput.disabled=locked()||!numericAvailable()||$('action').value!=='mode'||$('viewMode').value!=='blend';
   if(amountHelp)amountHelp.textContent=amountSupported(capabilities,current?.ownerEpoch)?'Own brightness shapes Peer input; the requested strength applies to both. The owner checks fresh Blend baselines.':'Numeric strength unavailable for this owner. Mode presets remain available.';
   if(amountRounding)amountRounding.textContent=amountEdited?(amountValid()?'Stored Float32 strength: '+blendAmountValue(blendAmountBits(amountInput.value)):'Enter a strength from 0 to 1.'):'';
  }
  for(const mode of modes){const button=$('mode'+modeLabels[mode]);button.disabled=locked()||bilateralModel(current).canMode!==true;button.ariaPressed=String(prepared?.action==='mode'&&prepared.mode===mode);}
  $('prepare').disabled=locked()||!ready()||!choiceReady();
  $('execute').disabled=locked()||!prepared||!ready()||prepared.blendAmountBits!==undefined&&!numericAvailable();
  if(!prepared)$('execute').textContent='Review request first';
  $('selectPair').disabled=locked()||!inventory||!!current?.activeRequestId||current?.reconciliationRequired===true;
  $('runKind').parentElement.hidden=$('action').value!=='establish';
  $('resumeRun').parentElement.hidden=$('action').value!=='establish'||$('runKind').value!=='resume';
  $('viewMode').parentElement.hidden=!['establish','mode'].includes($('action').value);
  $('evidenceError').textContent=controller.pending?'Original request retained · readback continues without replay':selectionIntent?'Original selection retained · refresh current status to confirm the selected headsets':!ready()&&bilateralModel(current).available?'This action is unavailable in the current app session.':'';
 }
 for(const id of ['action','runKind','resumeRun','viewMode'])$(id).onchange=()=>{resetAmount();prepared=null;$('envelope').textContent='No request.';controls();};
 if(amountInput)amountInput.oninput=()=>{amountEdited=true;prepared=null;$('envelope').textContent='No request.';controls();};
 function render(){
  const model=bilateralModel(current);$('status').textContent=model.available?'Owner connected':'Owner unavailable';
  $('controller').textContent=model.available?'App session: '+model.phase+' · peer video not verified by this report':'App session status unknown';
  $('sessionPanel').replaceChildren();const summary=document.createElement('p');summary.textContent=model.available?(current.members.length===2?'Both headsets selected. ':'Select two headsets. ')+(model.stalePublication?'Status publication is old; live owner guards still apply. ':'')+(current.reconciliationRequired?'An original operation needs reconciliation. ':''):'No authenticated owner report.';$('sessionPanel').append(summary);
  $('observations').replaceChildren();for(const role of ['Q','W']){const peer=model.peers.find(p=>p.role===role),row=document.createElement('tr');for(const text of ['Headset '+role,peer?.state??'Unknown native status',peer?.mode?({own:'Own view',peer:'Peer view',blend:'Blend view'})[peer.mode]+(peer.mode==='blend'?(amountObservation(peer)?' · strength '+blendAmountValue(peer.blendAmount.amountBits)+' (retained retired readback)':' · strength unavailable'):''):'View unknown','App video freshness unverified']){const cell=document.createElement('td');cell.textContent=text;row.append(cell);}$('observations').append(row);$('link'+(role==='Q'?'QW':'WQ')).textContent=(role==='Q'?'Q → W':'W → Q')+': video unverified';}
  $('reports').textContent=JSON.stringify({status:current,runs,retainedRequest:controller.pending,selectionIntent},null,2);controls();
 }
 const sourceFor=role=>{const member=current?.members?.find(m=>m.role===role);return member?sources.find(s=>s.deviceId===member.deviceId):sources.find(s=>s.id===role.toLowerCase());};
 function renderMirrors(){for(const role of ['Q','W']){
  const source=sourceFor(role),capture=captures.find(c=>c.id===source?.id),feed=$('feed'+role);populateCaptureModes(document,$('captureMode'+role),source,capture);
  $('displayState'+role).textContent='Mirror: '+(capture?.state??'not started')+(capture?.captureMode?' · '+captureModeLabel(capture.captureMode):'');
  $('startDisplay'+role).disabled=!source||capture?.retirementPending===true||['requested','connected','unknown','stopping'].includes(capture?.state);
  $('stopDisplay'+role).disabled=!source||capture?.retirementPending!==true||capture.state==='unknown';
  feed.hidden=!(source&&!disconnectedObservers.has(role)&&['requested','connected'].includes(capture?.state));
  if(!feed.hidden){if(feed.dataset.source!==source.id){feed.dataset.source=source.id;feed.src='viewer.html?pane='+role+'&observe=1&source='+encodeURIComponent(source.id)+'#token='+encodeURIComponent(token);}}
  else if(feed.dataset.source){delete feed.dataset.source;feed.src='about:blank';}
 }}
 async function refreshOwner(){if(refreshingOwner)return;refreshingOwner=true;
  try{if(controller.pending){try{const value=await controller.reconcile();say('Original request '+value.state+(value.actualEffectsCompleted?' · both headsets completed':' · completion unconfirmed'));}catch{say('Original outcome unknown · request retained; no retry');}}
   current=await call('/api/hub/v2/status',blendRead);
   if(capabilityEpoch&&capabilityEpoch!==current.ownerEpoch){capabilities=null;capabilityEpoch=null;}else if(capabilities&&!capabilityEpoch)capabilityEpoch=current.ownerEpoch;
   if(!inventory)await devices();
   try{
    const choices=runs?.selectionId===current.selectionId&&runs?.revision===current.revision?runs:await call('/api/hub/v2/runs');runs=choices;const stamp=JSON.stringify([choices.selectionId,choices.runs]);
    if(stamp!==runInventoryStamp){
     const previous=$('resumeRun').value;$('resumeRun').replaceChildren();
     for(const run of choices.runs){const option=document.createElement('option');option.value=run.runId;option.textContent=run.label+(run.resumable?'':' · unavailable');option.disabled=!run.resumable;$('resumeRun').append(option);}
     if([...$('resumeRun').options].some(o=>o.value===previous))$('resumeRun').value=previous;
     runInventoryStamp=stamp;
    }
   }catch{runs=null;}
   render();
   if(!capabilities&&!capabilityRead){try{await readCapabilities();}catch{}controls();}
  }catch{current=null;prepared=null;render();}
  finally{refreshingOwner=false;}
 }
 async function refreshMirrors(){if(refreshingMirrors)return;refreshingMirrors=true;
  try{[sources,captures]=await Promise.all([call('/api/sources'),call('/api/captures')]);renderMirrors();}catch{sources=[];for(const role of ['Q','W']){$('displayState'+role).textContent='Mirror status unavailable';$('startDisplay'+role).disabled=true;$('stopDisplay'+role).disabled=true;}}
  finally{refreshingMirrors=false;}
 }
 async function refresh(){await Promise.all([refreshOwner(),refreshMirrors()]);}
 async function devices(){try{inventory=await call('/api/hub/v2/devices');for(const role of ['Q','W']){const select=$('device'+role),previous=select.value;select.replaceChildren();for(const device of inventory.devices){const option=document.createElement('option');option.value=device.deviceId;option.textContent=device.label+(device.ready?'':' · unavailable');option.disabled=!device.ready;select.append(option);}if([...select.options].some(o=>o.value===previous))select.value=previous;}if($('deviceW').value===$('deviceQ').value&&$('deviceW').options.length>1)$('deviceW').value=$('deviceW').options[1].value;}catch{inventory=null;}controls();}
 $('refreshDevices').onclick=devices;
 $('selectPair').onclick=async()=>{
  if(busy||controller.pending||selectionIntent||!inventory)return;const q=$('deviceQ').value,w=$('deviceW').value;if(!q||q===w){say('Select two distinct headsets');return;}
  busy=true;controls();try{
   const request={schema:'questms.owner.selection.v2',requestId:crypto.randomUUID(),ownerEpoch:inventory.ownerEpoch,expectedRevision:current?.ownerEpoch===inventory.ownerEpoch?current.revision:inventory.revision,members:[{role:'Q',deviceId:q},{role:'W',deviceId:w}]};
   storage.setItem('questms-owner-v2-selection',JSON.stringify(request));selectionIntent=request;
   await call('/api/hub/v2/selection',{method:'POST',body:JSON.stringify(request)});storage.removeItem('questms-owner-v2-selection');selectionIntent=null;prepared=null;say('Headsets selected · no media started');
  }
  catch{say('Selection outcome unknown · original selection retained; refresh current status');}
  finally{busy=false;await refresh();}
 };
 function prepareRequest(){
  if(locked()||!ready()||!choiceReady())return;
  try{prepared=makeAction(current,$('action').value,$('runKind').value,$('resumeRun').value,$('viewMode').value,crypto.randomUUID(),numericRequested()?blendAmountBits(amountInput.value):undefined);$('envelope').textContent=JSON.stringify(prepared,null,2);$('execute').textContent=requestLabel(prepared);say(requestLabel(prepared)+' reviewed · owner checks current headsets before execution');}catch(error){say(error.message);}controls();
 }
 $('actionForm').onsubmit=event=>{event.preventDefault();prepareRequest();};
 for(const mode of modes)$('mode'+modeLabels[mode]).onclick=()=>{
  if(locked()||bilateralModel(current).canMode!==true)return;
  resetAmount();$('action').value='mode';$('viewMode').value=mode;prepareRequest();
 };
 $('execute').onclick=async()=>{controls();if($('execute').disabled)return;const request=prepared;busy=true;controls();try{const value=await controller.execute(request);say(requestLabel(request)+' · '+value.state+(value.actualEffectsCompleted?' · both headsets completed':' · completion unconfirmed'));prepared=null;}catch(error){if(error.blendCapability){capabilities=error.blendCapability==='unsupported'?{ownerEpoch:current?.ownerEpoch,features:[]}:null;capabilityEpoch=current?.ownerEpoch;prepared=null;say('Request not sent · '+error.message);}else say(requestLabel(request)+' outcome unknown · original request retained; no retry');}finally{busy=false;await refresh();}};
 for(const role of ['Q','W']){
  for(const action of ['start','stop']){const button=$(action+'Display'+role);button.hidden=false;button.textContent=action==='start'?'Start mirror':'Stop mirror';button.onclick=async()=>{const source=sourceFor(role);if(!source)return;button.disabled=true;try{const body={requestId:crypto.randomUUID()};if(action==='start')body.captureMode=$('captureMode'+role).value||'raw-two-lens';await call('/api/captures/'+encodeURIComponent(source.id)+'/'+action,{method:'POST',body:JSON.stringify(body)});}catch{$('displayState'+role).textContent='Mirror outcome unknown · inspect existing capture';}await refresh();};}
  $('disconnectDisplay'+role).hidden=false;$('disconnectDisplay'+role).onclick=()=>{
   if(disconnectedObservers.has(role)){disconnectedObservers.delete(role);$('disconnectDisplay'+role).textContent='Disconnect viewer';renderMirrors();}
   else{disconnectedObservers.add(role);const feed=$('feed'+role);delete feed.dataset.source;feed.src='about:blank';feed.hidden=true;$('disconnectDisplay'+role).textContent='Connect viewer';$('displayState'+role).textContent='Viewer disconnected · mirror producer unchanged';}
  };
 }
 $('refreshSession').onclick=async()=>{
  await refresh();
  if(selectionIntent&&current?.ownerEpoch===selectionIntent.ownerEpoch&&current.activeRequestId==null&&!current.reconciliationRequired&&current.members?.length===2&&current.members.every(m=>selectionIntent.members?.some(s=>s.role===m.role&&s.deviceId===m.deviceId))){
   storage.removeItem('questms-owner-v2-selection');selectionIntent=null;say('Selected headsets confirmed by current owner status');controls();
  }
 };
 $('stopMonitor').onclick=async()=>{try{await call('/operator-stop',{method:'POST'});$('status').textContent='Monitor stop requested · mirror retirement pending';}catch{$('status').textContent='Monitor stop outcome unknown';}};
 // This read is diagnostic; a startup refusal must not discard the normal
 // authenticated status loop or the disposal handle. Mutations stay explicit.
 const firstCapabilities=readCapabilities().catch(error=>{say('Owner capability check unavailable · '+error.message);controls();});
 await devices();const timer=setInterval(refresh,1000);await refresh();await firstCapabilities;return {refresh,dispose(){clearInterval(timer);}};
}
