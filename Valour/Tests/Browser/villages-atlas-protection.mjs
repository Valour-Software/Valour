import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';

const engines = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const engine = process.env.BROWSER_ENGINE || 'chromium';
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const output = resolve(process.env.VILLAGE_QA_OUTPUT || `TestResults/village-browser/atlas-protection-${engine}`);
await mkdir(output, { recursive: true });
const browser = await engines[engine].launch({ headless: true, ...(engine === 'chromium' && process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
let context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
let page = await context.newPage();
page.setDefaultTimeout(30000);
const checks = [], errors = [];
page.on('pageerror', error => errors.push(error.message));
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
async function openVillage(mobile = false) {
    page.setDefaultTimeout(30000);
    await page.goto(origin);
    await page.locator('input[type=email]').fill(process.env.VILLAGE_QA_EMAIL);
    await page.locator('input[type=password]').fill(process.env.VILLAGE_QA_PASSWORD);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    if (mobile) {
        await page.locator('.sidebar-toggle').waitFor({ timeout: 90000 });
        await page.locator('.sidebar-toggle').tap();
    }
    await page.getByText('Village Release QA', { exact: false }).first().waitFor({ timeout: 90000 });
    await page.getByText('Village Release QA', { exact: false }).first().click();
    if (mobile) {
        await page.waitForTimeout(500);
        if (await page.locator('.sidebar-menu').getAttribute('data-mobile-open') !== 'true')
            await page.locator('.sidebar-toggle').tap();
    }
    await page.getByText('Village', { exact: true }).first().click();
    await page.locator('.village-canvas').waitFor({ timeout: 60000 });
    await page.locator('.hud-chip.live').waitFor();
}
async function previewCheck() {
    const urls = await page.locator('.build-item-sprite, .wall-set-preview').evaluateAll(elements => elements
        .map(element => getComputedStyle(element).backgroundImage).filter(value => value !== 'none'));
    assert.ok(urls.length > 0);
    assert.ok(urls.every(url => url.includes('blob:')), `Unresolved build preview: ${urls[0]}`);
    await page.evaluate(async url => {
        const image = new Image(); image.src = url.slice(5, -2); await image.decode();
        if (image.width !== 2048 || image.height !== 1024) throw new Error('Incorrect preview atlas dimensions.');
    }, urls[0]);
}
try {
    const manifestResponse = await page.request.get(`${origin}/_content/Valour.Client/tilesets/exterior-tileset-0.json`);
    assert.ok(manifestResponse.ok());
    const manifest = await manifestResponse.json();
    assert.match(manifest.image, /\.vtex\.bin\?v=/);
    assert.ok(manifest.wallSets.every(wall => wall.Image === manifest.image));
    const asset = await page.request.get(origin + manifest.image);
    assert.ok(asset.ok());
    const bytes = await asset.body();
    assert.equal(bytes.subarray(0, 8).toString(), 'VLTEX001');
    assert.equal(asset.headers()['content-type'], 'application/octet-stream');
    const raw = await page.request.get(`${origin}/_content/Valour.Client/media/villages/library-atlas.png`);
    assert.equal(raw.status(), 404);
    record('Published app serves the protected official atlas and no plaintext PNG');

    await openVillage();
    const decodedHash = await page.evaluate(async url => {
        const { resolveVillageImageUrl } = await import('/_content/Valour.Client/ts/VillageAtlasProtection.js');
        const imageUrl = await resolveVillageImageUrl(url);
        const bytes = await (await fetch(imageUrl)).arrayBuffer();
        const digest = await crypto.subtle.digest('SHA-256', bytes);
        return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
    }, manifest.image);
    assert.equal(decodedHash, manifest.imageSha256);
    await page.screenshot({ path: `${output}/commons.png` });
    record('Outdoor renderer decodes the complete atlas without changing any PNG bytes');

    await page.getByRole('button', { name: 'Open build mode', exact: true }).click();
    await page.locator('.build-item-sprite').first().waitFor();
    await previewCheck();
    await page.screenshot({ path: `${output}/desktop-builder.png` });
    record('Desktop furniture previews use the decoded in-memory atlas');
    await page.getByRole('button', { name: 'Close build mode', exact: true }).click();
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.getByRole('button', { name: 'Browse village places' }).click();
    await page.locator('.place-list-item').filter({ has: page.getByText('Town Hall', { exact: true }) }).click();
    await page.getByRole('button', { name: 'Enter', exact: true }).click();
    await page.getByRole('button', { name: 'Leave building', exact: true }).waitFor();
    await page.locator('.hud-chip.live').waitFor();
    await page.screenshot({ path: `${output}/interior.png` });
    record('Furnished interior and walls load through the protected atlas');

    await page.getByRole('button', { name: 'User settings button', exact: true }).click();
    await page.getByText('Staff', { exact: true }).first().click();
    await page.getByText('Tileset Tool', { exact: true }).click();
    await page.getByRole('button', { name: /Open Tileset Tool/ }).click();
    await page.locator('.v-menu-close').waitFor({ state: 'hidden' });
    await page.getByLabel('Source sheet', { exact: true }).waitFor();
    assert.ok(await page.getByLabel('Source sheet', { exact: true }).locator('option').count() >= 17);
    await page.getByLabel('Jump to asset', { exact: true }).selectOption('office.desk.birch');
    await page.getByRole('button', { name: 'Build', exact: true }).click();
    const downloading = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Atlas', exact: true }).click();
    await (await downloading).saveAs(`${output}/staff-export.png`);
    const exported = JSON.parse(await page.locator('.json-output').inputValue());
    assert.match(exported.image, /\.png$/);
    assert.equal(exported.imageSha256, createHash('sha256').update(await readFile(`${output}/staff-export.png`)).digest('hex'));
    assert.equal(exported.definitions.length, manifest.definitions.length);
    await page.screenshot({ path: `${output}/staff-editor.png` });
    record('Staff can reconstruct, edit and export the official library; normal PNG exports remain supported');
    await context.close();
    context = await browser.newContext({ ...engines.devices[engine === 'webkit' ? 'iPhone 13' : 'Pixel 7'], viewport: { width: 390, height: 844 } });
    page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    await openVillage(true);
    await page.getByRole('button', { name: 'Open build mode', exact: true }).tap();
    await page.locator('.build-item-sprite').first().waitFor();
    await previewCheck();
    assert.ok((await page.locator('.village-build-panel').boundingBox()).width >= 320);
    await page.screenshot({ path: `${output}/phone-builder.png` });
    record('Phone session with touch input loads full-width furniture previews from the protected atlas');
    assert.deepEqual(errors, []);
} finally {
    await writeFile(`${output}/results.json`, JSON.stringify({ engine, checks, errors }, null, 2));
    await browser.close();
}
