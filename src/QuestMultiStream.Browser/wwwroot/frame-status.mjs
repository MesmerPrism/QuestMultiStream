// Only an actual current decoded frame on an open transport restores the live label.
export function decodedStatus(mode,error,open,width,height){
  if(mode!=='Live host'||error||!open)return null;
  return `${width} × ${height} · H.264 · Live host`;
}

// Arrival and canvas submission share performance.now(). Producer PTS is only
// compared with another PTS from the same configuration; it has no PC mapping.
export class MirrorTiming{
 constructor(now=()=>performance.now()){
  this.now=now;this.visible=true;this.visibleSegments=1;this.videoPackets=0;this.canvasDraws=0;this.hiddenPackets=0;this.configPackets=0;
  this.arrival={last:null,maxGapMs:null,gapsAtLeast1s:0};this.canvasDraw={last:null,maxGapMs:null,gapsAtLeast1s:0};
  this.sourcePts={last:null,maxGapMs:null,gapsAtLeast1s:0,nonadvancing:0};
 }
 sample(series,value){
  if(series.last!==null){const delta=value-series.last;if(delta>=0){series.maxGapMs=Math.max(series.maxGapMs??0,delta);if(delta>=1000)series.gapsAtLeast1s++;}}
  series.last=value;
 }
 visibility(visible){
  if(this.visible===visible)return;this.visible=visible;if(visible)this.visibleSegments++;
  this.arrival.last=this.canvasDraw.last=this.sourcePts.last=null;
 }
 packet(packet){
  if(packet.config){this.configPackets++;this.sourcePts.last=null;return;}
  if(!this.visible){this.hiddenPackets++;return;}
  this.videoPackets++;this.sample(this.arrival,this.now());
  const ptsMs=packet.timestamp/1000;
  if(this.sourcePts.last!==null&&ptsMs<=this.sourcePts.last)this.sourcePts.nonadvancing++;
  else this.sample(this.sourcePts,ptsMs);
 }
 draw(){if(this.visible){this.canvasDraws++;this.sample(this.canvasDraw,this.now());}}
 snapshot(){
  const now=this.now(),clock='browser-performance-now-ms';
  const browser=series=>({clock,maxGapMs:series.maxGapMs,gapsAtLeast1s:series.gapsAtLeast1s,ageMs:series.last===null?null:now-series.last});
  return {scope:'screen-mirror-observer',videoPackets:this.videoPackets,canvasDraws:this.canvasDraws,hiddenPackets:this.hiddenPackets,configPackets:this.configPackets,visibleSegments:this.visibleSegments,
   arrival:browser(this.arrival),canvasDraw:browser(this.canvasDraw),sourcePts:{clock:'producer-pts-us',maxGapMs:this.sourcePts.maxGapMs,gapsAtLeast1s:this.sourcePts.gapsAtLeast1s,nonadvancing:this.sourcePts.nonadvancing}};
 }
}
