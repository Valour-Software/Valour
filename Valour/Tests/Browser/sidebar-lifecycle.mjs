import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium, webkit } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
assert.ok(process.env.VILLAGE_QA_EMAIL && process.env.VILLAGE_QA_PASSWORD);
const engine = process.env.VILLAGE_QA_BROWSER || 'chromium';
assert.ok(['chromium', 'webkit'].includes(engine));
const output = resolve(process.env.VILLAGE_QA_OUTPUT || `TestResults/village-browser/sidebar-lifecycle-${engine}`);
await mkdir(output, { recursive: true });
const browser = await (engine === 'webkit' ? webkit : chromium).launch({ headless: true, executablePath: engine === 'webkit' ? undefined : process.env.BROWSER_EXECUTABLE });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, serviceWorkers: 'block' });
const page = await context.newPage();
page.setDefaultTimeout(20000);
const errors = [], checks = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => {
    if (message.type() === 'error' && /ObjectDisposed|Unhandled exception|ErrorBoundary|rendering component/i.test(message.text())) errors.push(message.text());
});
const record = name => { checks.push(name); console.log(`PASS ${name}`); };
let importRequested, releaseImport;
const requested = new Promise(resolve => importRequested = resolve);
const released = new Promise(resolve => releaseImport = resolve);
let importFinished;
const finished = new Promise(resolve => importFinished = resolve);
await page.route('**/Components/Sidebar/Sidebar.razor.js', async route => {
    importRequested();
    await released;
    await route.continue();
    importFinished();
});

try {
    await page.goto(origin);
    await page.locator('input[type=email]').fill(process.env.VILLAGE_QA_EMAIL);
    await page.locator('input[type=password]').fill(process.env.VILLAGE_QA_PASSWORD);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    await Promise.race([requested, page.waitForTimeout(90000).then(() => { throw new Error('Sidebar import was not requested'); })]);
    await page.locator('.sidebar-menu').waitFor();
    const oldId = await page.locator('.sidebar-menu').getAttribute('id');

    // A same-document route change unmounts Index while its module import is held.
    await page.evaluate(() => {
        const link = document.createElement('a');
        link.id = 'sidebar-lifecycle-route';
        link.href = '/ForgotPassword';
        link.textContent = 'Leave this screen';
        link.style.cssText = 'position:fixed;top:0;right:0;z-index:2147483647;background:white;color:black;padding:12px';
        document.body.append(link);
    });
    await page.locator('#sidebar-lifecycle-route').click();
    await page.getByRole('heading', { name: 'Forgot your password?', exact: true }).waitFor();
    await page.locator('.sidebar-menu').waitFor({ state: 'detached' });
    releaseImport();
    await finished;
    await page.waitForTimeout(500);
    assert.deepEqual(errors, []);
    assert.equal(await page.evaluate(() => typeof window.toggleSidebar), 'undefined');
    record('Closing the app view during a delayed sidebar import leaves no error or stale handler');

    await page.goBack();
    await page.locator('.sidebar-menu[data-mobile-open="false"]').waitFor({ timeout: 90000 });
    assert.notEqual(await page.locator('.sidebar-menu').getAttribute('id'), oldId);
    assert.equal(await page.evaluate(() => typeof window.toggleSidebar), 'function');
    await page.locator('#sidebar-lifecycle-route').evaluate(element => element.remove());
    await page.getByText(process.env.VILLAGE_QA_PLANET || 'Village Release QA', { exact: false }).first().click();
    record('Returning creates a working replacement sidebar');

    for (let cycle = 0; cycle < 5; cycle++) {
        await page.getByText('Village', { exact: true }).first().click();
        await page.locator('.village-canvas').waitFor({ timeout: 60000 });
        const window = page.locator('.window-wrapper').filter({ has: page.locator('.village-canvas') });
        await window.getByTitle('Close tab', { exact: true }).click();
        await page.locator('.village-canvas').waitFor({ state: 'detached' });
    }
    await page.getByText('Village', { exact: true }).first().click();
    await page.locator('.village-canvas').waitFor();
    await page.getByRole('button', { name: 'Browse village places', exact: true }).click();
    await page.getByRole('button', { name: 'Close places', exact: true }).waitFor();
    await page.screenshot({ path: `${output}/reopened-village.png` });
    await page.waitForTimeout(300);
    assert.deepEqual(errors, []);
    record('Five close/reopen cycles preserve navigation and the village window');
} catch (error) {
    await page.screenshot({ path: `${output}/failure.png` }).catch(() => {});
    throw error;
} finally {
    releaseImport();
    await writeFile(`${output}/results.json`, JSON.stringify({ engine, checks, errors }, null, 2));
    await browser.close();
}
