export function sessionModel(value){
 const unavailable={available:false,message:'Session evidence unavailable. Current authority and flow are unverified.',members:[]};
 if(!value||value.schema!=='questms.hub.session-status.v1'||value.currentAuthoritySupported!==false)return unavailable;
 if(value.state!=='retained-evidence'||value.publisherAuthenticated!==true||typeof value.session!=='string'||!Array.isArray(value.members)||value.members.length!==2)return unavailable;
 const members=[];
 for(const peer of ['Q','W']){
  const rows=value.members.filter(row=>row?.peer===peer);if(rows.length!==1)return unavailable;
  const row=rows[0];
  if(!Number.isSafeInteger(row.appProcessGeneration)||row.appProcessGeneration<=0||typeof row.processEpoch!=='string'||!row.processEpoch.startsWith(`app-process.${row.appProcessGeneration}.`)||typeof row.nativeProcessGeneration!=='string'||!/^[1-9][0-9]{0,19}$/.test(row.nativeProcessGeneration))return unavailable;
  const r=row.retainedEvidence;
  if(r&&(r.currentAuthoritySupported!==false||r.session!==value.session||r.appProcessGeneration!==row.appProcessGeneration||r.nativeProcessGeneration!==row.nativeProcessGeneration||r.acceptedLifecycle!=='active'||typeof r.receipt!=='string'||!Number.isFinite(Date.parse(r.observedUtc))||!Number.isFinite(Date.parse(r.expiresUtc))||Date.parse(r.expiresUtc)<=Date.parse(r.observedUtc)))return unavailable;
  members.push({...row,retainedEvidence:r??null});
 }
 return {available:true,message:'Retained owner evidence only · current authority and peer flow unavailable.',session:value.session,members};
}

const rendered=new WeakMap(); // DOM only; each response still validates. No authority cache.
export function renderSession(root,value){
 const model=sessionModel(value),doc=root.ownerDocument;
 const signature=JSON.stringify(model);if(rendered.get(root)===signature)return;rendered.set(root,signature);
 root.replaceChildren();
 const message=doc.createElement('p');message.className='sessionNotice';message.setAttribute('role','status');message.textContent=model.message;root.append(message);
 if(!model.available)return;
 const session=doc.createElement('p');session.className='sessionIdentity';session.textContent='Session: '+model.session;root.append(session);
 const cards=doc.createElement('div');cards.className='sessionCards';root.append(cards);
 for(const member of model.members){
  const card=doc.createElement('article');card.className='sessionCard';card.setAttribute('aria-label',`Quest ${member.peer} session evidence`);
  const title=doc.createElement('h3');title.textContent='Quest '+member.peer;card.append(title);
  const fields=doc.createElement('dl');card.append(fields);
  const field=(name,value)=>{const label=doc.createElement('dt'),text=doc.createElement('dd');label.textContent=name;text.textContent=value;fields.append(label,text);};
  field('Application generation',String(member.appProcessGeneration));field('Process epoch',member.processEpoch);field('Native generation',member.nativeProcessGeneration);
  field('Current authority / peer flow','Unavailable');
  const r=member.retainedEvidence;
  if(r){
   const details=doc.createElement('details'),summary=doc.createElement('summary');summary.textContent='Retained receipt';details.append(summary);
   const text=doc.createElement('p');text.textContent=r.receipt;details.append(text);
   const times=doc.createElement('p');times.textContent=`Native observation: ${r.observedUtc} · native expiry: ${r.expiresUtc}. Recorded timestamps; not a current authority check.`;details.append(times);card.append(details);
  }else{const empty=doc.createElement('p');empty.textContent='No retained receipt available.';card.append(empty);}
  cards.append(card);
 }
}
