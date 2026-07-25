import { test } from 'node:test';
import assert from 'node:assert/strict';

// Texture cache: onLoaded must fire on cache hits and for concurrent callers.
const checks = [];
const check = (name, actual, expected) => checks.push([name, actual, expected]);

// Minimal browser Image stub: load resolves async unless the url contains "fail"
globalThis.Image = class {
  constructor(){ this.width=0; this.height=0; this._src=''; }
  set src(v){
    this._src=v;
    setTimeout(()=>{
      if(String(v).includes('fail')){ this.onerror && this.onerror(); }
      else { this.width=1024; this.height=2048; this.onload && this.onload(); }
    }, 5);
  }
  get src(){ return this._src; }
};

const {
  loadTexture,
  getCallAudioElementId,
  normalizeTileDefinition,
  getBottomAnchoredSpriteBounds,
  getBottomAnchoredCollisionCells,
  getVillageRenderScale,
  adjustVillageZoom
} = await import('../../Client/wwwroot/ts/VillageTileRendering.js');
const wait = (fn,ms=500)=>new Promise(r=>{const t=setTimeout(()=>r('NEVER FIRED'),ms); fn(()=>{clearTimeout(t); r('fired');});});


const cache=new Map(); const url='/sheet.png';

check('1. fresh load fires callback',
  await wait(cb=>loadTexture(cache,url,cb)), 'fired');

check('2. CACHE HIT still fires callback (the bug)',
  await wait(cb=>loadTexture(cache,url,cb)), 'fired');

const tex=cache.get(url);
check('3. cached texture reports real dimensions', `${tex.image.width}x${tex.image.height}`, '1024x2048');

// two callers racing the same in-flight load must both be notified
const c2=new Map(); let fired=0;
await new Promise(r=>{ let d=0; const t=setTimeout(r,500); const cb=()=>{fired++; if(++d===2){clearTimeout(t); r();}};
  loadTexture(c2,'/other.png',cb); loadTexture(c2,'/other.png',cb); });
check('4. both in-flight callers notified', fired, 2);

check('5. failure path notifies too',
  await wait(cb=>loadTexture(new Map(),'/fail.png',cb)), 'fired');

const c3=new Map(); await wait(cb=>loadTexture(c3,'/fail.png',cb));
check('6. failure is flagged on the texture', c3.get('/fail.png').failed, true);

check('7. no callback passed does not throw', (loadTexture(new Map(),'/x.png')!==null), true);

check('8. call audio id matches CallPanel ASCII encoding',
  getCallAudioElementId('peer:42'), 'call-audio-706565723a3432');

check('9. call audio id uses UTF-8 for non-ASCII peer ids',
  getCallAudioElementId('é'), 'call-audio-c3a9');

const treeDefinition = normalizeTileDefinition({
  Kind: 'Sprite',
  Key: 'tree',
  Width: 3,
  Height: 4,
  Collision: [false, false, false, false, false, false, false, false, false, false, true, false]
});
check('10. PascalCase collision masks are normalized',
  treeDefinition.collision, [false, false, false, false, false, false, false, false, false, false, true, false]);

check('11. tall sprite bounds overhang above the physical footprint',
  getBottomAnchoredSpriteBounds(10, 20, 1, 3, 4),
  { x: 10, y: 17, width: 3, height: 4 });

check('12. collision mask uses the same bottom anchor as rendering',
  getBottomAnchoredCollisionCells(10, 20, 1, treeDefinition),
  [{ x: 11, y: 20 }]);

check('13. desktop interiors retain a scrollable camera',
  getVillageRenderScale(1280, 'Interior'), 3);

check('14. mobile interiors remain readable and scrollable',
  getVillageRenderScale(390, 'Interior'), 2);

check('15. outdoor zoom is unchanged',
  getVillageRenderScale(1280, 'Outdoor'), 2);

check('16. village zoom advances in crisp quarter steps',
  adjustVillageZoom(1, 1), 1.25);

check('17. village zoom is capped at 200 percent',
  adjustVillageZoom(2, 1), 2);

check('18. village zoom is capped at 50 percent',
  adjustVillageZoom(0.5, -1), 0.5);


for (const [name, actual, expected] of checks) {
    test(name, () => assert.deepEqual(actual, expected));
}
