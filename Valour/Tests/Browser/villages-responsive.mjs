import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium, webkit, devices } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use an isolated local QA server.');
assert.ok(process.env.VILLAGE_QA_EMAIL && process.env.VILLAGE_QA_PASSWORD, 'Set local QA credentials.');
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser/responsive');
await mkdir(output, { recursive: true });
const engine = process.env.VILLAGE_QA_BROWSER || 'chromium';
assert.ok(['chromium', 'webkit'].includes(engine));
const browser = await (engine === 'webkit' ? webkit : chromium).launch({ headless: true, executablePath: engine === 'webkit' ? undefined : process.env.BROWSER_EXECUTABLE });
const checks = [], failures = [], geometry = [], errors = [];
let touchMode = false;
const activate = (locator, options) => touchMode ? locator.tap(options) : locator.click(options);
const groups = [
    { name: 'touch', options: { ...devices['iPhone 13'], deviceScaleFactor: 1 }, sizes: [[320,568],[375,667],[390,844],[430,932],[568,320],[667,375],[844,390],[932,430],[768,1024],[1024,768]] },
    { name: 'desktop', options: { viewport: { width: 1440, height: 900 } }, sizes: [[650,800],[850,900],[1280,800],[1440,900],[1920,1080]] }
];
const check = (condition, message) => { if (!condition) { failures.push(message); console.error(`FAIL ${message}`); } };
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
function overlaps(a,b) { return a && b && Math.min(a.x+a.width,b.x+b.width)-Math.max(a.x,b.x)>2 && Math.min(a.y+a.height,b.y+b.height)-Math.max(a.y,b.y)>2; }
async function inspect(page, label, rootSelector, selectors, pairs=[]) {
    const boxes = await page.evaluate(selectors => Object.fromEntries(selectors.map(selector => {
        const el=document.querySelector(selector);
        if (!el || !el.checkVisibility()) return [selector,null];
        const r=el.getBoundingClientRect();
        return [selector,{x:r.x,y:r.y,width:r.width,height:r.height,scrollWidth:el.scrollWidth,clientWidth:el.clientWidth,scrollHeight:el.scrollHeight,clientHeight:el.clientHeight}];
    })), [rootSelector,...selectors]);
    const root=boxes[rootSelector];
    check(root && root.width>200 && root.height>200, `${label}: usable root`);
    check(root.scrollWidth <= root.clientWidth+2, `${label}: horizontal overflow`);
    for (const selector of selectors) {
        const box=boxes[selector]; if(!box) continue;
        check(box.x>=root.x-1 && box.x+box.width<=root.x+root.width+1,`${label}: ${selector} crosses horizontal bounds`);
        if (rootSelector === '.village-window') check(box.y>=root.y-1 && box.y+box.height<=root.y+root.height+1,`${label}: ${selector} crosses vertical bounds`);
    }
    for (const [a,b] of pairs) check(!overlaps(boxes[a],boxes[b]),`${label}: ${a} overlaps ${b}`);
    geometry.push({label,boxes});
    await page.screenshot({path:`${output}/${label}.png`});
    return boxes;
}
async function reachable(locator) {
    await locator.scrollIntoViewIfNeeded();
    await activate(locator, {trial:true});
}
async function openSettings(page, mobile) {
    if(mobile && await page.locator('.sidebar-menu').getAttribute('data-mobile-open') !== 'true') await activate(page.locator('.sidebar-toggle'));
    await activate(page.getByRole('button',{name:'User settings button',exact:true}));
    if(mobile) await activate(page.locator('.v-menu-sidebar-toggle'));
    await activate(page.getByText('Staff',{exact:true}).first());
}
try {
    for(const group of groups) {
        if(process.env.VILLAGE_QA_GROUP && process.env.VILLAGE_QA_GROUP!==group.name)continue;
        const mobile=group.name==='touch';
        touchMode = mobile;
        const context=await browser.newContext(group.options);
        const page=await context.newPage(); page.setDefaultTimeout(15000);
        page.on('pageerror',e=>errors.push(`${group.name}: ${e.message}`));
        try {
            await page.goto(origin);
            await page.locator('input[type=email]').fill(process.env.VILLAGE_QA_EMAIL);
            await page.locator('input[type=password]').fill(process.env.VILLAGE_QA_PASSWORD);
            await activate(page.getByRole('button',{name:'Log in',exact:true}));
            if(mobile) { await page.locator('.sidebar-toggle').waitFor({timeout:90000}); await activate(page.locator('.sidebar-toggle')); }
            await page.getByText(process.env.VILLAGE_QA_PLANET || 'Village Release QA',{exact:false}).first().waitFor({timeout:90000});
            await activate(page.getByText(process.env.VILLAGE_QA_PLANET || 'Village Release QA',{exact:false}).first());
            await page.waitForTimeout(300);
            if(mobile && await page.locator('.sidebar-menu').getAttribute('data-mobile-open') !== 'true') await activate(page.locator('.sidebar-toggle'));
            await activate(page.getByText('Village',{exact:true}).first());
            await page.locator('.village-canvas').waitFor({timeout:60000});
            await page.locator('.hud-chip.live').waitFor({state:'attached'});
            for(const [width,height] of group.sizes) {
                const label=`${group.name}-${width}x${height}`;
                await page.setViewportSize({width,height}); await page.waitForTimeout(200);
                const before=failures.length;
                try {
                    await inspect(page,`${label}-outdoor`,'.village-window',['.village-topbar','.village-world-header','.village-quick-controls','.village-chat-composer'],[['.village-world-header','.village-quick-controls'],['.village-topbar','.village-chat-composer']]);
                    await activate(page.getByRole('button',{name:'Browse village places',exact:true}));
                    await reachable(page.locator('.place-list-item').filter({has:page.getByText('Studio',{exact:true})}));
                    await inspect(page,`${label}-places`,'.village-window',['.village-topbar','.village-places-panel','.village-chat-composer'],[['.village-topbar','.village-places-panel'],['.village-places-panel','.village-chat-composer']]);
                    await activate(page.locator('.place-list-item').filter({has:page.getByText('Town Hall',{exact:true})}));
                    await reachable(page.getByRole('button',{name:'Close place details',exact:true}));
                    await reachable(page.getByRole('button',{name:'Enter',exact:true}));
                    await inspect(page,`${label}-inspector`,'.village-window',['.village-topbar','.village-inspector','.village-chat-composer'],[['.village-topbar','.village-inspector'],['.village-inspector','.village-chat-composer']]);
                    await activate(page.getByRole('button',{name:'Enter',exact:true}));
                    await page.getByRole('button',{name:'Leave building',exact:true}).waitFor(); await page.locator('.hud-chip.live').waitFor({state:'attached'});
                    await inspect(page,`${label}-interior`,'.village-window',['.village-topbar','.village-chat-composer'],[['.village-topbar','.village-chat-composer']]);
                    await activate(page.getByRole('button',{name:'Open build mode',exact:true}));
                    await activate(page.locator('.build-tool-row').getByRole('button',{name:'Furnish',exact:true}));
                    await page.getByRole('textbox',{name:'Search build catalog'}).fill('Glass cabinet');
                    const cabinet=page.locator('.build-catalog-item').filter({hasText:'Glass cabinet'});
                    await activate(cabinet);
                    check((await cabinet.getAttribute('class')).includes('active'),`${label}: furniture selected`);
                    const build=await inspect(page,`${label}-build`,'.village-window',['.village-canvas','.village-topbar','.village-build-panel','.build-catalog','.build-panel-footer'],[['.village-topbar','.village-build-panel'],['.village-canvas','.village-build-panel']]);
                    check(build['.build-catalog'].height>=100,`${label}: furniture catalog is too short`);
                    check(build['.village-canvas'].height>=140,`${label}: room remains visible while furnishing`);
                    await page.getByRole('textbox',{name:'Search build catalog'}).fill('');
                    await activate(page.locator('.build-categories').getByRole('button',{name:'Recreation',exact:true}));
                    check(await page.locator('.build-catalog-item').count()>0,`${label}: final catalog category is reachable`);
                    await activate(page.locator('.build-categories').getByRole('button',{name:'All',exact:true}));
                    for(const name of ['Paint','Walls','Pan','Move','Erase']) await activate(page.locator('.build-tool-row').getByRole('button',{name,exact:true}));
                    await activate(page.getByRole('button',{name:'Close build mode',exact:true}));
                    await activate(page.getByRole('button',{name:'Leave building',exact:true}));
                    await page.getByRole('button',{name:'Leave building',exact:true}).waitFor({state:'hidden'});
                    if(mobile) {
                        await activate(page.getByRole('button',{name:'Switch to handheld controls',exact:true}));
                        for(const name of ['Move left','Interact with nearby place','Back or leave building','Focus nearby chat']) await reachable(page.getByRole('button',{name,exact:true}));
                        await inspect(page,`${label}-handheld`,'.village-window',['.village-topbar','.village-handheld','.village-chat-composer'],[['.village-topbar','.village-chat-composer'],['.village-topbar','.village-handheld'],['.village-handheld','.village-chat-composer']]);
                        await activate(page.getByRole('button',{name:'Open or close Places',exact:true}));
                        await reachable(page.getByRole('button',{name:'Close places',exact:true}));
                        await inspect(page,`${label}-handheld-places`,'.village-window',['.village-topbar','.village-places-panel','.village-handheld','.village-chat-composer'],[['.village-topbar','.village-places-panel'],['.village-places-panel','.village-handheld'],['.village-places-panel','.village-chat-composer']]);
                        await activate(page.getByRole('button',{name:'Close places',exact:true}));
                        await activate(page.getByRole('button',{name:'Switch to floating joystick controls',exact:true}));
                    }
                    if(failures.length===before)record(`${label}: navigation, entry/exit, all build tools and catalog, overlay layout`);
                } catch(error) {
                    failures.push(`${label}: ${error.message.split('\n')[0]}`);console.error(failures.at(-1));
                    await page.screenshot({path:`${output}/${label}-failure.png`});
                    await page.setViewportSize({width:mobile?390:1440,height:mobile?844:900});
                    const close=page.getByRole('button',{name:'Close build mode',exact:true});if(await close.count())await activate(close);
                    const leave=page.getByRole('button',{name:'Leave building',exact:true});if(await leave.count())await activate(leave);
                }
            }
            await page.setViewportSize({width:mobile?390:1440,height:mobile?844:900});
            await openSettings(page,mobile);
            await activate(page.getByText('Default Village',{exact:true}));
            await page.locator('.template-editor').waitFor();
            for(const [width,height] of group.sizes) {
                const label=`${group.name}-${width}x${height}-template`, before=failures.length;
                await page.setViewportSize({width,height});
                await reachable(page.getByRole('button',{name:/Edit.*preview draft/}));
                await inspect(page,label,'.template-editor',['.template-publication','.template-section']);
                await reachable(page.getByRole('button',{name:/Publish revision/}));
                if(failures.length===before)record(`${label}: draft and publication controls fit and remain reachable`);
            }
            await page.setViewportSize({width:mobile?390:1440,height:mobile?844:900});
            if(mobile)await activate(page.locator('.v-menu-sidebar-toggle'));
            await activate(page.getByText('Tileset Tool',{exact:true}));
            await activate(page.getByRole('button',{name:/Open Tileset Tool/}));
            await page.locator('.v-menu-close').waitFor({state:'hidden'});
            await page.locator('.sheet-canvas').waitFor();
            for(const [width,height]of group.sizes) {
                const label=`${group.name}-${width}x${height}-tileset`, before=failures.length;
                await page.setViewportSize({width,height});await page.waitForTimeout(150);
                try {
                    await page.getByLabel('Jump to asset',{exact:true}).selectOption('living.cabinet.glass');
                    await page.waitForFunction(() => document.querySelector('.source-sheet-picker option:checked')?.textContent === 'Living objects');
                    await reachable(page.getByLabel('Source sheet',{exact:true}));
                    await activate(page.getByRole('button',{name:'Fit sheet',exact:true}));
                    await page.locator('.sheet-canvas').scrollIntoViewIfNeeded();
                    await inspect(page,label,'.tileset-tool',['.tool-topbar','.tool-load-controls','.tool-body','.sheet-stage','.definition-panel','.package-panel']);
                    await reachable(page.getByRole('button',{name:'Save',exact:true}));
                    await activate(page.getByRole('button',{name:'Terrains',exact:true}));
                    await reachable(page.getByRole('button',{name:'Add Terrain',exact:true}));
                    await activate(page.getByRole('button',{name:'Brushes',exact:true}));
                    await reachable(page.getByRole('button',{name:'Save Brush',exact:true}));
                    await activate(page.getByRole('button',{name:'Tiles',exact:true}));
                    await reachable(page.getByRole('button',{name:'Build',exact:true}));
                    await page.screenshot({path:`${output}/${label}-export.png`});
                    if(failures.length===before)record(`${label}: source selection, definition/terrain/brush forms, package controls`);
                } catch(error) { failures.push(`${label}: ${error.message.split('\n')[0]}`);console.error(failures.at(-1));await page.screenshot({path:`${output}/${label}-failure.png`}); }
            }
        } finally { await context.close(); }
    }
} finally {
    await writeFile(`${output}/results.json`,JSON.stringify({engine,checks,failures,errors,geometry},null,2));
    await browser.close();
}
for(const failure of failures)console.error(`FAIL ${failure}`);
assert.deepEqual(failures,[]);
assert.deepEqual(errors,[]);
