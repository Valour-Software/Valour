import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, sep } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = resolve('Valour/Client');
const server = createServer(async (request, response) => {
    const url = new URL(request.url, 'http://localhost');
    if (url.pathname === '/') {
        response.writeHead(200, { 'Content-Type': 'text/html' });
        response.end('<!doctype html><html><head><link rel="icon" href="data:,"></head><body><div id="editor" contenteditable="true"></div></body></html>');
        return;
    }
    const path = resolve(root, '.' + url.pathname);
    if (!path.startsWith(root + sep) || !path.endsWith('.js')) {
        response.writeHead(404).end();
        return;
    }
    try {
        response.writeHead(200, { 'Content-Type': 'text/javascript' });
        response.end(await readFile(path));
    } catch { response.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
const page = await browser.newPage();
const errors = [], networkFailures = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
page.on('requestfailed', request => networkFailures.push(request.url()));
try {
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const checks = await page.evaluate(async () => {
        const checks = [];
        function check(condition, message) { if (!condition) throw new Error(message); checks.push(message); }
        const input = await import('/Components/Windows/ChannelWindows/InputComponent.razor.js');
        const callbacks = [];
        const editor = document.querySelector('#editor');
        const context = input.init({ async invokeMethodAsync(method, ...args) { callbacks.push({ method, args }); } }, editor);
        editor.textContent = 'hello';
        editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: 'hello' }));
        await new Promise(resolve => setTimeout(resolve, 90));
        check(callbacks.some(x => x.method === 'OnChatboxUpdate' && x.args[0].trim() === 'hello'), 'Live editor reports typed text');
        const count = callbacks.length;
        editor.dispatchEvent(new InputEvent('input', { bubbles: true }));
        context.cleanup();
        editor.remove();
        await new Promise(resolve => setTimeout(resolve, 90));
        check(callbacks.length === count, 'Removed editor does not invoke a disposed callback');
        const fade = await import('/Components/Utility/Fade.razor.js');
        fade.fadeIn(null); fade.fadeOut(null);
        check(true, 'Animation tolerates a removed host');
        const browser = await import('/Components/Utility/BrowserUtils.razor.js');
        let oldCalls = 0, currentCalls = 0;
        const old = browser.init({ async invokeMethodAsync() { oldCalls++; } });
        const current = browser.init({ async invokeMethodAsync() { currentCalls++; } });
        old.dispose();
        window.dispatchEvent(new Event('resize'));
        await new Promise(resolve => setTimeout(resolve, 0));
        check(oldCalls === 0 && currentCalls === 1, 'Replacement browser listeners survive old component disposal');
        current.dispose();
        window.dispatchEvent(new Event('resize'));
        check(currentCalls === 1, 'Browser listeners stop after disposal');
        return checks;
    });
    assert.deepEqual(errors, []);
    assert.deepEqual(networkFailures, []);
    for (const check of checks) console.log(`PASS ${check}`);
    console.log('PASS no browser console errors or failed network requests');
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
}
