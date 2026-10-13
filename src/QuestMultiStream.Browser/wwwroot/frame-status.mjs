// Only an actual current decoded frame on an open transport restores the live label.
export function decodedStatus(mode,error,open,width,height){
  if(mode!=='Live host'||error||!open)return null;
  return `${width} × ${height} · H.264 · Live host`;
}
