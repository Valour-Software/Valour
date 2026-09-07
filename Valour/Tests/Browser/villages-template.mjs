import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const output = resolve('TestResults/village-browser/template'); await mkdir(output, {recursive:true});
const browser = await chromium.launch({headless:true, executablePath:process.env.BROWSER_EXECUTABLE});
const context = await browser.newContext({viewport:{width:1600,height:1000}});
const page = await context.newPage(); page.setDefaultTimeout(20000);
const checks=[], errors=[], moves=[], joins=[]; let scene, headers;
const parse = s => JSON.parse(s, (k,v,c) => typeof v==='number' && !Number.isSafeInteger(v) ? c.source : v);
page.on('pageerror',e=>errors.push(e.message));
page.on('websocket', ws => ws.on('framesent', frame => {
    if (typeof frame.payload !== 'string') return;
    for (const part of frame.payload.split('\x1e').filter(Boolean)) {
        try { const event=parse(part); if(event.target==='MoveInVillage') moves.push(event.arguments); if(event.target==='JoinVillageMap') joins.push(event.arguments); } catch {}
    }
}));
page.on('response',async r=>{if(r.url().endsWith('/village/poc')&&r.ok()){scene=await r.json(); const h=await r.request().allHeaders(); headers={authorization:h.authorization};}});
const record=name=>{checks.push(name);console.log('PASS '+name);};
const shot=name=>page.screenshot({path:`${output}/${name}.png`});
async function waitUntil(predicate, label, timeout=20000){const end=Date.now()+timeout;while(!predicate()){assert.ok(Date.now()<end,label);await page.waitForTimeout(50);}}
async function step(key){const before=moves.length;await page.keyboard.press(key);await waitUntil(()=>moves.length>before,`Movement stuck after ${key}`);await page.waitForTimeout(80);return moves.at(-1);}
async function enter(name){await page.getByRole('button',{name:'Browse village places'}).click();await page.locator('.place-list-item').filter({has:page.getByText(name,{exact:true})}).click();await page.getByRole('button',{name:'Enter',exact:true}).click();await page.getByRole('button',{name:'Leave building',exact:true}).waitFor();await page.locator('.hud-chip.live').waitFor();await page.waitForTimeout(500);}
try {
 await page.goto(origin);
 await page.locator('input[type=email]').fill(process.env.VILLAGE_QA_EMAIL);
 await page.locator('input[type=password]').fill(process.env.VILLAGE_QA_PASSWORD);
 await page.getByRole('button',{name:'Log in',exact:true}).click();
 await page.getByText('Village Release QA',{exact:false}).first().waitFor({timeout:90000});
 await page.getByText('Village Release QA',{exact:false}).first().click();
 await page.getByText('Village',{exact:true}).first().click();
 await page.locator('.village-canvas').waitFor({timeout:60000});
 await page.locator('.hud-chip.live').waitFor();
 await waitUntil(()=>!!scene,'Scene loaded');
 if (!process.env.VILLAGE_QA_STAFF_ONLY) {
 await shot('commons');
 await page.locator('.village-canvas').focus();
 // Walk around the plaza, then up the actual porch steps, without teleporting through Places.
 for(let i=0;i<2;i++) await step('ArrowDown');
 for(let i=0;i<20;i++) await step('ArrowLeft');
 for(let i=0;i<12;i++) await step('ArrowUp');
 await page.getByRole('button',{name:'Leave building',exact:true}).waitFor();
 await page.locator('.hud-chip.live').waitFor();
 assert.ok(joins.length>=2);record('Walk from commons up the porch into the actual house door');
 for(let cycle=0;cycle<4;cycle++){
   await page.waitForTimeout(350);
   await page.locator('.village-canvas').focus();
   await step('ArrowUp'); await step('ArrowDown');
   await page.getByRole('button',{name:'Leave building',exact:true}).waitFor({state:'hidden'});
   await page.locator('.hud-chip.live').waitFor();await page.waitForTimeout(350);
   await step('ArrowUp');
   await page.getByRole('button',{name:'Leave building',exact:true}).waitFor();
   await page.locator('.hud-chip.live').waitFor();
 }
 record('Four physical exit and re-entry cycles retain movement and presence');
 for(const name of ['Town Hall','Voice Lounge','Maker House','Studio']){
   await page.getByRole('button',{name:'Leave building',exact:true}).click();
   await page.getByRole('button',{name:'Leave building',exact:true}).waitFor({state:'hidden'});
   await enter(name);
   while((await page.getByRole('button',{name:'Zoom out',exact:true}).isEnabled())){await page.getByRole('button',{name:'Zoom out',exact:true}).click(); await page.waitForTimeout(300);}
   await page.waitForTimeout(400); await shot(name.toLowerCase().replaceAll(' ','-'));
 }
 record('All four distinct furnished interiors render and can be entered and left');
 await page.getByRole('button',{name:'Leave building',exact:true}).click();
 for(const width of [1600,1100,850,650]){
   await page.setViewportSize({width,height:1000}); await page.waitForTimeout(250);
   const brand=await page.locator('.village-world-header').boundingBox(), controls=await page.locator('.village-quick-controls').boundingBox();
   assert.ok(brand.x+brand.width<=controls.x+1 || brand.y+brand.height<=controls.y+1,`HUD overlap at ${width}`);
   await shot(`layout-${width}`);
 }
 record('Header and controls do not overlap across four pane sizes');
 await page.setViewportSize({width:1600,height:1000});
 }
 await page.getByRole('button',{name:'User settings button',exact:true}).click();
 await page.getByText('Staff',{exact:true}).first().click();
 await page.getByText('Default Village',{exact:true}).click();
 await page.locator('.template-editor').waitFor();
 await page.getByLabel('Workshop village').selectOption(scene.planetId);
 const use=page.getByRole('button',{name:'Use as draft',exact:true});
 if(await use.isEnabled())await use.click();
 await page.getByRole('button',{name:/Edit.*preview draft/}).waitFor();
 await shot('staff-template-draft');
 await page.getByRole('button',{name:/Publish revision/}).click();
 await page.getByText('Published. New villages will start with this world.',{exact:true}).waitFor();
 record('Staff selects a shared draft and publishes the default template through Settings');
 await page.getByRole('button',{name:/Edit.*preview draft/}).click();
 await page.locator('.v-menu-close').waitFor({state:'hidden'});
 await page.locator('.village-canvas').waitFor();
 await page.getByRole('button',{name:'Open build mode',exact:true}).click();
 await page.locator('.village-build-panel').waitFor();
 await shot('staff-draft-builder');
 await page.getByRole('button',{name:'Close build mode',exact:true}).click();
 record('Edit and preview opens the draft in the normal builder and closes the settings overlay');
 await page.getByRole('button',{name:'User settings button',exact:true}).click();
 await page.getByText('Staff',{exact:true}).first().click();
 await page.getByText('Tileset Tool',{exact:true}).click();
 await page.getByRole('button',{name:/Open Tileset Tool/}).click();
 await page.locator('.v-menu-close').waitFor({state:'hidden'});
 await page.locator('.sheet-canvas').waitFor();
 await page.getByLabel('Source sheet',{exact:true}).waitFor();
 const picker=page.getByLabel('Source sheet',{exact:true});
 const options=await picker.locator('option').allTextContents();
 assert.ok(options.length>=17);
 for(const index of [0,1,4,options.findIndex(x=>x==='Office objects')]){
   assert.ok(index>=0);await picker.selectOption(String(index));await page.waitForTimeout(300);await shot(`tileset-source-${index}`);
 }
 await page.getByLabel('Jump to asset',{exact:true}).selectOption('office.desk.birch');
 await page.waitForTimeout(300);
 assert.equal(await picker.locator('option:checked').innerText(),'Office objects');
 await shot('tileset-selected-desk');
 record('Tileset source navigation loads the original sheet and curated furniture sheets');
 await page.getByRole('button',{name:'Build',exact:true}).click();
 await page.getByRole('button',{name:'Atlas',exact:true}).waitFor();
 async function download(name, file) {
   const pending=page.waitForEvent('download'); await page.getByRole('button',{name,exact:true}).click();
   const data=await pending; await data.saveAs(file);
 }
 await download('Atlas', `${output}/roundtrip-atlas.png`);
 await download('Manifest', `${output}/roundtrip.tileset.json`);
 const first=JSON.parse(await page.locator('.json-output').inputValue());
 assert.equal(first.imageSha256, createHash('sha256').update(await readFile(`${output}/roundtrip-atlas.png`)).digest('hex'));
 await page.locator('input[accept=".png,.jpg,.jpeg,.webp"]').setInputFiles(`${output}/roundtrip-atlas.png`);
 await page.waitForTimeout(600);
 await page.locator('input[accept=".json,application/json"]').setInputFiles(`${output}/roundtrip.tileset.json`);
 await page.waitForTimeout(900);
 await page.getByRole('button',{name:'Build',exact:true}).click();
 await page.waitForTimeout(600);
 await download('Atlas', `${output}/roundtrip-again.png`);
 const second=JSON.parse(await page.locator('.json-output').inputValue());
 assert.equal(second.imageSha256, createHash('sha256').update(await readFile(`${output}/roundtrip-again.png`)).digest('hex'));
 assert.deepEqual(second.definitions.map(x=>[x.Key,x.SourceX,x.SourceY,x.Width,x.Height]), first.definitions.map(x=>[x.Key,x.SourceX,x.SourceY,x.Width,x.Height]));
 assert.deepEqual(second.definitions.map(x=>[x.Key,x.FootprintWidth,x.FootprintHeight,x.PlacementLayer,x.SupportsItems]), first.definitions.map(x=>[x.Key,x.FootprintWidth,x.FootprintHeight,x.PlacementLayer,x.SupportsItems]));
 const pngs = await Promise.all([
   resolve(import.meta.dirname, '../../Client/wwwroot/media/villages/library-atlas.png'),
   `${output}/roundtrip-atlas.png`, `${output}/roundtrip-again.png`
 ].map(async path => `data:image/png;base64,${(await readFile(path)).toString('base64')}`));
 assert.equal(await page.evaluate(async sources => {
   const pixels=[];
   for(const src of sources){
     const image=new Image();image.src=src;await image.decode();
     const canvas=document.createElement('canvas');canvas.width=image.width;canvas.height=image.height;
     const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);pixels.push(ctx.getImageData(0,0,image.width,image.height).data);
   }
   return pixels.every(data=>data.length===pixels[0].length&&data.every((value,index)=>value===pixels[0][index]));
 }, pngs), true, 'All exported pixels retain their exact browser-rendered colors and alpha');
 record('Tileset package exports, reimports its packed atlas, and retains every source rectangle');

 assert.deepEqual(errors,[]);
} finally {
 await shot('last-state').catch(()=>{});
 await writeFile(`${output}/results.json`,JSON.stringify({checks,errors,movementCount:moves.length,joinCount:joins.length},null,2));
 await browser.close();
}
