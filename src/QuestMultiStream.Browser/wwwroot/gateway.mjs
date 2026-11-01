// Public mode selects exactly one explicit gateway. Nothing here probes a network.
const tokenPattern=/^[0-9a-f]{48}$/;
export function appBase(href){
 const url=new URL(href);
 if(url.protocol!=='https:'||url.username||url.password||!/^\/bilateral-control\/(?:index\.html|viewer\.html)?$/.test(url.pathname))throw Error('Use this secure control app');
 return url.origin+'/bilateral-control/';
}
export function gatewayOrigin(text){
 if(typeof text!=='string'||!/^http:\/\/\d{1,3}(?:\.\d{1,3}){3}(?::[1-9]\d{0,4})?$/.test(text))throw Error('Use an exact private IPv4 HTTP gateway');
 const url=new URL(text),parts=url.hostname.split('.').map(Number);
 if(parts.some((n,i)=>n>255||String(n)!==text.slice(7).split(':')[0].split('.')[i])||!(parts[0]===10||parts[0]===127||parts[0]===172&&parts[1]>=16&&parts[1]<=31||parts[0]===192&&parts[1]===168))throw Error('Gateway must be a private or loopback IPv4 address');
 return url.origin;
}
export function parseGatewayLink(text,here){
 const url=new URL(text),base=appBase(here),parameters=new URLSearchParams(url.hash.slice(1));
 if(appBase(url.href)!==base||!['/bilateral-control/','/bilateral-control/index.html'].includes(url.pathname)||url.search||parameters.size!==2||parameters.getAll('gateway').length!==1||parameters.getAll('token').length!==1||!tokenPattern.test(parameters.get('token')??''))throw Error('Use the current operator link for this app');
 return {appUrl:base,gateway:gatewayOrigin(parameters.get('gateway')),token:parameters.get('token')};
}
const namespace=context=>'questms.public-bilateral.v1:'+encodeURIComponent(context.appUrl)+':'+encodeURIComponent(context.gateway)+':';
export function scopedStorage(storage,context){
 const prefix=namespace(context);
 return {getItem:key=>storage.getItem(prefix+key),setItem:(key,value)=>storage.setItem(prefix+key,value),removeItem:key=>storage.removeItem(prefix+key)};
}
const selectionKey=app=>'questms.public-bilateral.v1:'+encodeURIComponent(app)+':selected-gateway';
export function rememberGateway(storage,context,consent){
 if(!consent)throw Error('Trusted LAN consent required');
 const scope=scopedStorage(storage,context);scope.setItem('token',context.token);scope.setItem('trusted-lan-consent','1');storage.setItem(selectionKey(context.appUrl),context.gateway);
}
export function restoreGateway(storage,here){
 try{const appUrl=appBase(here),gateway=gatewayOrigin(storage.getItem(selectionKey(appUrl))),scope=scopedStorage(storage,{appUrl,gateway}),token=scope.getItem('token');
  if(!tokenPattern.test(token??''))return null;
  return {appUrl,gateway,token,consent:scope.getItem('trusted-lan-consent')==='1'};
 }catch{return null;}
}
export function approvedViewer(storage,context){
 const retained=restoreGateway(storage,context.appUrl);
 return retained?.consent===true&&retained.gateway===context.gateway&&retained.token===context.token;
}
export async function gatewayPermissionGranted(navigator,gateway){
 try{return (await navigator.permissions.query({name:new URL(gateway).hostname.startsWith('127.')?'loopback-network':'local-network'})).state==='granted';}catch{return false;}
}
function endpoint(path){
 if(typeof path!=='string'||!/^\/(?:api\/|observe\/|stream\/|operator-stop$)/.test(path)||/[?#\\]/.test(path)||/\.\.|%2e|%2f|%5c/i.test(path))throw Error('Invalid gateway endpoint');
 return path;
}
export function createTransport(context,fetcher=(...args)=>fetch(...args)){
 const {appUrl,gateway,token}=context;
 const fragment='#'+new URLSearchParams({gateway,token}).toString();
 return {context,
  async fetch(path,options={}){
   endpoint(path);const headers={...options.headers};
   if(Object.keys(headers).some(key=>key.toLowerCase()==='authorization'))throw Error('Gateway owns Authorization');
   return fetcher(gateway+path,{...options,headers:{...headers,Authorization:'Bearer '+token},credentials:'omit',cache:'no-store',redirect:'error',referrerPolicy:'no-referrer',targetAddressSpace:new URL(gateway).hostname.startsWith('127.')?'loopback':'local'});
  },
  webSocket:path=>gateway.replace(/^http:/,'ws:')+endpoint(path)+'?token='+encodeURIComponent(token),
  viewer:(pane,source,observe=true)=>appUrl+'viewer.html?'+new URLSearchParams({pane,source,...(observe?{observe:'1'}:{})})+fragment
 };
}
export function isPublicDocument(document){return document.documentElement?.dataset.publicGateway==='true';}
export function initializeTransport({document,location,storage,history,viewer=false,fetcher=(...args)=>fetch(...args)}){
 if(isPublicDocument(document)){
  let context;
  try{if(viewer){
   // A fragment alone never admits a fresh viewer. The parent must have connected.
   const url=new URL(location.href),params=new URLSearchParams(url.hash.slice(1));
   context=parseGatewayLink(appBase(url.href)+'#'+params.toString(),url.href);
  }else context=restoreGateway(storage,location.href);
  }catch{}finally{history.replaceState(null,'',location.pathname+(location.search??''));}
  if(!context||!approvedViewer(storage,context))return null;
  return {...createTransport(context,fetcher),storage:scopedStorage(storage,context)};
 }
 const token=new URLSearchParams(location.hash.slice(1)).get('token')??storage.getItem('questms-local-token')??'';
 if(token)storage.setItem('questms-local-token',token);history.replaceState(null,'',location.pathname);
 return {storage,context:null,token,
  fetch:(path,options)=>fetcher(path+'?token='+encodeURIComponent(token),options),
  webSocket:path=>`${location.protocol==='https:'?'wss':'ws'}://${location.host}${path}?token=${encodeURIComponent(token)}`,
  viewer:(pane,source,observe=true)=>'viewer.html?pane='+pane+(observe?'&observe=1':'')+'&source='+encodeURIComponent(source)+'#token='+encodeURIComponent(token)
 };
}
