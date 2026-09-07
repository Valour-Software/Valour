import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use a local QA server.');
const planet = process.env.VILLAGE_QA_PLANET || 'Village Release QA';
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const errors = [], checks = [];
const participants = [];
async function join(email, password) {
    assert.ok(email && password, 'Set owner and guest QA credentials; both must belong to the local QA planet.');
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    page.setDefaultTimeout(20000);
    await page.addInitScript(() => {
        window.__villageDrawnText = new Set();
        const fillText = CanvasRenderingContext2D.prototype.fillText;
        CanvasRenderingContext2D.prototype.fillText = function(text, ...args) {
            if (this.canvas.classList.contains('village-canvas')) window.__villageDrawnText.add(String(text));
            return fillText.call(this, text, ...args);
        };
    });
    page.on('pageerror', error => errors.push(error.message));
    const sent = [], received = [];
    page.on('websocket', ws => {
        ws.on('framesent', frame => { if (String(frame.payload).includes('MoveInVillage')) sent.push(String(frame.payload)); });
        ws.on('framereceived', frame => { if (String(frame.payload).includes('Village-Presence-Moved')) received.push(String(frame.payload)); });
    });
    let headers, sceneUrl, scene;
    page.on('response', async response => {
        if (response.url().includes('/village/poc') && response.ok()) {
            sceneUrl = response.url();
            scene = await response.json();
            const all = await response.request().allHeaders();
            headers = Object.fromEntries(Object.entries(all).filter(([key]) => ['authorization', 'cookie'].includes(key)));
        }
    });
    await page.goto(origin);
    await page.getByRole('button', { name: 'Log in', exact: true }).waitFor({ timeout: 90000 });
    await page.locator('input[type=email]').fill(email);
    await page.locator('input[type=password]').fill(password);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    await page.getByText(planet, { exact: false }).first().waitFor({ timeout: 90000 });
    await page.getByText(planet, { exact: false }).first().click();
    await page.getByText('Village', { exact: true }).first().click();
    await page.locator('.village-canvas').waitFor({ timeout: 60000 });
    const result = { context, page, sent, received, get name() { return scene?.characters.find(character => character.isLocalPlayer)?.name; }, get headers() { return headers; }, get sceneUrl() { return sceneUrl; } };
    participants.push(result);
    return result;
}
async function enter(page, name) {
    await page.getByRole('button', { name: 'Browse village places' }).click();
    await page.locator('.place-list-item').filter({ has: page.getByText(name, { exact: true }) }).click();
    await page.getByRole('button', { name: 'Enter', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor();
}
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
try {
    const owner = await join(process.env.VILLAGE_QA_EMAIL, process.env.VILLAGE_QA_PASSWORD);
    const guest = await join(process.env.VILLAGE_QA_GUEST_EMAIL, process.env.VILLAGE_QA_GUEST_PASSWORD);
    await owner.page.getByText('2 here', { exact: true }).waitFor();
    await guest.page.getByText('2 here', { exact: true }).waitFor();
    await guest.page.waitForFunction(name => window.__villageDrawnText.has(name), owner.name);
    record('Two independent accounts discover and render each other outdoors');
    await owner.page.bringToFront();
    await owner.page.locator('.village-canvas').focus();
    await owner.page.keyboard.press('ArrowLeft');
    await owner.page.waitForTimeout(700);
    assert.ok(owner.sent.some(frame => frame.includes('MoveInVillage')), 'Keyboard movement reaches the server');
    assert.ok(guest.received.some(frame => frame.includes('Village-Presence-Moved')), 'The guest receives movement');
    await owner.page.getByPlaceholder('Say something nearby…').fill('Villages QA: nearby chat works.');
    const send = owner.page.waitForResponse(response => response.request().method() === 'POST' && response.url().includes('/messages'));
    await owner.page.getByPlaceholder('Say something nearby…').press('Enter');
    assert.ok((await send).ok(), 'Nearby chat is accepted by the server');
    await owner.page.waitForTimeout(700);
    await guest.page.bringToFront();
    await guest.page.waitForTimeout(250);
    await guest.page.waitForFunction(() => [...window.__villageDrawnText].some(text => text.includes('nearby chat works')));
    await guest.page.screenshot({ path: `${output}/shared-chat.png` });
    record('Walk and send nearby chat with a second connected client');
    await enter(owner.page, 'Town Hall');
    await owner.page.getByText('1 here', { exact: true }).waitFor();
    await guest.page.getByText('1 here', { exact: true }).waitFor();
    await enter(guest.page, 'Town Hall');
    await owner.page.getByText('2 here', { exact: true }).waitFor();
    await guest.page.getByText('2 here', { exact: true }).waitFor();
    record('Presence leaves the outdoor group and joins the interior group');
    assert.equal(await guest.page.getByRole('button', { name: 'Open build mode', exact: true }).count(), 0);
    const guestScene = await (await guest.context.request.get(guest.sceneUrl, { headers: guest.headers })).json();
    const office = guestScene.maps.find(map => map.name === 'Town Hall Interior');
    const denied = await guest.context.request.put(guest.sceneUrl.replace(/poc(?:\?.*)?$/, `maps/${office.id}/build`), {
        headers: guest.headers, data: { action: 1, definitionKey: 'office.chair', x: 7, y: 9 }
    });
    assert.ok([400, 403].includes(denied.status()), 'A guest cannot bypass the editor permissions with a direct request');
    record('Guest UI and server both enforce room edit permissions');
    const rolesUrl = `${origin}/api/planet/${guestScene.planetId}/roles`;
    const temporaryRoles = new Set();
    try {
        const createRole = async name => {
            const response = await owner.context.request.post(rolesUrl, {
                headers: owner.headers,
                data: { planetId: guestScene.planetId, name, permissions: 0x800000, chatPermissions: 0, voicePermissions: 0, categoryPermissions: 0 }
            });
            assert.ok(response.ok(), 'The owner creates a temporary village manager role');
            const role = JSON.parse(await response.text(), (key, value, context) =>
                key === 'id' && typeof value === 'number' ? context.source : value);
            temporaryRoles.add(role.id);
            return role;
        };
        const role = await createRole('Village QA temporary manager');
        const assigned = await owner.context.request.post(
            `${origin}/api/planets/${guestScene.planetId}/members/${guestScene.localMemberId}/roles/${role.id}`,
            { headers: owner.headers });
        assert.ok(assigned.ok(), `The owner grants the guest village management (${assigned.status()}: ${await assigned.text()})`);
        await guest.page.getByRole('button', { name: 'Open build mode', exact: true }).waitFor();
        await guest.page.getByRole('button', { name: 'Open build mode', exact: true }).click();
        const deleted = await owner.context.request.delete(`${rolesUrl}/${role.id}`, { headers: owner.headers });
        assert.ok(deleted.ok(), 'The owner deletes the assigned role');
        temporaryRoles.delete(role.id);
        await guest.page.getByRole('button', { name: 'Close build mode', exact: true }).waitFor({ state: 'hidden' });
        await guest.page.getByRole('button', { name: 'Open build mode', exact: true }).waitFor({ state: 'hidden' });
        const replacement = await createRole('Village QA replacement manager');
        assert.equal(replacement.flagBitIndex, role.flagBitIndex, 'The replacement reuses the deleted role bit');
        const afterReuse = await (await guest.context.request.get(guest.sceneUrl, { headers: guest.headers })).json();
        assert.equal(afterReuse.canManageVillage, false, 'A recycled role bit grants no permission to the former member');
        const rejected = await guest.context.request.put(guest.sceneUrl.replace(/poc(?:\?.*)?$/, `maps/${office.id}/build`), {
            headers: guest.headers, data: { action: 1, definitionKey: 'office.chair', x: 7, y: 9 }
        });
        assert.ok([400, 403].includes(rejected.status()), 'Editing stays forbidden after role deletion and reuse');
        record('Role grants update the open editor; deleting and reusing the role revokes access immediately');
    } finally {
        for (const id of temporaryRoles) {
            const response = await owner.context.request.delete(`${rolesUrl}/${id}`, { headers: owner.headers });
            assert.ok(response.ok() || response.status() === 404, 'Temporary QA roles are removed');
        }
    }
    await owner.page.getByRole('button', { name: 'Open build mode', exact: true }).click();
    await owner.page.locator('.build-tool-row').getByRole('button', { name: 'Furnish', exact: true }).click();
    await owner.page.getByRole('textbox', { name: 'Search build catalog' }).fill('Potted plant');
    await owner.page.locator('.build-catalog-item').filter({ hasText: 'Potted plant' }).click();
    await owner.page.keyboard.press('Escape');
    await owner.page.getByRole('button', { name: 'Close build mode', exact: true }).click();
    await guest.context.close();
    await owner.page.getByText('1 here', { exact: true }).waitFor({ timeout: 30000 });
    record('Disconnect removes the remote participant');
    assert.deepEqual(errors, []);
} catch (error) {
    for (const [index, participant] of participants.entries())
        await participant.page.screenshot({ path: `${output}/presence-failure-${index}.png` }).catch(() => {});
    throw error;
} finally {
    await writeFile(`${output}/presence-results.json`, JSON.stringify({ checks, errors }, null, 2));
    await browser.close();
}
