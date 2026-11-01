import test from 'node:test';
import assert from 'node:assert/strict';
test('static public cache excludes every non-GET, query, cross-origin, API, auth and media request',async()=>{
 const {staticRequestAllowed}=await import('./public-cache.mjs');
 const origin='https://controls.example',paths=['/bilateral-control/','/bilateral-control/index.html','/bilateral-control/app.mjs','/bilateral-control/icons/quest-multi-stream-192.png'];
 const request=(url,method='GET',headers={})=>({url,method,headers:new Headers(headers)});
 assert.equal(staticRequestAllowed(request(origin+'/bilateral-control/app.mjs'),origin,paths),true);
 assert.equal(staticRequestAllowed(request(origin+'/bilateral-control/'),origin,paths),true);
 for(const value of [request(origin+'/bilateral-control/app.mjs','POST'),request(origin+'/bilateral-control/app.mjs?x=1'),request(origin+'/bilateral-control/app.mjs#token=x'),request('http://192.168.1.10:43118/api/hub'),request('https://evil.example/bilateral-control/app.mjs'),request(origin+'/api/hub'),request(origin+'/bilateral-control/not-reviewed.mjs'),request(origin+'/viscereality/'),request(origin+'/bilateral-control/app.mjs','GET',{Authorization:'Bearer secret'}),request(origin+'/bilateral-control/app.mjs','GET',{Range:'bytes=0-2'}),request(origin+'/bilateral-control/movie.mp4')])assert.equal(staticRequestAllowed(value,origin,paths),false,value.url);
 // Even an accidentally broadened inventory cannot turn an API/media file into static shell.
 for(const path of ['/bilateral-control/api/hub','/bilateral-control/media.mp4','/bilateral-control/foo.test.mjs'])assert.equal(staticRequestAllowed(request(origin+path),origin,[path]),false);
});
