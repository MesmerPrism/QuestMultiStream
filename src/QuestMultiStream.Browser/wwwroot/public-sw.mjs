import {staticRequestAllowed} from './public-cache.mjs';
// Export sets the exact version and finite static shell. Local root never registers this worker.
const CACHE_VERSION='unexported';
const STATIC_PATHS=[];
const CACHE_PREFIX='questms-public-bilateral-v1-';
const CACHE_NAME=CACHE_PREFIX+CACHE_VERSION;
self.addEventListener('install',event=>event.waitUntil((async()=>{
 const cache=await caches.open(CACHE_NAME);
 // credentials omitted and redirects refused even for the public static shell.
 for(const path of STATIC_PATHS){const request=new Request(new URL(path,self.location.origin),{credentials:'omit',cache:'reload',redirect:'error',referrerPolicy:'no-referrer'});const response=await fetch(request);if(!response.ok||response.type!=='basic')throw Error('Static shell unavailable');await cache.put(request,response);}
})()));
self.addEventListener('activate',event=>event.waitUntil((async()=>{
 for(const key of await caches.keys())if(key.startsWith(CACHE_PREFIX)&&key!==CACHE_NAME)await caches.delete(key);
})()));
self.addEventListener('fetch',event=>{
 if(!staticRequestAllowed(event.request,self.location.origin,STATIC_PATHS))return;
 event.respondWith((async()=>{
  const cache=await caches.open(CACHE_NAME),cached=await cache.match(event.request);
  return cached??fetch(event.request);
 })());
});
