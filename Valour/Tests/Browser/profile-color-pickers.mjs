import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = resolve('Valour/Client');
const server = createServer(async (request, response) => {
    const files = {
        '/picker.js': '/Components/Utility/ColorPickerComponent.razor.js',
        '/picker.css': '/wwwroot/css/pickr.min.css'
    };
    if (files[request.url]) {
        response.writeHead(200, { 'Content-Type': request.url.endsWith('.css') ? 'text/css' : 'text/javascript' });
        response.end(await readFile(root + files[request.url]));
    } else if (request.url === '/') {
        response.writeHead(200, { 'Content-Type': 'text/html' });
        response.end('<!doctype html><link rel="icon" href="data:,"><link rel="stylesheet" href="/picker.css"><main></main>');
    } else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('requestfailed', request => errors.push(request.url()));
    if (process.env.PICKR_SCRIPT) {
        const script = await readFile(process.env.PICKR_SCRIPT, 'utf8');
        await page.route('https://cdn.jsdelivr.net/npm/@simonwep/pickr/dist/pickr.min.js', route =>
            route.fulfill({ contentType: 'text/javascript', body: script }));
    }
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const colors = ['#12ABCD', '#A63FC1', '#E57219', '#36C582', '#EEEEEE'];
    for (let pass = 0; pass < 2; pass++) {
        await page.evaluate(async colors => {
            window.colorChanges = [];
            const picker = await import('/picker.js');
            const main = document.querySelector('main');
            for (let i = 0; i < colors.length; i++) {
                const host = document.createElement('div');
                host.id = `color-${i}`;
                main.append(host);
                await picker.init(host.id, { async invokeMethodAsync(method, value) {
                    colorChanges.push({ method, value });
                } }, colors[i]);
            }
        }, colors);
        const buttons = page.locator('.pcr-button');
        for (let i = 0; i < colors.length; i++) {
            const rgb = colors[i].slice(1).match(/../g).map(value => parseInt(value, 16));
            await page.waitForFunction(({ i, rgb }) =>
                getComputedStyle(document.querySelectorAll('.pcr-button')[i], '::after').backgroundColor === `rgb(${rgb.join(', ')})`, { i, rgb });
            await buttons.nth(i).click();
            assert.equal((await page.locator('.pcr-app.visible .pcr-result').inputValue()).toUpperCase(), colors[i]);
            await buttons.nth(i).click();
        }
        assert.deepEqual(await page.evaluate(() => colorChanges), []);
        await buttons.first().click();
        await page.locator('.pcr-app.visible .pcr-result').fill('#FF3366');
        await page.waitForFunction(() => colorChanges.some(change => change.value === '#FF3366'));
        await page.evaluate(async () => {
            const picker = await import('/picker.js');
            for (let i = 0; i < 5; i++) picker.destroy(`color-${i}`);
            document.querySelector('main').replaceChildren();
        });
    }
    assert.deepEqual(errors, []);
    console.log('PASS five saved swatches and editor values, reopening, user color changes, and no initialization callbacks or browser errors');
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
}
