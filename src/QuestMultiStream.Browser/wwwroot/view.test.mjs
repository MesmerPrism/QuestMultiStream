import test from 'node:test';
import assert from 'node:assert/strict';
import {packet,avcCodec,cropRect,eyeRects,blendPixel} from './view.mjs';
test('actual packet flags and payload, malformed/truncated packets',()=>{
 const b=new ArrayBuffer(16),v=new DataView(b);v.setBigUint64(0,(1n<<62n)|123n);v.setUint32(8,4);
 assert.equal(packet(b).key,true);assert.equal(packet(b).timestamp,123);assert.equal(packet(b).data.length,4);
 assert.throws(()=>packet(b.slice(0,12)));v.setUint32(8,5);assert.throws(()=>packet(b));
 v.setUint32(8,4);v.setBigUint64(0,1n<<61n);assert.throws(()=>packet(b));
});
test('SPS codec and bounded crop',()=>{
 assert.equal(avcCodec(Uint8Array.from([0,0,0,1,0x67,0x42,0xc0,0x1e,0,0])),'avc1.42c01e');
 assert.throws(()=>avcCodec(Uint8Array.from([0,0,1,0x65,1])));
 assert.deepEqual(cropRect(100,80,'40:30:10:20'),{x:10,y:20,w:40,h:30});
 for(const x of ['0:3:0:0','101:4:0:0','2:2:-1:0','x'])assert.throws(()=>cropRect(100,80,x));
 const r=eyeRects(1344,704);assert.equal(r.length,2);assert.ok(r.every(x=>x.x>=0&&x.x+x.w<=1344&&x.y+x.h<=704));
});
test('owner black fallback and unaligned smooth blend',()=>{
 assert.deepEqual(blendPixel([0,0,0],[90,60,30],.5),[90,60,30,255]);
 assert.deepEqual(blendPixel([90,60,30],[0,0,0],.5),[90,60,30,255]);
 assert.deepEqual(blendPixel([60,60,60],[100,100,100],.5),[80,80,80,255]);
});
