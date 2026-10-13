// Crop constants and black fallback from MIT QuestMultiStream.App/StereoCompositePreviewRenderer.
export function eyeRects(w,h){
  const half=Math.floor(w/2),top=Math.round(h*.08),bottom=Math.round(h*.98),width=Math.round(half*.78);
  return [{x:Math.min(Math.round(half*.16),half-width),y:top,w:width,h:bottom-top},
    {x:half+Math.min(Math.round(half*.04),half-width),y:top,w:width,h:bottom-top}];
}
export function cropRect(w,h,text){
  if(!/^\d+:\d+:\d+:\d+$/.test(text))throw Error('Crop must be width:height:x:y');
  const [width,height,x,y]=text.split(':').map(Number);
  if(width<1||height<1||x+width>w||y+height>h)throw Error('Crop lies outside the decoded frame');
  return {x,y,w:width,h:height};
}
export function blendPixel(left,right,t){
  if(left[0]+left[1]+left[2]<36&&right[0]+right[1]+right[2]>=36)return [...right.slice(0,3),255];
  if(right[0]+right[1]+right[2]<36&&left[0]+left[1]+left[2]>=36)return [...left.slice(0,3),255];
  const s=t*t*(3-2*t);return [0,1,2].map(i=>Math.round(left[i]*(1-s)+right[i]*s)).concat(255);
}
export function packet(data){
  const v=new DataView(data);if(v.byteLength<13)throw Error('Truncated video packet');
  const stamp=v.getBigUint64(0),length=v.getUint32(8);
  if(length!==v.byteLength-12||length>16*1024*1024)throw Error('Bad video packet length');
  const pts=stamp&((1n<<62n)-1n);if(pts>BigInt(Number.MAX_SAFE_INTEGER))throw Error('Unsafe timestamp');
  return {config:!!(stamp&(1n<<63n)),key:!!(stamp&(1n<<62n)),timestamp:Number(pts),data:new Uint8Array(data,12)};
}
export function avcCodec(data){
  for(let i=0;i<data.length-7;i++){
    let n=0;if(data[i]===0&&data[i+1]===0&&data[i+2]===1)n=i+3;
    else if(data[i]===0&&data[i+1]===0&&data[i+2]===0&&data[i+3]===1)n=i+4;
    if(n&&(data[n]&31)===7)return 'avc1.'+Array.from(data.slice(n+1,n+4),b=>b.toString(16).padStart(2,'0')).join('');
  }throw Error('No Annex-B SPS configuration');
}
