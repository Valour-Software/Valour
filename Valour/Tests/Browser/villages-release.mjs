import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Run this mutating suite against a local QA server.');
const email = process.env.VILLAGE_QA_EMAIL;
const password = process.env.VILLAGE_QA_PASSWORD;
const planetName = process.env.VILLAGE_QA_PLANET || 'Village Release QA';
assert.ok(email && password, 'Set VILLAGE_QA_EMAIL and VILLAGE_QA_PASSWORD for a local account that manages the QA village.');
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const context = await browser.newContext({ viewport: { width: 1440, height: 960 } });
const page = await context.newPage();
page.setDefaultTimeout(15000);
const errors = [], failedResources = [], checks = [], cleanup = new Map(), originalObjectIds = new Set();
const originalFloors = new Map();
let buildRequests = 0;
let cameraTile = null;
page.on('websocket', socket => socket.on('framesent', frame => {
    if (typeof frame.payload !== 'string') return;
    for (const part of frame.payload.split('\x1e').filter(Boolean)) {
        try {
            const event = JSON.parse(part);
            if (['JoinVillageMap', 'MoveInVillage'].includes(event.target)) cameraTile = {x:event.arguments[2], y:event.arguments[3]};
        } catch {}
    }
}));
let scene, sceneUrl, headers, activeMap, zoom = 1, pan = { x: 0, y: 0 };
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (request.method() === 'PUT' && request.url().endsWith('/build')) buildRequests++; });
page.on('response', async response => {
    if (response.status() >= 400 && !response.url().endsWith('/api/federation/passport'))
        failedResources.push({ status: response.status(), url: response.url() });
    if (response.url().includes('/village/poc') && response.ok()) {
        scene = await response.json();
        sceneUrl = response.url();
        const requestHeaders = await response.request().allHeaders();
        headers = Object.fromEntries(Object.entries(requestHeaders).filter(([key]) => ['authorization', 'cookie'].includes(key)));
    }
});
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
const screenshot = name => page.screenshot({ path: `${output}/${name}.png` });
async function openVillage() {
    await page.getByText(planetName, { exact: false }).first().waitFor({ timeout: 90000 });
    await page.getByText(planetName, { exact: false }).first().click();
    await page.getByText('Village', { exact: true }).first().click();
    await page.locator('.village-canvas').waitFor({ timeout: 60000 });
    await page.getByRole('button', { name: 'Browse village places' }).waitFor();
    activeMap = scene.maps.find(map => map.id === scene.startingMapId);
    for (const map of scene.maps) for (const item of map.decorations) originalObjectIds.add(item.id);
    for (const map of scene.maps) originalFloors.set(map.id, structuredClone(map.groundTiles));
    zoom = 1; pan = { x: 0, y: 0 };
}
async function enter(name) {
    await page.getByRole('button', { name: 'Browse village places' }).click();
    await page.locator('.place-list-item').filter({ has: page.getByText(name, { exact: true }) }).click();
    await page.getByRole('button', { name: 'Enter', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor();
    activeMap = scene.maps.find(map => map.id === scene.maps.flatMap(map => map.buildings).find(building => building.name === name).interiorMapId);
    pan = { x: 0, y: 0 };
    await page.locator('.village-places-panel').waitFor({ state: 'hidden' });
    await page.waitForTimeout(400);
}
async function tilePoint(x, y) {
    const box = await page.locator('.village-canvas').boundingBox();
    const scale = box.width < 760 ? (activeMap.mapKind === 'Interior' ? 2 : 1) : (activeMap.mapKind === 'Interior' ? 3 : 2);
    zoom = Number.parseInt(await page.locator('.village-zoom-level').innerText()) / 100;
    const px = activeMap.tileSize * scale * zoom;
    const center = cameraTile ?? activeMap.spawnTile;
    return { x: box.x + box.width / 2 + (x - center.x) * px + pan.x,
        y: box.y + box.height / 2 + (y - center.y) * px + pan.y };
}
async function clickTile(x, y) {
    const point = await tilePoint(x, y);
    await page.mouse.click(point.x, point.y);
}
async function dragTiles(from, to, shift = false) {
    const a = await tilePoint(...from), b = await tilePoint(...to);
    await page.mouse.move(a.x, a.y);
    if (shift) await page.keyboard.down('Shift');
    await page.mouse.down();
    await page.mouse.move(b.x, b.y, { steps: 12 });
    await page.mouse.up();
    if (shift) await page.keyboard.up('Shift');
}
async function tool(name) {
    await page.locator('.build-tool-row').getByRole('button', { name, exact: true }).click();
}
async function choose(name) {
    await page.getByRole('textbox', { name: 'Search build catalog' }).fill(name);
    await page.locator('.build-catalog-item').filter({ has: page.locator('.build-item-name', { hasText: new RegExp(`^${name}$`) }) }).first().click();
}
async function build(action) {
    const response = page.waitForResponse(r => r.url().endsWith(`/maps/${activeMap.id}/build`) && r.request().method() === 'PUT');
    await action();
    const result = await response;
    assert.ok(result.ok(), `Build rejected: ${result.status()} ${await result.text()}`);
    const body = await result.json();
    await page.waitForTimeout(350);
    return body;
}
function remember(result) {
    for (const item of result.decorations ?? []) {
        if (item.zIndex > -100 && !originalObjectIds.has(item.id)) cleanup.set(item.id, { mapId: activeMap.id, id: item.id });
    }
    if (result.decoration?.zIndex > -100 && !originalObjectIds.has(result.decoration.id))
        cleanup.set(result.decoration.id, { mapId: activeMap.id, id: result.decoration.id });
    for (const id of result.removedObjectIds ?? []) cleanup.delete(id);
}
async function fetchScene() {
    const response = await context.request.get(sceneUrl, { headers });
    assert.ok(response.ok());
    return response.json();
}
try {
    for (let attempt = 0; attempt < 30; attempt++) {
        if (await fetch(origin).then(response => response.ok).catch(() => false)) break;
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    await page.goto(origin);
    await page.getByRole('button', { name: 'Log in', exact: true }).waitFor({ timeout: 90000 });
    await page.locator('input[type=email]').fill(email);
    await page.locator('input[type=password]').fill(password);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    await openVillage();
    await screenshot('outdoor');
    record('Login, planet navigation, outdoor rendering, and Places');
    await enter('Town Hall');
    assert.equal(await page.locator('.village-place-inspector').count(), 0);
    await page.getByRole('button', { name: 'Browse village places' }).click();
    assert.ok(await page.locator('.place-list-item').filter({ hasText: 'Room settings' }).isVisible());
    await page.getByRole('button', { name: 'Close places', exact: true }).click();
    record('Enter furnished office and find room settings');
    while (await page.getByRole('button', { name: 'Zoom out', exact: true }).isEnabled()) {
        await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
        await page.waitForTimeout(300);
    }
    zoom = 0.5;
    await page.getByRole('button', { name: 'Open build mode', exact: true }).click();
    await page.waitForTimeout(600);
    for (const category of ['Kitchen', 'Bathroom', 'Studio', 'Music', 'Recreation'])
        assert.ok(await page.locator('.build-categories').getByRole('button', { name: category, exact: true }).isVisible());
    for (const [name, key] of [
        ['Glass cabinet', 'living.cabinet.glass'], ['Oak bookcase', 'living.bookcase'],
        ['Birch bookcase', 'living.bookcase.wide'], ['Oak desk', 'office.desk.oak'],
        ['Birch desk', 'office.desk.birch'], ['Walnut desk', 'office.desk.walnut'],
        ['Steel refrigerator', 'kitchen.fridge.steel'], ['Studio drum kit', 'music.drums']
    ]) {
        await tool('Furnish'); await choose(name);
        const item = await build(() => clickTile(3, 12)); remember(item);
        assert.equal(item.decoration.definitionKey, key);
        assert.ok((await fetchScene()).maps.find(map => map.id === activeMap.id).decorations.some(d => d.id === item.decoration.id));
        await screenshot(`placed-${key}`);
        await tool('Erase'); remember(await build(() => clickTile(3, 12)));
    }
    record('Place, persist, and erase repaired cabinets, both bookcases, all desks, a refrigerator, and the complete drum kit');
    await tool('Walls');
    await choose('Green wainscot');
    const walls = await build(() => dragTiles([2, 12], [6, 14], true));
    remember(walls);
    const room = walls.decorations.filter(item => item.definitionKey.startsWith('wall:modern.green:') &&
        item.x >= 2 && item.x <= 6 && item.y >= 12 && item.y <= 14);
    assert.equal(room.length, 12, 'Shift walls creates the perimeter of a 5 × 3 room');
    assert.ok(!room.some(item => item.x === 4 && item.y === 13), 'Room center stays open');
    record('Draw a room with connected corners and an open center');
    await screenshot('room-walls');
    await tool('Erase');
    remember(await build(() => clickTile(4, 14)));
    await tool('Furnish');
    await choose('Wooden chair facing right');
    const chair = await build(() => clickTile(3, 13));
    remember(chair);
    assert.equal(chair.decoration.definitionKey, 'office.chair.dark');
    await tool('Move');
    await clickTile(3, 13);
    const moved = await build(() => clickTile(4, 13));
    remember(moved);
    assert.equal(moved.decoration.id, chair.decoration.id);
    assert.equal(moved.decoration.x, 4);
    record('Erase a doorway, search furniture, place it, and move it');
    await tool('Paint');
    await choose('Oak planks');
    const painted = await build(() => dragTiles([3, 13], [5, 13], true));
    assert.ok(painted.decorations.some(item => item.definitionKey === 'floor.oak'));
    const fresh = (await fetchScene()).maps.find(map => map.id === activeMap.id);
    assert.ok(fresh.decorations.some(item => item.id === chair.decoration.id && item.x === 4));
    assert.ok(!fresh.decorations.some(item => item.kind === 'Wall' && item.x === 4 && item.y === 14));
    record('Paint beneath furniture and verify authoritative persistence');
    await tool('Pan');
    const before = buildRequests;
    const box = await page.locator('.village-canvas').boundingBox();
    await page.mouse.move(box.x + 450, box.y + 280);
    await page.mouse.down();
    await page.mouse.move(box.x + 450, box.y + 480, { steps: 15 });
    await page.mouse.up();
    pan.y += 200;
    await page.waitForTimeout(350);
    assert.equal(buildRequests, before, 'Panning never submits a build request');
    await screenshot('office-overview');
    record('Pan to inspect the complete office');
    await page.getByRole('button', { name: 'Close build mode', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor({ state: 'hidden' });
    await enter('Maker House');
    await page.waitForTimeout(400);
    await screenshot('house');
    record('Return outdoors and enter a furnished house');
    await page.getByRole('button', { name: 'Leave building', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor({ state: 'hidden' });
    activeMap = scene.maps.find(map => map.mapKind === 'Outdoor');
    pan = {x:0,y:0};
    await page.getByRole('button', { name: 'Open build mode', exact: true }).click();
    await tool('Furnish');
    await choose('Apartment Small Brown');
    await tool('Pan');
    const panBox = await page.locator('.village-canvas').boundingBox();
    await page.mouse.move(panBox.x + 420, panBox.y + 450);
    await page.mouse.down(); await page.mouse.move(panBox.x + 170, panBox.y + 450, {steps:10}); await page.mouse.up();
    pan.x -= 250;
    await tool('Furnish');
    const building = await build(() => clickTile(18, 31));
    assert.ok(building.sceneChanged && building.buildingId && building.interiorMapId);
    cleanup.set(building.buildingId, {mapId:activeMap.id,id:building.buildingId});
    scene = await fetchScene();
    const newHome = scene.maps.find(map => map.id === building.interiorMapId);
    assert.ok(newHome?.decorations.some(item => item.definitionKey === 'bedroom.bed.blue'));
    assert.ok(newHome?.decorations.some(item => item.definitionKey.startsWith('wall:modern.cream:')));
    assert.ok(newHome?.groundTiles.some(item => item.definitionKey === 'floor.oak'));
    await page.getByRole('button', { name: 'Close build mode', exact: true }).click();
    await screenshot('new-building');
    await enter('Apartment Small Brown');
    assert.equal(activeMap.id, building.interiorMapId);
    await screenshot('new-house-interior');
    await page.getByRole('button', { name: 'Leave building', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor({ state: 'hidden' });
    record('Place an outdoor building and enter its newly seeded, persistent home');
    assert.deepEqual(errors, [], 'No uncaught browser errors');
    assert.deepEqual(failedResources, [], 'No failed app or art requests');
} catch (error) {
    await screenshot('failure').catch(() => {});
    await writeFile(`${output}/failure.txt`, `${error.stack}\n${(await page.locator('body').first().innerText().catch(() => '')).slice(-10000)}`);
    throw error;
} finally {
    if (sceneUrl && headers) {
        for (const item of [...cleanup.values()].reverse()) {
            const url = sceneUrl.replace(/poc(?:\?.*)?$/, `maps/${item.mapId}/build`);
            const response = await context.request.put(url, { headers, data: { action: 2, objectId: item.id } });
            if (!response.ok()) console.error(`Cleanup failed for QA object ${item.id}: ${response.status()}`);
        }
        const office = scene.maps.find(map => map.name === 'Town Hall Interior');
        if (office) {
            for (const x of [3, 4, 5]) {
                const original = originalFloors.get(office.id)?.find(item => item.x === x && item.y === 13 && item.zIndex <= -100);
                if (original) await context.request.put(sceneUrl.replace(/poc(?:\?.*)?$/, `maps/${office.id}/build`), {
                    headers, data: { action: 0, definitionKey: original.definitionKey, x, y: 13 }
                });
            }
        }
    }
    await writeFile(`${output}/results.json`, JSON.stringify({ checks, errors, failedResources }, null, 2));
    await browser.close();
}
