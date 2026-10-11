// This changes browser decoding only, never the native capture/socket epoch.
export class DecoderPolicy {
  constructor(){this.waitingKey=true;this.paused=false;this.lastTimestamp=null;}
  select(packet,queue,visible){
    let reset=false;
    if(!visible){reset=!this.paused;this.paused=true;this.waitingKey=true;return {decode:false,reset,prepend:false};}
    if(this.paused){this.paused=false;this.waitingKey=true;reset=true;}
    if(queue>8){this.waitingKey=true;reset=true;}
    if(this.lastTimestamp!==null&&packet.timestamp<=this.lastTimestamp)return {decode:false,reset,prepend:false};
    if(this.waitingKey&&!packet.key)return {decode:false,reset,prepend:false};
    const prepend=this.waitingKey;this.waitingKey=false;this.lastTimestamp=packet.timestamp;
    return {decode:true,reset,prepend};
  }
}
