import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, sep } from 'node:path';

const { chromium, firefox } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = resolve('Valour/Client');
const uploads = [];
const server = createServer(async (request, response) => {
    if (request.url === '/upload') {
        const chunks = [];
        for await (const chunk of request) chunks.push(chunk);
        uploads.push(Buffer.concat(chunks).toString());
        response.writeHead(200, { 'Content-Type': 'text/plain' }).end('uploaded');
        return;
    }
    if (request.url === '/') {
        response.writeHead(200, { 'Content-Type': 'text/html' });
        response.end(`<!doctype html><link rel="icon" href="data:,"><style>
            * { box-sizing: border-box; } .pane { position:relative; height:100px; }
            .tab-wrapper { transition:none !important; }
            </style><link rel="stylesheet" href="/Components/DockWindows/WindowComponent.razor.css">
            <script src="/wwwroot/js/channel.js"></script>
            <script src="/wwwroot/js/contextPressEvent.js"></script>
            <div id="drop"><input id="file" type="file"></div>
            <button id="profile">My profile</button><div class="pane"></div>`);
        return;
    }
    const path = resolve(root, '.' + new URL(request.url, 'http://localhost').pathname);
    if (!path.startsWith(root + sep) || !/\.(js|css)$/.test(path)) {
        response.writeHead(404).end();
        return;
    }
    try {
        response.writeHead(200, { 'Content-Type': path.endsWith('.css') ? 'text/css' : 'text/javascript' });
        response.end(await readFile(path));
    } catch { response.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const engine = process.env.BROWSER_ENGINE === 'firefox' ? firefox : chromium;
const browser = await engine.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
const page = await browser.newPage({ hasTouch: true });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
page.on('requestfailed', request => errors.push(request.url()));
try {
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.evaluate(async () => {
        const { afterStarted } = await import('/wwwroot/js/Valour.Client.lib.module.js');
        window.profilePresses = [];
        afterStarted({ registerCustomEventType(name, options) {
            document.querySelector('#profile').addEventListener(name, event => {
                window.profilePresses.push(options.createEventArgs(event));
            });
        } });
    });
    await page.locator('#profile').click({ button: 'right' });
    assert.equal(await page.evaluate(() => profilePresses.length), 1);
    if (engine === chromium) {
        const rect = await page.locator('#profile').boundingBox();
        const touch = await page.context().newCDPSession(page);
        await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: rect.x + 5, y: rect.y + 5 }] });
        await page.waitForFunction(() => profilePresses.length > 1);
        await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        const args = await page.evaluate(() => profilePresses[1]);
        assert.ok(Math.abs(args.clientX - (rect.x + 5)) < 1);
        assert.ok(Math.abs(args.clientY - (rect.y + 5)) < 1);
        await touch.detach();
        console.log('PASS real touch long-press reaches the custom event adapter');
    }
    await page.evaluate(async () => {
        initializeFileDropZone(document.querySelector('#drop'), document.querySelector('#file'), null);
        const transfer = new DataTransfer();
        transfer.items.add(new File(['clipboard image payload'], 'clipboard.png', { type: 'image/png' }));
        // Firefox ignores ClipboardEventInit.clipboardData for synthetic events.
        const paste = new Event('paste', { bubbles: true });
        Object.defineProperty(paste, 'clipboardData', { value: transfer });
        document.querySelector('#drop').dispatchEvent(paste);
        transfer.items.clear();
        await new Promise(resolve => setTimeout(resolve, 10));
        const upload = await import('/wwwroot/ts/UploadService.js');
        const input = document.querySelector('#file');
        if (input.files.length !== 1) throw new Error('Clipboard file was lost');
        const preview = upload.createObjectUrlFromInput(input);
        if (await (await fetch(preview)).text() !== 'clipboard image payload') throw new Error('Preview data changed');
        upload.revokeObjectUrl(preview);
        await new Promise((resolve, reject) => upload.startFromInput('/upload', input, 'clipboard.png', null, {
            async invokeMethodAsync(name, value) {
                if (name === 'NotifyUploadComplete') resolve();
                if (name === 'NotifyUploadError') reject(new Error(value));
            }
        }));
    });
    assert.match(uploads[0], /clipboard image payload/);
    console.log('PASS pasted file survives clipboard release, previews, and uploads');

    for (const width of [420, 850, 1280]) {
        for (const count of [1, 2, 3, 4, 5, 6, 7, 10]) {
            const bounds = await page.evaluate(({ width, count }) => {
                const pane = document.querySelector('.pane');
                pane.style.width = `${width}px`;
                pane.replaceChildren();
                for (let i = 0; i < count; i++) {
                    const wrapper = document.createElement('div');
                    wrapper.className = 'window-wrapper docked active';
                    const slot = document.createElement('div');
                    slot.className = `tab-wrapper docked ${i === count - 1 ? 'last' : ''}`;
                    slot.style.cssText = `width:calc(${100 / count}% - ${30 / count}px + ${i === count - 1 ? 30 : 0}px); margin-left:min(${250 * i}px, calc(${100 / count * i}% - ${30 / count * i}px));`;
                    slot.innerHTML = '<div class="tab"><div class="tab-info"><span class="tab-title">Channel name</span></div><div class="tab-buttons"><span>−</span></div></div>' +
                        (i === count - 1 ? '<div class="tab add">+</div>' : '');
                    wrapper.append(slot);
                    pane.append(wrapper);
                }
                return [...pane.querySelectorAll('.tab')].map(el => {
                    const rect = el.getBoundingClientRect();
                    return { left: rect.left - pane.getBoundingClientRect().left, right: rect.right - pane.getBoundingClientRect().left };
                });
            }, { width, count });
            for (let i = 0; i < bounds.length; i++) {
                assert.ok(bounds[i].right <= width + 1, `tab exceeds pane at ${width}px/${count} tabs`);
                if (i) assert.ok(bounds[i].left >= bounds[i - 1].right - 1, `tabs overlap at ${width}px/${count} tabs`);
            }
        }
    }
    assert.deepEqual(errors, []);
    console.log('PASS tab geometry at 24 pane/count combinations; no console or network errors');
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
}
