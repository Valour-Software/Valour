import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium, webkit, devices } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use a local QA server.');
assert.ok(process.env.VILLAGE_QA_EMAIL && process.env.VILLAGE_QA_PASSWORD, 'Set local QA credentials.');
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser');
await mkdir(output, { recursive: true });
const engine = process.env.VILLAGE_QA_BROWSER || 'chromium';
assert.ok(['chromium', 'webkit'].includes(engine));
const browser = await (engine === 'webkit' ? webkit : chromium).launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: engine === 'webkit' ? undefined : process.env.BROWSER_EXECUTABLE } : {}) });
const context = await browser.newContext({ ...devices['iPhone 13'] });
const page = await context.newPage();
page.setDefaultTimeout(20000);
let scene, sceneUrl, headers, placed;
const errors = [], moves = [], checks = [];
page.on('pageerror', error => errors.push(error.message));
page.on('websocket', ws => ws.on('framesent', frame => {
    if (String(frame.payload).includes('MoveInVillage')) moves.push(String(frame.payload));
}));
page.on('response', async response => {
    if (response.url().includes('/village/poc') && response.ok()) {
        scene = await response.json(); sceneUrl = response.url();
        const all = await response.request().allHeaders();
        headers = Object.fromEntries(Object.entries(all).filter(([key]) => ['authorization', 'cookie'].includes(key)));
    }
});
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
try {
    await page.goto(origin);
    await page.getByRole('button', { name: 'Log in', exact: true }).waitFor({ timeout: 90000 });
    await page.locator('input[type=email]').fill(process.env.VILLAGE_QA_EMAIL);
    await page.locator('input[type=password]').fill(process.env.VILLAGE_QA_PASSWORD);
    await page.getByRole('button', { name: 'Log in', exact: true }).tap();
    await page.locator('.sidebar-toggle').waitFor({ timeout: 90000 });
    await page.locator('.sidebar-toggle').tap();
    await page.getByText(process.env.VILLAGE_QA_PLANET || 'Village Release QA', { exact: false }).first().tap();
    await page.waitForTimeout(500);
    if (await page.locator('.sidebar-menu').getAttribute('data-mobile-open') !== 'true') await page.locator('.sidebar-toggle').tap();
    await page.getByText('Village', { exact: true }).first().tap();
    await page.locator('.village-canvas').waitFor({ timeout: 60000 });
    assert.ok((await page.locator('.village-canvas').boundingBox()).width >= 380);
    await page.screenshot({ path: `${output}/mobile-outdoor.png` });
    record('Mobile navigation opens the village across the full screen');
    await page.getByRole('button', { name: 'Switch to handheld controls', exact: true }).tap();
    await page.getByRole('group', { name: 'Handheld village controls', exact: true }).waitFor();
    const before = moves.length;
    await page.getByRole('button', { name: 'Move left', exact: true }).tap();
    await page.waitForTimeout(600);
    assert.ok(moves.length > before, 'The D-pad sends a real movement update');
    await page.screenshot({ path: `${output}/mobile-handheld.png` });
    record('Handheld D-pad moves the avatar and reports presence');
    await page.getByRole('button', { name: 'Browse village places', exact: true }).tap();
    await page.locator('.place-list-item').filter({ has: page.getByText('Town Hall', { exact: true }) }).tap();
    await page.getByRole('button', { name: 'Enter', exact: true }).tap();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Open build mode', exact: true }).tap();
    assert.equal(await page.getByRole('group', { name: 'Handheld village controls', exact: true }).count(), 0);
    await page.locator('.build-tool-row').getByRole('button', { name: 'Furnish', exact: true }).tap();
    await page.getByRole('textbox', { name: 'Search build catalog' }).fill('Potted plant');
    await page.locator('.build-catalog-item').filter({ hasText: 'Potted plant' }).tap();
    const office = scene.maps.find(map => map.name === 'Town Hall Interior');
    const box = await page.locator('.village-canvas').boundingBox();
    const px = office.tileSize * 2 * (Number.parseInt(await page.locator(".village-zoom-level").innerText()) / 100);
    const response = page.waitForResponse(r => r.url().endsWith(`/maps/${office.id}/build`) && r.request().method() === 'PUT');
    await page.touchscreen.tap(box.x + box.width / 2 - px, box.y + box.height / 2 - 2 * px);
    const built = await response;
    assert.ok(built.ok());
    placed = { ...(await built.json()).decoration, mapId: office.id };
    assert.equal(placed.definitionKey, 'decor.indoor-plant');
    assert.equal(placed.x, office.spawnTile.x - 1);
    assert.equal(placed.y, office.spawnTile.y - 2);
    await page.getByText('Potted plant placed.', {exact:true}).waitFor();
    await page.screenshot({ path: `${output}/mobile-building.png` });
    record('Touch catalog search and furniture placement work inside a room');
    await page.getByRole('button', { name: 'Close build mode', exact: true }).tap();
    await page.getByRole('button', { name: 'Back or leave building', exact: true }).tap();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor({ state: 'hidden' });
    await page.getByRole('button', { name: 'Switch to floating joystick controls', exact: true }).tap();
    assert.equal(await page.getByRole('group', { name: 'Handheld village controls', exact: true }).count(), 0);
    record('Handheld back exits the building and controls switch to the joystick');
    assert.deepEqual(errors, []);
} catch (error) {
    await page.screenshot({ path: `${output}/mobile-failure.png` }).catch(() => {});
    throw error;
} finally {
    if (placed) {
        const response = await context.request.put(sceneUrl.replace(/poc(?:\?.*)?$/, `maps/${placed.mapId}/build`), {
            headers, data: { action: 2, objectId: placed.id }
        });
        assert.ok(response.ok(), 'Clean up the mobile QA furnishing');
    }
    await writeFile(`${output}/mobile-results.json`, JSON.stringify({ engine, checks, errors }, null, 2));
    await browser.close();
}
