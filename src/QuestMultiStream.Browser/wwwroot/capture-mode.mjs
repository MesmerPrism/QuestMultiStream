const labels={'raw-two-lens':'Raw two-lens mirror','square-left':'Native square · left eye'};
export function captureModes(source){
  const modes=Array.isArray(source?.captureModes)?source.captureModes:['raw-two-lens'];
  return ['raw-two-lens','square-left'].filter(mode=>modes.includes(mode));
}
export function captureModeLabel(mode){return labels[mode]??'Capture mode unknown';}
export function captureHeld(capture){return capture?.retirementPending===true||['queued','requested','connected','running','streaming','unknown','stopping'].includes(capture?.state);}
export function frameLayout(metadata){return metadata?.frameLayout==='mono'||metadata?.captureMode==='square-left'?'mono':'stereo-sbs';}
export function presentationView(view,layout){return layout==='mono'&&['left','right','composite'].includes(view)?'full':view;}
export function populateCaptureModes(document,node,source,capture){
  const choices=captureModes(source),previous=node.value,held=captureHeld(capture);
  if([...node.options].map(option=>option.value).join(',')!==choices.join(',')){
    node.replaceChildren();
    for(const mode of choices){const option=document.createElement('option');option.value=mode;option.textContent=captureModeLabel(mode);node.append(option);}
  }
  const actual=choices.includes(capture?.captureMode)?capture.captureMode:null;
  node.value=held&&actual?actual:choices.includes(previous)?previous:choices[0]??'';
  node.disabled=held||choices.length<2;
  return choices;
}
