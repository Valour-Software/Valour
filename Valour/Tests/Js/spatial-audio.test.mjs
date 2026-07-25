import { test } from 'node:test';
import assert from 'node:assert/strict';

// Positional voice graph: panning, distance model, lifecycle.
const checks = [];
const check = (name, actual, expected) => checks.push([name, actual, expected]);

// Minimal Web Audio + DOM stubs
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
const created=[];
globalThis.document={ createElement(){ const el={ style:{}, srcObject:null, paused:true,
    play(){ this.paused=false; return Promise.resolve(); }, remove(){ this.removed=true; } };
  created.push(el); return el; }, body:{ appendChild(){} } };
globalThis.MediaStream = class { constructor(t){ this.tracks=t||[]; } };

const { createSpatialAudio } = await import('../../Client/wwwroot/ts/VillageSpatialAudio.js');

const sa = createSpatialAudio({ refDistance: 2, maxDistance: 14, rolloff: 1.4 });
const streamA = new MediaStream(['a']);

sa.setEnabled(true);
sa.setListener(10, 10);
sa.upsert('u1', 13, 10, streamA);          // 3 tiles to the RIGHT

const ctx = lastCtx;
const panner = ctx.sources[0].connected[0];
check('1. graph built (source->panner)', panner instanceof Panner, true);
check('2. panner uses HRTF', panner.panningModel, 'HRTF');
check('3. distance model + range', [panner.distanceModel,panner.refDistance,panner.maxDistance], ['inverse',2,14]);
check('4. source right of listener -> +X', panner.positionX.value, 3);
check('5. flat plane -> Y is 0', panner.positionY.value, 0);
check('6. no depth offset -> Z is 0', panner.positionZ.value, 0);

sa.setListener(10, 14);                     // listener moves 4 BELOW the source
check('7. source above listener -> -Z', panner.positionZ.value, -4);

const gain = panner.connected[0];
check('8. enabled -> gain 1', gain.gain.value, 1);
sa.setEnabled(false);
check('9. disabled -> gain 0', gain.gain.value, 0);
sa.setEnabled(true);

check('10. muted element keeps track alive', created[0].muted, true);
check('11. element got the stream', created[0].srcObject===streamA, true);

// position-only update must not rebuild the graph
const sourceCountBefore = ctx.sources.length;
sa.upsert('u1', 11, 14, null);
check('12. position update reuses graph', ctx.sources.length, sourceCountBefore);
check('13. position update applied', panner.positionX.value, 1);

// a renegotiated stream must rebuild
const streamB = new MediaStream(['b']);
sa.upsert('u1', 11, 14, streamB);
check('14. new stream rebuilds graph', ctx.sources.length, sourceCountBefore+1);

sa.remove('u1');
check('15. remove disconnects', ctx.sources[ctx.sources.length-1].disconnected, true);

sa.upsert('u2', 1, 1, new MediaStream(['c']));
sa.dispose();
check('16. dispose closes context', ctx.closed, true);



for (const [name, actual, expected] of checks) {
    test(name, () => assert.deepEqual(actual, expected));
}
