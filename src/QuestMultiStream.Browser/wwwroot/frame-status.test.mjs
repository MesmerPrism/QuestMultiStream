import test from 'node:test';
import assert from 'node:assert/strict';
import {decodedStatus} from './frame-status.mjs';
test('decoded recovery replaces waiting and stale-age labels',()=>{
  for(const old of ['Waiting for next keyframe','No new decoded frame']){
    let label=old;const live=decodedStatus('Live host','',true,1344,704);if(live)label=live;
    assert.equal(label,'1344 × 704 · H.264 · Live host');
  }
});
test('late/error/local output cannot replace terminal transport truth',()=>{
  assert.equal(decodedStatus('Live host','Decoder failed',true,1344,704),null);
  assert.equal(decodedStatus('Live host','',false,1344,704),null);
  assert.equal(decodedStatus('Local file','',true,1344,704),null);
});
