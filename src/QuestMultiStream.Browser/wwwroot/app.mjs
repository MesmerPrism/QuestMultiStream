import {eyeRects,cropRect,blendPixel,packet,avcCodec} from './view.mjs';
import {decodedStatus,MirrorTiming} from './frame-status.mjs';
import {DecoderPolicy} from './decoder-policy.mjs';
import {captureModeLabel,populateCaptureModes,frameLayout,presentationView} from './capture-mode.mjs';
const $=id=>document.getElementById(id),canvas=$('screen'),ctx=canvas.getContext('2d'),scratch=document.createElement('canvas'),sctx=scratch.getContext('2d'),video=$('local');
const pane=new URLSearchParams(location.search).get('pane');
const viewerSource=new URLSearchParams(location.search).get('source');
const observeOnly=new URLSearchParams(location.search).get('observe')==='1';
if(pane!==null)document.body.classList.add('embedded');
if(pane!==null&&observeOnly){document.body.classList.add('observer-only');for(const id of ['connect','stop','stopCapture','stopHost'])$(id).hidden=true;}
const token=new URLSearchParams(location.hash.slice(1)).get('token')??sessionStorage.getItem('questms-local-token')??'';
if(token)sessionStorage.setItem('questms-local-token',token);history.replaceState(null,'',location.pathname);
let socket=null,decoder=null,shared=null,blob=null,generation=0,frames=0,frameTime=0,mode='',streamError='',config=null,waitingKey=true,policy=null,decoderEpoch=0,packetTime=0,keyTime=0;
let sources=[],layout='stereo-sbs',actualCaptureMode=null,refreshingMode=false,mirrorTiming=null,timingReportAt=-Infinity;
function applyLayout(meta){
  layout=frameLayout(meta);actualCaptureMode=meta?.captureMode??(layout==='mono'?'square-left':'raw-two-lens');
  $('view').value=presentationView($('view').value,layout);
  for(const option of $('view').options)if(['left','right','composite'].includes(option.value))option.disabled=layout==='mono';
  $('captureModeNote').textContent=`Current capture: ${captureModeLabel(actualCaptureMode)}. `+(observeOnly?'This pane only observes the capture.':'Stop the capture before choosing another mode, then start it explicitly.');
}
async function refreshCaptureMode(){
  const source=sources.find(s=>s.id===$('source').value);if(!source||refreshingMode)return;refreshingMode=true;
  try{const response=await fetch('/api/captures?token='+encodeURIComponent(token),{cache:'no-store'});if(!response.ok)throw Error('Capture state unavailable');const values=await response.json();if(!Array.isArray(values))throw Error('Capture state unavailable');if($('source').value!==source.id)return;const capture=values.find(c=>c.id===source.id);populateCaptureModes(document,$('captureMode'),source,capture);if(observeOnly)$('captureMode').disabled=true;
    if(capture?.captureMode&&!socket)$('captureModeNote').textContent=`Last started capture: ${captureModeLabel(capture.captureMode)}. Stop and retirement are required before a mode change.`;
  }catch{$('captureMode').disabled=true;}finally{refreshingMode=false;}
}
$('source').onchange=refreshCaptureMode;
function recoveryMetrics(){const p=policy?.snapshot();if(!p)return 'No received video packets';const age=t=>t?((performance.now()-t)/1000).toFixed(1)+' s':'not observed';return `${p.packets} received video packets · ${p.keys} keyframes · packet age ${age(packetTime)} · keyframe age ${age(keyTime)} · ${p.lastReason} · decoded freshness unknown`;}
function timingMetrics(){
 if(!mirrorTiming)return '';const value=mirrorTiming.snapshot(),ms=v=>v===null?'unobserved':Math.round(v)+' ms';
 const now=performance.now();if(now-timingReportAt>=250){$('timingReport').textContent=JSON.stringify({...value,observer:socket?.readyState===WebSocket.OPEN?'connected':'disconnected'},null,2);timingReportAt=now;}
 return ` · arrival max ${ms(value.arrival.maxGapMs)} · draw max ${ms(value.canvasDraw.maxGapMs)} · ≥1 s gaps ${value.arrival.gapsAtLeast1s}/${value.canvasDraw.gapsAtLeast1s}`;
}
function clearImage(){frameTime=0;ctx.clearRect(0,0,canvas.width,canvas.height);$('empty').hidden=false;$('empty').textContent='Capture connected · waiting for a current keyframe. No decoded image is available.';$('metrics').textContent=recoveryMetrics();}
function resetDecoder(current){
  const epoch=++decoderEpoch;if(decoder&&decoder.state!=='closed')decoder.close();decoder=null;clearImage();
  if(!config)return;
  decoder=new VideoDecoder({output(frame){try{if(current===generation&&epoch===decoderEpoch&&!document.hidden)draw(frame,frame.displayWidth,frame.displayHeight);}catch(e){status(e.message);}finally{frame.close();}},error(e){if(current===generation&&epoch===decoderEpoch){streamError='Decoder failed: '+e.message;status(streamError);socket?.close();}}});
  decoder.configure({codec:avcCodec(config),optimizeForLatency:true});
}
document.addEventListener('visibilitychange',()=>{mirrorTiming?.visibility(!document.hidden);if(socket&&policy){policy.paused=true;policy.waitingKey=true;resetDecoder(generation);status(document.hidden?'Viewer hidden · decoding paused':'Viewer visible · waiting for next keyframe on the same capture');}});
function status(text){$('status').textContent=text;}
function draw(image,w,h){
  let rect={x:0,y:0,w,h};const view=presentationView($('view').value,layout);
  if(view==='left'||view==='right')rect=eyeRects(w,h)[view==='left'?0:1];
  if(view==='crop')rect=cropRect(w,h,$('crop').value);
  canvas.width=rect.w;canvas.height=rect.h;ctx.drawImage(image,rect.x,rect.y,rect.w,rect.h,0,0,rect.w,rect.h);
  if(view==='composite'){
    const [a,b]=eyeRects(w,h);scratch.width=w;scratch.height=h;sctx.drawImage(image,0,0,w,h);
    const left=sctx.getImageData(a.x,a.y,a.w,a.h),right=sctx.getImageData(b.x,b.y,b.w,b.h);canvas.width=a.w;canvas.height=a.h;
    for(let y=0;y<a.h;y++)for(let x=0;x<a.w;x++){
      const i=(y*a.w+x)*4,p=blendPixel(left.data.subarray(i,i+4),right.data.subarray(i,i+4),x/Math.max(1,a.w-1));left.data.set(p,i);
    }ctx.putImageData(left,0,0);
  }
  canvas.style.aspectRatio=`${canvas.width} / ${canvas.height}`;
  const resumed=decodedStatus(mode,streamError,socket?.readyState===WebSocket.OPEN,w,h);if(resumed)status(resumed);else if(mode==='Mirror observer'&&!streamError&&socket?.readyState===WebSocket.OPEN)status('Headset mirror · decoded frames');
  $('empty').hidden=true;frames++;frameTime=performance.now();mirrorTiming?.draw();$('metrics').textContent=`${w} × ${h} source · ${frames} decoded frames · ${mode}`+timingMetrics();
}
function stop(){if(mirrorTiming)$('timingReport').textContent=JSON.stringify({...mirrorTiming.snapshot(),observer:'disconnected'},null,2);mirrorTiming=null;generation++;socket?.close();socket=null;if(decoder&&decoder.state!=='closed')decoder.close();decoder=null;shared?.getTracks().forEach(t=>t.stop());shared=null;video.pause();video.srcObject=null;video.removeAttribute('src');if(blob)URL.revokeObjectURL(blob);blob=null;mode='';streamError='';config=null;waitingKey=true;frames=0;frameTime=0;packetTime=0;keyTime=0;policy=null;layout='stereo-sbs';actualCaptureMode=null;for(const option of $('view').options)option.disabled=false;$('metrics').textContent='No decoded frames';ctx.clearRect(0,0,canvas.width,canvas.height);$('empty').hidden=false;$('empty').textContent='Choose a configured stream or open a local video.';status('Disconnected');}
$('stop').onclick=stop;
$('stopCapture').onclick=async()=>{const id=$('source').value;if(!id)return;try{const response=await fetch(`/api/captures/${encodeURIComponent(id)}/stop?token=${encodeURIComponent(token)}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID()})});if(!response.ok)throw new Error('Capture stop denied: '+response.status);status('Capture stop requested · cleanup pending');}catch(error){status(error.message);}};
$('stopHost').onclick=async()=>{try{const r=await fetch('/operator-stop?token='+encodeURIComponent(token),{method:'POST'});if(!r.ok)throw new Error('Monitor stop denied: '+r.status);status('Monitor stop requested · actual capture cleanup pending');}catch(e){status(e.message);}};
$('connect').onclick=async()=>{
  const selectedMode=$('captureMode').value||'raw-two-lens';stop();const id=$('source').value;if(!id)return;const current=generation;mode=pane===null?'Live host':'Mirror observer';
  if(!window.VideoDecoder){status('WebCodecs requires a supported browser and secure context');return;}
  policy=new DecoderPolicy();mirrorTiming=new MirrorTiming();timingReportAt=-Infinity;mirrorTiming.visibility(!document.hidden);
  if(!observeOnly){$('captureMode').disabled=true;const started=await fetch(`/api/captures/${encodeURIComponent(id)}/start?token=${encodeURIComponent(token)}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID(),captureMode:selectedMode})});if(!started.ok){status('Capture request denied: '+started.status);await refreshCaptureMode();return;}const state=await started.json();if(current!==generation)return;if(state.state==='stopping'||state.state==='unknown'){status('Capture is retiring or unresolved; wait for its actual cleanup');return;}}
  socket=new WebSocket(`ws://${location.host}/observe/${encodeURIComponent(id)}?token=${encodeURIComponent(token)}`);socket.binaryType='arraybuffer';
  socket.onopen=()=>status('Capture requested · waiting for video');
  socket.onmessage=async event=>{
    if(current!==generation)return;
    try{
      if(typeof event.data==='string'){const meta=JSON.parse(event.data);applyLayout(meta);status(`${meta.Width} × ${meta.Height} · H.264 · ${captureModeLabel(actualCaptureMode)}`);return;}
      const p=packet(event.data);
      mirrorTiming.packet(p);
      packetTime=performance.now();if(!p.config&&p.key)keyTime=packetTime;
      if(p.config){config=p.data.slice();policy.configure();resetDecoder(current);return;}
      if(!config)return;
      const decision=policy.select(p,decoder?.decodeQueueSize??0,!document.hidden);
      if(decision.reset)resetDecoder(current);
      if(!decision.decode){if(!document.hidden){status('Waiting for next keyframe · same native capture · freshness unknown');$('metrics').textContent=recoveryMetrics();}return;}
      let data=p.data;if(decision.prepend){data=new Uint8Array(config.length+p.data.length);data.set(config);data.set(p.data,config.length);}
      decoder.decode(new EncodedVideoChunk({type:p.key?'key':'delta',timestamp:p.timestamp,data}));
    }catch(e){streamError='Stream failed: '+e.message;status(streamError);socket?.close();}
  };
  socket.onclose=()=>{if(current===generation)status(streamError||'Host stream ended · no automatic reconnect');};socket.onerror=()=>{streamError='Host connection failed';status(streamError);};
};
function localLoop(current){if(current!==generation||video.paused)return;if(video.readyState>=2){try{draw(video,video.videoWidth,video.videoHeight);}catch(e){status(e.message);}}video.requestVideoFrameCallback?.(()=>localLoop(current));}
$('file').onchange=async()=>{const file=$('file').files[0];if(!file)return;stop();const current=generation;blob=URL.createObjectURL(file);video.src=blob;video.loop=true;mode='Local file';try{await video.play();if(current===generation){status('Local file · no device connection');localLoop(current);}}catch(e){status(e.message);}};
$('share').onclick=async()=>{stop();const current=generation;try{const stream=await navigator.mediaDevices.getDisplayMedia({video:true,audio:false});if(current!==generation){stream.getTracks().forEach(t=>t.stop());return;}shared=stream;video.srcObject=stream;mode='Manual window share';stream.getVideoTracks()[0].onended=()=>{if(current===generation)stop();};await video.play();if(current===generation){status('Manual browser-selected window');localLoop(current);}}catch(e){status('Window share: '+e.message);}};
$('fullscreen').onclick=()=>canvas.requestFullscreen();
try{const response=await fetch('/api/sources?token='+encodeURIComponent(token));if(response.ok){sources=await response.json();const choices=pane===null?sources:sources.filter(s=>s.id===viewerSource);for(const s of choices){const o=document.createElement('option');o.value=s.id;o.textContent=s.label+' · Display '+s.displayId;$('source').append(o);}if(choices.length){$('source').options[0].textContent='Choose host source';if(pane!==null)$('source').value=choices[0].id;$('hostNote').textContent='One mirror per headset. Reconnect does not restart it.';}}else{status('Host authentication denied: '+response.status);}}catch(e){status('Host unavailable');}
await refreshCaptureMode();
$('connect').disabled=$('source').options.length===1;
if(observeOnly&&!$('connect').disabled)await $('connect').onclick();
setInterval(()=>{if(mirrorTiming)timingMetrics();if(mode&&frameTime&&socket?.readyState===WebSocket.OPEN&&performance.now()-frameTime>=1000)status((streamError?streamError+' · ':'')+'No new decoded frame · age '+((performance.now()-frameTime)/1000).toFixed(1)+' s · freshness unknown');},250);

try{const f=await fetch('/api/fixture?token='+encodeURIComponent(token));if(f.ok&& (await f.json()).available)$('fixture').hidden=false;}catch{}
$('fixture').onclick=async()=>{stop();const current=generation;video.src='/fixture?token='+encodeURIComponent(token);video.loop=true;mode='Synthetic fixture';try{await video.play();if(current===generation){status('Synthetic fixture · no headset capture');localLoop(current);}}catch(e){status(e.message);}};
setInterval(()=>{if(mode==='Live host'&&!streamError&&socket?.readyState===WebSocket.OPEN&&policy?.waitingKey&&!document.hidden)$('metrics').textContent=recoveryMetrics();},1000);
setInterval(refreshCaptureMode,1000);
