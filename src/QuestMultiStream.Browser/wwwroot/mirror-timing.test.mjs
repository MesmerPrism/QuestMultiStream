import test from 'node:test';
import assert from 'node:assert/strict';
import * as timing from './frame-status.mjs';

test('mirror gap measurements use browser arrival/draw deltas and separate producer PTS deltas',()=>{
 assert.equal(typeof timing.MirrorTiming,'function');
 let now=0;const value=new timing.MirrorTiming(()=>now);
 value.packet({config:true,timestamp:0});
 value.packet({config:false,timestamp:500000000});value.draw();
 now=999;value.packet({config:false,timestamp:500033333});value.draw();
 now=1999;value.packet({config:false,timestamp:501033333});value.draw();
 const snapshot=value.snapshot();
 assert.equal(snapshot.videoPackets,3);assert.equal(snapshot.canvasDraws,3);
 assert.equal(snapshot.arrival.maxGapMs,1000);assert.equal(snapshot.arrival.gapsAtLeast1s,1);
 assert.equal(snapshot.canvasDraw.maxGapMs,1000);assert.equal(snapshot.canvasDraw.gapsAtLeast1s,1);
 assert.equal(snapshot.sourcePts.maxGapMs,1000);assert.equal(snapshot.sourcePts.gapsAtLeast1s,1);
 assert.equal(snapshot.sourcePts.clock,'producer-pts-us');assert.equal(snapshot.arrival.clock,'browser-performance-now-ms');
 assert.equal(snapshot.arrival.ageMs,0);now=2999;assert.equal(value.snapshot().canvasDraw.ageMs,1000);
 assert.equal(Object.hasOwn(snapshot,'latencyMs'),false,'unmapped producer PTS never becomes a PC latency');
});

test('hidden viewer intervals and config PTS resets never become delivery gaps',()=>{
 assert.equal(typeof timing.MirrorTiming,'function');
 let now=0;const value=new timing.MirrorTiming(()=>now);
 value.packet({config:false,timestamp:100});value.draw();
 value.visibility(false);now=5000;value.packet({config:false,timestamp:5000100});
 value.visibility(true);now=10000;value.packet({config:true,timestamp:0});
 value.packet({config:false,timestamp:0});value.draw();
 now=10033;value.packet({config:false,timestamp:33333});value.draw();
 const snapshot=value.snapshot();
 assert.equal(snapshot.hiddenPackets,1);assert.equal(snapshot.visibleSegments,2);
 assert.equal(snapshot.arrival.gapsAtLeast1s,0);assert.equal(snapshot.canvasDraw.gapsAtLeast1s,0);
 assert.equal(snapshot.sourcePts.gapsAtLeast1s,0);assert.equal(snapshot.sourcePts.nonadvancing,0);
 value.packet({config:false,timestamp:33333});assert.equal(value.snapshot().sourcePts.nonadvancing,1);
});

test('a connected stream without video remains unobserved, including browser clock zero',()=>{
 assert.equal(typeof timing.MirrorTiming,'function');
 let now=0;const value=new timing.MirrorTiming(()=>now);
 assert.equal(value.snapshot().arrival.ageMs,null);assert.equal(value.snapshot().canvasDraw.maxGapMs,null);
 value.packet({config:true,timestamp:0});assert.equal(value.snapshot().videoPackets,0);
 value.packet({config:false,timestamp:0});now=1001;
 assert.equal(value.snapshot().arrival.ageMs,1001);assert.equal(value.snapshot().canvasDraw.ageMs,null);
});
