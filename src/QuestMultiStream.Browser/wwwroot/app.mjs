import {eyeRects,cropRect,blendPixel,packet,avcCodec} from './view.mjs';
import {decodedStatus} from './frame-status.mjs';
import {DecoderPolicy} from './decoder-policy.mjs';
const $=id=>document.getElementById(id),canvas=$('screen'),ctx=canvas.getContext('2d'),scratch=document.createElement('canvas'),sctx=scratch.getContext('2d'),video=$('local');
const token=new URLSearchParams(location.hash.slice(1)).get('token')??sessionStorage.getItem('questms-local-token')??'';
if(token)sessionStorage.setItem('questms-local-token',token);history.replaceState(null,'',location.pathname);
let socket=null,decoder=null,shared=null,blob=null,generation=0,frames=0,frameTime=0,mode='',streamError='',config=null,waitingKey=true,policy=null,decoderEpoch=0,packetTime=0,keyTime=0;
function recoveryMetrics(){const p=policy?.snapshot();if(!p)return 'No received video packets';const age=t=>t?((performance.now()-t)/1000).toFixed(1)+' s':'not observed';return `${p.packets} received video packets · ${p.keys} keyframes · packet age ${age(packetTime)} · keyframe age ${age(keyTime)} · ${p.lastReason} · decoded freshness unknown`;}
function clearImage(){frameTime=0;ctx.clearRect(0,0,canvas.width,canvas.height);$('empty').hidden=false;$('empty').textContent='Capture connected · waiting for a current keyframe. No decoded image is available.';$('metrics').textContent=recoveryMetrics();}
function resetDecoder(current){
  const epoch=++decoderEpoch;if(decoder&&decoder.state!=='closed')decoder.close();decoder=null;clearImage();
  if(!config)return;
  decoder=new VideoDecoder({output(frame){try{if(current===generation&&epoch===decoderEpoch&&!document.hidden)draw(frame,frame.displayWidth,frame.displayHeight);}catch(e){status(e.message);}finally{frame.close();}},error(e){if(current===generation&&epoch===decoderEpoch){streamError='Decoder failed: '+e.message;status(streamError);socket?.close();}}});
  decoder.configure({codec:avcCodec(config),optimizeForLatency:true});
}
document.addEventListener('visibilitychange',()=>{if(socket&&policy){policy.paused=true;policy.waitingKey=true;resetDecoder(generation);status(document.hidden?'Viewer hidden · decoding paused':'Viewer visible · waiting for next keyframe on the same capture');}});
function status(text){$('status').textContent=text;}
function draw(image,w,h){
  let rect={x:0,y:0,w,h};const view=$('view').value;
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
  const resumed=decodedStatus(mode,streamError,socket?.readyState===WebSocket.OPEN,w,h);if(resumed)status(resumed);
  $('empty').hidden=true;frames++;frameTime=performance.now();$('metrics').textContent=`${w} × ${h} source · ${frames} decoded frames · ${mode}`;
}
function stop(){generation++;socket?.close();socket=null;if(decoder&&decoder.state!=='closed')decoder.close();decoder=null;shared?.getTracks().forEach(t=>t.stop());shared=null;video.pause();video.srcObject=null;video.removeAttribute('src');if(blob)URL.revokeObjectURL(blob);blob=null;mode='';streamError='';config=null;waitingKey=true;frames=0;frameTime=0;packetTime=0;keyTime=0;policy=null;$('metrics').textContent='No decoded frames';ctx.clearRect(0,0,canvas.width,canvas.height);$('empty').hidden=false;$('empty').textContent='Choose a configured stream or open a local video.';status('Disconnected');}
$('stop').onclick=stop;
$('stopHost').onclick=async()=>{try{const r=await fetch('/operator-stop?token='+encodeURIComponent(token),{method:'POST'});if(!r.ok)throw new Error('Monitor stop denied: '+r.status);status('Monitor stop requested · actual capture cleanup pending');}catch(e){status(e.message);}};
$('connect').onclick=async()=>{
  stop();const id=$('source').value;if(!id)return;const current=generation;mode='Live host';
  if(!window.VideoDecoder){status('WebCodecs requires a supported browser and secure context');return;}
  policy=new DecoderPolicy();
  socket=new WebSocket(`ws://${location.host}/stream/${encodeURIComponent(id)}?token=${encodeURIComponent(token)}`);socket.binaryType='arraybuffer';
  socket.onopen=()=>status('Capture requested · waiting for video');
  socket.onmessage=async event=>{
    if(current!==generation)return;
    try{
      if(typeof event.data==='string'){const meta=JSON.parse(event.data);status(`${meta.Width} × ${meta.Height} · H.264`);return;}
      const p=packet(event.data);
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
try{const response=await fetch('/api/sources?token='+encodeURIComponent(token));if(response.ok){const sources=await response.json();for(const s of sources){const o=document.createElement('option');o.value=s.id;o.textContent=s.label+' · Display '+s.displayId;$('source').append(o);}if(sources.length){$('source').options[0].textContent='Choose host source';$('hostNote').textContent='Each connection starts one explicitly configured capture. No automatic retry.';}}}catch(e){status('Host unavailable');}
$('connect').disabled=$('source').options.length===1;
setInterval(()=>{if(mode&&frameTime&&performance.now()-frameTime>3000)status((streamError?streamError+' · ':'')+'No new decoded frame · age '+((performance.now()-frameTime)/1000).toFixed(1)+' s · freshness unknown');},1000);

try{const f=await fetch('/api/fixture?token='+encodeURIComponent(token));if(f.ok&& (await f.json()).available)$('fixture').hidden=false;}catch{}
$('fixture').onclick=async()=>{stop();const current=generation;video.src='/fixture?token='+encodeURIComponent(token);video.loop=true;mode='Synthetic fixture';try{await video.play();if(current===generation){status('Synthetic fixture · no headset capture');localLoop(current);}}catch(e){status(e.message);}};
setInterval(()=>{if(mode==='Live host'&&!streamError&&socket?.readyState===WebSocket.OPEN&&policy?.waitingKey&&!document.hidden)$('metrics').textContent=recoveryMetrics();},1000);
