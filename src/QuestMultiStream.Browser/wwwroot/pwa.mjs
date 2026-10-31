// Installation adds no controller, credential persistence, offline cache or mutation queue.
// The existing hub remains the only consumer of fragment/session authentication.
export async function mountPwa({document,window,storage,loadHub}) {
 const $=id=>document.getElementById(id);
 let loaded=false,installPrompt=null;
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
   const link=new URL(text),here=new URL(window.location.href),token=new URLSearchParams(link.hash.slice(1)).get('token');
   if(link.origin!==here.origin||link.username||link.password||!['/','/index.html'].includes(link.pathname)||link.search||!token)throw Error();
   window.location.hash='#token='+encodeURIComponent(token);
   $('operatorConnection').open=false;$('operatorConnectionStatus').textContent='Connecting with the supplied operator link…';
   await connect();
  }catch{$('operatorConnectionStatus').textContent='Use the current operator link for this same address, with its #token fragment. No connection was made.';}
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
 let token=new URLSearchParams(window.location.hash.slice(1)).get('token');
 if(!token)try{token=storage.getItem('questms-local-token');}catch{}
 $('controlHub').hidden=!token;$('stopMonitor').hidden=!token;
 if(token)await connect();
 else{$('status').textContent='Disconnected · operator link required';$('operatorConnection').open=true;$('operatorConnectionStatus').textContent='Open the current host operator link here to connect. Installing the app does not grant control.';}
}

if(typeof window!=='undefined'&&typeof document!=='undefined')
 await mountPwa({document,window,storage:sessionStorage,loadHub:()=>import('./hub.mjs')});

