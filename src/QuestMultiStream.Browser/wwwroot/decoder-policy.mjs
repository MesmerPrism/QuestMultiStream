// This changes browser decoding only, never the native capture/socket epoch.
// Soft backlog keeps H.264 delta dependencies intact until a genuine keyframe.
// The hard bound prevents submitting beyond 128 queued chunks even mid-GOP.
const SOFT_QUEUE_LIMIT=8,HARD_QUEUE_LIMIT=128;
export class DecoderPolicy {
  constructor(){this.waitingKey=true;this.paused=false;this.lastTimestamp=null;this.packets=0;this.keys=0;this.configurations=0;this.lastReason='initial-keyframe';}
  configure(){this.configurations=Math.min(Number.MAX_SAFE_INTEGER,this.configurations+1);this.waitingKey=true;this.lastTimestamp=null;this.lastReason='codec-configuration';}
  snapshot(){return {packets:this.packets,keys:this.keys,configurations:this.configurations,lastReason:this.lastReason,waitingKey:this.waitingKey,lastTimestamp:this.lastTimestamp};}
  select(packet,queue,visible){
    let reset=false;
    this.packets=Math.min(Number.MAX_SAFE_INTEGER,this.packets+1);if(packet.key)this.keys=Math.min(Number.MAX_SAFE_INTEGER,this.keys+1);
    if(!visible){this.lastReason='viewer-hidden';reset=!this.paused;this.paused=true;this.waitingKey=true;return {decode:false,reset,prepend:false};}
    if(this.paused){this.paused=false;this.waitingKey=true;reset=true;}
    if((packet.key&&queue>SOFT_QUEUE_LIMIT)||queue>=HARD_QUEUE_LIMIT){this.waitingKey=true;reset=true;this.lastReason=queue>=HARD_QUEUE_LIMIT?'decoder-hard-queue-pressure':'decoder-queue-pressure';}
    if(this.lastTimestamp!==null&&packet.timestamp<=this.lastTimestamp){this.lastReason='nonadvancing-packet-timestamp';return {decode:false,reset,prepend:false};}
    if(this.waitingKey&&!packet.key){if(queue<HARD_QUEUE_LIMIT)this.lastReason='keyframe-required';return {decode:false,reset,prepend:false};}
    const prepend=this.waitingKey;this.waitingKey=false;this.lastTimestamp=packet.timestamp;
    this.lastReason='decode-submitted';return {decode:true,reset,prepend};
  }
}
