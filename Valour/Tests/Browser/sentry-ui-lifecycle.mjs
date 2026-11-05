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
        const tick = () => new Promise(resolve => setTimeout(resolve, 0));
        const roots = [];
        window.Blazor = { rootComponents: { async add(element, identifier, parameters) {
            const root = { identifier, parameters, disposed: false, async dispose() { root.disposed = true; } };
            element.textContent = '@Nova';
            roots.push(root);
            return root;
        } } };
        const chipEditor = document.createElement('div');
        chipEditor.contentEditable = 'true';
        document.body.appendChild(chipEditor);
        const chipCalls = [];
        const memberToken = '«@m-12638576793878528»';
        const chips = input.init({ async invokeMethodAsync(method, ...args) {
            chipCalls.push({ method, args });
            return method === 'GetInputSegments' ? [{ kind: 'mention', text: memberToken, label: '@Nova' }] : null;
        } }, chipEditor, 'input-1');
        chips.setInputSegments([{ kind: 'text', text: 'hi ' }, { kind: 'mention', text: memberToken, label: '@Nova' }, { kind: 'text', text: ' and\nbye' }]);
        await tick();
        check(roots.length === 1 && roots[0].identifier === 'valour-input-mention' &&
            roots[0].parameters.token === memberToken && roots[0].parameters.inputId === 'input-1',
            'Loaded mention renders through its Blazor root component');
        const chip = chipEditor.querySelector('.input-mention');
        chip.remove();
        await tick();
        check(roots[0].disposed, 'Removed mention disposes its root component');
        chipEditor.insertBefore(chip, chipEditor.childNodes[1]);
        await tick();
        check(roots.length === 2 && !roots[1].disposed && chip.textContent === '@Nova', 'Restored mention renders again');

        const selection = window.getSelection();
        selection.selectAllChildren(chipEditor);
        const copied = new DataTransfer();
        chipEditor.dispatchEvent(new ClipboardEvent('copy', { clipboardData: copied, bubbles: true, cancelable: true }));
        check(copied.getData('text/plain') === 'hi @Nova and\nbye', 'Copying gives other apps the visible text');
        check(copied.getData('application/x-valour-message') === `hi ${memberToken} and\nbye`, 'Copying keeps mention tokens for the input');

        await chips.submitMessage();
        const sent = chipCalls.find(x => x.method === 'OnChatboxUpdate');
        // The editor is a block element, so its text is wrapped in line breaks that sending trims.
        const editorText = args => args[0].slice(1, -1);
        check(editorText(sent.args) === `hi ${memberToken} and\nbye`, 'Sent text carries the mention token');
        await tick();
        check(roots[1].disposed, 'Clearing the input disposes its mentions');

        chipEditor.textContent = 'hello @no';
        const caret = document.createRange();
        caret.setStart(chipEditor.firstChild, chipEditor.firstChild.length);
        selection.removeAllRanges();
        selection.addRange(caret);
        chipCalls.length = 0;
        chips.injectMention('«@u-5»', '@Nova');
        await tick();
        check(chipCalls.at(-1).method === 'OnChatboxUpdate' && editorText(chipCalls.at(-1).args) === 'hello «@u-5» ' && chipCalls.at(-1).args[1] === '',
            'Picked mention replaces the typed word');
        check(roots.length === 3 && roots[2].parameters.token === '«@u-5»', 'Picked mention renders through its Blazor root component');

        const pasted = new DataTransfer();
        pasted.setData('application/x-valour-message', memberToken);
        chipEditor.dispatchEvent(new ClipboardEvent('paste', { clipboardData: pasted, bubbles: true, cancelable: true }));
        await tick();
        await tick();
        check(roots.length === 4 && roots[3].parameters.token === memberToken, 'Pasted mention token becomes a chip');
        check(editorText(chipCalls.at(-1).args) === `hello «@u-5» ${memberToken}`, 'Pasted mention is part of the message text');

        chips.cleanup();
        await tick();
        check(roots.every(root => root.disposed), 'Input cleanup disposes every mention');
        chipEditor.remove();
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
