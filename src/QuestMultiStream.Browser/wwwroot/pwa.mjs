import {isPublicDocument,parseGatewayLink,rememberGateway,restoreGateway,gatewayPermissionGranted} from './gateway.mjs';
// Installation adds no controller or mutation queue. Public credentials remain session-only.
export async function mountPwa({document,window,storage,loadHub}) {
 const $=id=>document.getElementById(id);
 const publicMode=isPublicDocument(document);
 let loaded=false,installPrompt=null,candidate=null,hadFragment=false;
 if(publicMode){
  hadFragment=!!window.location.hash;
  try{if(hadFragment)candidate=parseGatewayLink(window.location.href,window.location.href);}catch{}
  finally{window.history.replaceState(null,'',window.location.pathname+window.location.search);}
  $('trustedLanWarning').hidden=false;$('operatorLink').required=false;
 }
 async function connect(){
  $('controlHub').hidden=false;$('stopMonitor').hidden=false;
  if(loaded){window.location.reload();return;}
  loaded=true;
  try{await loadHub();}
  catch{$('status').textContent='Controller unavailable';$('operatorConnection').open=true;$('operatorConnectionStatus').textContent='Controls could not load. Check the host and reconnect with its current operator link.';}
 }
 $('operatorLinkForm').onsubmit=async event=>{
  event.preventDefault();const text=$('operatorLink').value.trim();$('operatorLink').value='';
  try{
   if(publicMode){
    if(text)candidate=parseGatewayLink(text,window.location.href);
    const selected=candidate??restoreGateway(storage,window.location.href);
    if(!selected)throw Error('Use the current operator link for this app.');
    if(!$('trustedLanConsent').checked){$('operatorConnectionStatus').textContent='Confirm trusted LAN access before connecting. HTTP and WebSocket traffic is plaintext.';return;}
    rememberGateway(storage,selected,true);
    $('operatorConnection').open=false;$('operatorConnectionStatus').textContent='Connecting to the selected gateway. Allow local network access if your browser asks. If blocked, check this site’s local network permission and gateway reachability.';
    await connect();return;
   }
   const link=new URL(text),here=new URL(window.location.href),token=new URLSearchParams(link.hash.slice(1)).get('token');
   if(link.origin!==here.origin||link.username||link.password||!['/','/index.html'].includes(link.pathname)||link.search||!token)throw Error();
   window.location.hash='#token='+encodeURIComponent(token);
   $('operatorConnection').open=false;$('operatorConnectionStatus').textContent='Connecting with the supplied operator link…';
   await connect();
  }catch{$('operatorConnectionStatus').textContent=publicMode?'Use the current gateway operator link for this same secure app. No connection was made.':'Use the current operator link for this same address, with its #token fragment. No connection was made.';}
 };
 function network(){
  $('networkNotice').hidden=window.navigator.onLine!==false;
  $('networkNotice').textContent='Network unavailable. Current owner status is unverified; actions are not queued or retried. Reconnect to the host to read status.';
 }
 window.addEventListener('offline',network);window.addEventListener('online',network);network();
 $('installApp').hidden=true;
 window.addEventListener('beforeinstallprompt',event=>{event.preventDefault();installPrompt=event;$('installApp').hidden=false;});
 window.addEventListener('appinstalled',()=>{installPrompt=null;$('installApp').hidden=true;});
 $('installApp').onclick=async()=>{
  const prompt=installPrompt;installPrompt=null;$('installApp').hidden=true;
  if(prompt)try{await prompt.prompt();await prompt.userChoice;}catch{}
 };
 $('secureOriginNotice').hidden=window.isSecureContext===true;
 if(publicMode){
  $('controlHub').hidden=true;$('stopMonitor').hidden=true;$('operatorConnection').open=true;
  const retained=restoreGateway(storage,window.location.href);
  if(!hadFragment&&retained?.consent&&await gatewayPermissionGranted(window.navigator,retained.gateway))await connect();
  else{
   $('status').textContent='Disconnected · gateway connection required';
   $('operatorConnectionStatus').textContent=candidate?'Operator link received and removed from the address bar. Confirm trusted LAN access, then Connect.':retained?'Gateway retained for this window. Confirm trusted LAN access and Connect; allow local network permission if offered. Unsupported browsers cannot connect.':'Paste the current gateway operator link. Confirm trusted LAN access and Connect. Local network permission and a supported browser are required.';
  }
  // This registration owns only this app path; the exported worker caches static assets only.
  if(window.navigator.serviceWorker)try{await window.navigator.serviceWorker.register('./public-sw.mjs',{scope:'./',type:'module'});}catch{}
  return;
 }
 let token=new URLSearchParams(window.location.hash.slice(1)).get('token');
 if(!token)try{token=storage.getItem('questms-local-token');}catch{}
 $('controlHub').hidden=!token;$('stopMonitor').hidden=!token;
 if(token)await connect();
 else{$('status').textContent='Disconnected · operator link required';$('operatorConnection').open=true;$('operatorConnectionStatus').textContent='Open the current host operator link here to connect. Installing the app does not grant control.';}
}

if(typeof window!=='undefined'&&typeof document!=='undefined')
 await mountPwa({document,window,storage:sessionStorage,loadHub:()=>import('./hub.mjs')});
