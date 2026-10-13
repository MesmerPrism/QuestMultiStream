// This changes browser decoding only, never the native capture/socket epoch.
export class DecoderPolicy {
  constructor(){this.waitingKey=true;this.paused=false;this.lastTimestamp=null;this.packets=0;this.keys=0;this.configurations=0;this.lastReason='initial-keyframe';}
  configure(){this.configurations=Math.min(Number.MAX_SAFE_INTEGER,this.configurations+1);this.waitingKey=true;this.lastTimestamp=null;this.lastReason='codec-configuration';}
  snapshot(){return {packets:this.packets,keys:this.keys,configurations:this.configurations,lastReason:this.lastReason,waitingKey:this.waitingKey,lastTimestamp:this.lastTimestamp};}
  select(packet,queue,visible){
    let reset=false;
    this.packets=Math.min(Number.MAX_SAFE_INTEGER,this.packets+1);if(packet.key)this.keys=Math.min(Number.MAX_SAFE_INTEGER,this.keys+1);
    if(!visible){this.lastReason='viewer-hidden';reset=!this.paused;this.paused=true;this.waitingKey=true;return {decode:false,reset,prepend:false};}
    if(this.paused){this.paused=false;this.waitingKey=true;reset=true;}
    if(queue>8){this.waitingKey=true;reset=true;this.lastReason='decoder-queue-pressure';}
    if(this.lastTimestamp!==null&&packet.timestamp<=this.lastTimestamp){this.lastReason='nonadvancing-packet-timestamp';return {decode:false,reset,prepend:false};}
    if(this.waitingKey&&!packet.key){if(queue<=8)this.lastReason='keyframe-required';return {decode:false,reset,prepend:false};}
    const prepend=this.waitingKey;this.waitingKey=false;this.lastTimestamp=packet.timestamp;
    this.lastReason='decode-submitted';return {decode:true,reset,prepend};
  }
}
