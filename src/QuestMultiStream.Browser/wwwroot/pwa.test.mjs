import test from 'node:test';
import assert from 'node:assert/strict';

const module=()=>import('./pwa.mjs');
function fixture({token='',stored=null,online=true}={}) {
 const nodes=new Map(),events=new Map(),loads=[];
 const element=id=>{if(!nodes.has(id))nodes.set(id,{hidden:false,value:'',textContent:'',open:false});return nodes.get(id);};
 const document={getElementById:element};
 const window={location:{href:'http://127.0.0.1:43118/'+(token?'#token='+token:''),hash:token?'#token='+token:''},navigator:{onLine:online},isSecureContext:true,addEventListener(name,fn){events.set(name,fn);}};
 const storage={getItem(){return stored;}};
 return {document,window,storage,loads,events,element,loadHub:async()=>loads.push(window.location.hash)};
}

test('fresh installed window stays disconnected until an explicit same-origin operator link; never persists credentials',async()=>{
 const {mountPwa}=await module(),f=fixture();await mountPwa(f);
 assert.equal(f.loads.length,0);assert.equal(f.element('controlHub').hidden,true);assert.equal(f.element('operatorConnection').open,true);
 f.element('operatorLink').value='http://127.0.0.1:43118/#token=fresh-operator';
 await f.element('operatorLinkForm').onsubmit({preventDefault(){}});
 assert.deepEqual(f.loads,['#token=fresh-operator']);assert.equal(f.element('operatorLink').value,'');assert.equal(f.element('controlHub').hidden,false);
});

test('foreign, query-authenticated or non-hub links never bootstrap a control session',async()=>{
 const {mountPwa}=await module();
 for(const link of ['https://other.example/#token=secret','http://127.0.0.1:43118/?token=secret','http://127.0.0.1:43118/viewer.html#token=secret','http://u:p@127.0.0.1:43118/#token=secret','http://127.0.0.1:43118/#token=']){
  const f=fixture();await mountPwa(f);f.element('operatorLink').value=link;await f.element('operatorLinkForm').onsubmit({preventDefault(){}});
  assert.equal(f.loads.length,0,link);assert.equal(f.element('controlHub').hidden,true);assert.equal(f.element('operatorLink').value,'');assert.match(f.element('operatorConnectionStatus').textContent,/same address/);
 }
});

test('ordinary authenticated tab and same-window relaunch preserve existing hub bootstrap without changing credential storage',async()=>{
 const {mountPwa}=await module();for(const input of [{token:'operator'},{stored:'operator'}]){const f=fixture(input);await mountPwa(f);assert.equal(f.loads.length,1);assert.equal(f.element('controlHub').hidden,false);}
});

test('network advisory never claims owner connectivity or replays a bootstrap',async()=>{
 const {mountPwa}=await module(),f=fixture({token:'operator',online:false});await mountPwa(f);
 assert.match(f.element('networkNotice').textContent,/unverified/);assert.equal(f.element('networkNotice').hidden,false);
 f.window.navigator.onLine=true;f.events.get('online')();assert.equal(f.element('networkNotice').hidden,true);assert.equal(f.loads.length,1);
});

test('install prompt requires a user action, is consumed once and hides after installation',async()=>{
 const {mountPwa}=await module(),f=fixture();await mountPwa(f);let prompts=0;
 assert.equal(f.element('installApp').hidden,true);
 const prompt={preventDefault(){},async prompt(){prompts++;},userChoice:Promise.resolve({outcome:'dismissed'})};f.events.get('beforeinstallprompt')(prompt);
 assert.equal(prompts,0);assert.equal(f.element('installApp').hidden,false);await f.element('installApp').onclick();assert.equal(prompts,1);assert.equal(f.element('installApp').hidden,true);
 f.events.get('appinstalled')();assert.equal(f.element('installApp').hidden,true);
});
