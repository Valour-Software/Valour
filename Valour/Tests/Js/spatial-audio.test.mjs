import { test } from 'node:test';
import assert from 'node:assert/strict';

// Positional voice graph: panning, distance model, lifecycle.
const checks = [];
const check = (name, actual, expected) => checks.push([name, actual, expected]);

// Minimal Web Audio stubs
class Param { constructor(v=0){this.value=v; this.targets=[];}
  setTargetAtTime(v){ this.value=v; this.targets.push(v); } }
class Node2 { constructor(){ this.connected=[]; this.disconnected=false; }
  connect(n){ this.connected.push(n); return n; } disconnect(){ this.disconnected=true; } }
class Panner extends Node2 { constructor(){ super();
  this.positionX=new Param(); this.positionY=new Param(); this.positionZ=new Param();
  this.panningModel=''; this.distanceModel=''; this.refDistance=0; this.maxDistance=0; this.rolloffFactor=0; } }
class Gain extends Node2 { constructor(){ super(); this.gain=new Param(0); } }
class Ctx {
  constructor(){ this.state='suspended'; this.currentTime=0; this.destination=new Node2();
    this.sources=[]; this.closed=false; }
  createMediaStreamSource(s){ const n=new Node2(); n.stream=s; this.sources.push(n); return n; }
  createPanner(){ return new Panner(); }
  createGain(){ return new Gain(); }
  async resume(){ this.state='running'; }
  async close(){ this.closed=true; }
}
let lastCtx=null;
globalThis.window={ AudioContext: function(){ lastCtx=new Ctx(); return lastCtx; } };
globalThis.MediaStream = class { constructor(t){ this.tracks=t||[]; } };

const { createSpatialAudio, calculateDistanceGain } =
  await import('../../Client/wwwroot/ts/VillageSpatialAudio.js');

const sa = createSpatialAudio({ refDistance: 2, maxDistance: 14 });
const streamA = new MediaStream(['a']);

sa.setEnabled(true);
sa.setListener(10, 10);
const routed = sa.upsert('u1', 13, 10, streamA, 0.35); // 3 tiles to the RIGHT

const ctx = lastCtx;
const panner = ctx.sources[0].connected[0];
check('1. graph built (source->panner)', panner instanceof Panner, true);
check('2. panner uses HRTF', panner.panningModel, 'HRTF');
check('3. panner handles direction only',
  [panner.distanceModel,panner.refDistance,panner.maxDistance,panner.rolloffFactor],
  ['inverse',1,10000,0]);
check('4. source right of listener -> +X', panner.positionX.value, 3);
check('5. flat plane -> Y is 0', panner.positionY.value, 0);
check('6. no depth offset -> Z is 0', panner.positionZ.value, 0);
check('7. successful graph reports routed', routed, true);

const distanceGain = panner.connected[0];
const outputGain = distanceGain.connected[0];
check('8. distance uses smooth attenuation',
  distanceGain.gain.value,
  calculateDistanceGain(3, 2, 14));
check('9. participant volume is preserved', outputGain.gain.value, 0.35);

sa.setListener(10, 14);                     // listener moves 4 BELOW the source
check('10. source above listener -> -Z', panner.positionZ.value, -4);

sa.setEnabled(false);
check('11. disabled -> output gain 0', outputGain.gain.value, 0);
sa.setEnabled(true);
check('12. re-enabled -> participant volume', outputGain.gain.value, 0.35);

sa.setListener(30, 14);
check('13. voice reaches true silence at max range', distanceGain.gain.value, 0);

// position-only update must not rebuild the graph
const sourceCountBefore = ctx.sources.length;
sa.setListener(10, 14);
sa.upsert('u1', 11, 14, null, 0.6);
check('14. position update reuses graph', ctx.sources.length, sourceCountBefore);
check('15. position update applied', panner.positionX.value, 1);
check('16. volume update reaches graph', outputGain.gain.value, 0.6);
sa.upsert('u1', 11, 14, null, Number.NaN);
check('17. invalid participant volume fails safe', outputGain.gain.value, 1);

// a renegotiated stream must rebuild
const streamB = new MediaStream(['b']);
sa.upsert('u1', 11, 14, streamB, 0.6);
check('18. new stream rebuilds graph', ctx.sources.length, sourceCountBefore+1);

sa.remove('u1');
check('19. remove disconnects', ctx.sources[ctx.sources.length-1].disconnected, true);

sa.upsert('u2', 1, 1, new MediaStream(['c']));
sa.dispose();
check('20. dispose closes context', ctx.closed, true);

check('21. curve is full volume inside near range', calculateDistanceGain(2, 2, 14), 1);
check('22. curve is silent outside far range', calculateDistanceGain(14, 2, 14), 0);
check('23. invalid distance fails silent', calculateDistanceGain(Number.NaN, 2, 14), 0);

// If Web Audio is unavailable, the caller must know not to mute its normal
// audio element. upsert's false result is that fallback signal.
window.AudioContext = undefined;
const fallback = createSpatialAudio();
check('24. unsupported browser reports unrouted',
  fallback.upsert('u3', 0, 0, new MediaStream(['d'])),
  false);

window.AudioContext = function(){
  const broken = new Ctx();
  broken.createMediaStreamSource = () => { throw new Error('device unavailable'); };
  return broken;
};
const brokenGraph = createSpatialAudio();
check('25. graph construction failure reports unrouted',
  brokenGraph.upsert('u4', 0, 0, new MediaStream(['e'])),
  false);

window.AudioContext = function(){ throw new Error('context limit'); };
const brokenContext = createSpatialAudio();
check('26. context construction failure reports unrouted',
  brokenContext.upsert('u5', 0, 0, new MediaStream(['f'])),
  false);



for (const [name, actual, expected] of checks) {
    test(name, () => assert.deepEqual(actual, expected));
}
