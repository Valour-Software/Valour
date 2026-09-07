import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
import { fadeIn, fadeOut } from '../../Client/Components/Utility/Fade.razor.js';
import { init, destroy } from '../../Client/Components/Utility/ColorPickerComponent.razor.js';

test('fade tolerates a removed element and animates a live one', () => {
    fadeIn(null);
    fadeOut(null);
    const classes = new Set();
    const el = { classList: { add: value => classes.add(value), remove: value => classes.delete(value) } };
    fadeIn(el);
    assert.ok(classes.has('fade-in'));
    fadeOut(el);
    assert.equal(classes.size, 0);
});

test('color picker does not initialize after its host disappears; destroy is idempotent', async () => {
    let creates = 0, destroys = 0;
    let host = null;
    globalThis.document = { getElementById: () => host };
    globalThis.Pickr = { create() { creates++; return { on() {}, destroyAndRemove() { destroys++; } }; } };
    await init('gone', {}, '#ffffff');
    assert.equal(creates, 0);
    destroy('gone');
    host = { isConnected: true };
    await init('live', {}, '#ffffff');
    destroy('live');
    destroy('live');
    assert.equal(creates, 1);
    assert.equal(destroys, 1);
});

test('file drop works without an upload button and removes all listeners', async () => {
    const source = await readFile(new URL('../../Client/wwwroot/js/channel.js', import.meta.url), 'utf8');
    const context = vm.createContext({ console: { log() {} }, Event });
    vm.runInContext(source, context);
    const listeners = new Map();
    const zone = { classList: { remove() {} }, addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
    let changed = 0;
    const input = { dispatchEvent() { changed++; } };
    const drop = context.initializeFileDropZone(zone, input, undefined);
    const files = ['example.webp'];
    listeners.get('drop')({ preventDefault() {}, dataTransfer: { files } });
    assert.equal(input.files, files);
    assert.equal(changed, 1);
    drop.dispose();
    assert.equal(listeners.size, 0);
    context.initializeFileDropZone(null, null, null).dispose();
});

test('closing an old dock preserves the replacement browser history listener', async () => {
    const listeners = new Set();
    let pushes = 0;
    globalThis.window = {
        history: { state: {}, replaceState(state) { this.state = state; }, pushState() { pushes++; } },
        addEventListener(name, handler) { listeners.add(handler); },
        removeEventListener(name, handler) { listeners.delete(handler); }
    };
    const history = await import('../../Client/Components/DockWindows/WindowDockComponent.razor.js');
    history.initialize({}, 'tab-a', 'content-a', 'old');
    history.initialize({}, 'tab-b', 'content-b', 'current');
    history.dispose('old');
    assert.equal(listeners.size, 1);
    history.push('tab-c', 'content-c', 'old');
    assert.equal(pushes, 0);
    history.push('tab-c', 'content-c', 'current');
    assert.equal(pushes, 1);
    history.dispose('current');
    assert.equal(listeners.size, 0);
});

test('browser utilities detach only their own listeners and ignore queued callbacks', async () => {
    const windowListeners = new Map(), documentListeners = new Map();
    function target(listeners) {
        return {
            addEventListener(name, fn) {
                if (!listeners.has(name)) listeners.set(name, new Set());
                listeners.get(name).add(fn);
            },
            removeEventListener(name, fn) { listeners.get(name).delete(fn); }
        };
    }
    globalThis.window = { ...target(windowListeners), innerWidth: 800, innerHeight: 600 };
    globalThis.document = { ...target(documentListeners), hidden: false };
    const browser = await import('../../Client/Components/Utility/BrowserUtils.razor.js');
    let oldCalls = 0, newCalls = 0;
    const old = browser.init({ async invokeMethodAsync() { oldCalls++; } });
    const queued = [...windowListeners.get('resize')][0];
    const current = browser.init({ async invokeMethodAsync() { newCalls++; } });
    old.dispose();
    await queued();
    assert.equal(oldCalls, 0);
    for (const fn of windowListeners.get('resize')) await fn();
    assert.equal(newCalls, 1);
    assert.equal(documentListeners.get('visibilitychange').size, 1);
    current.dispose();
    for (const listeners of [...windowListeners.values(), ...documentListeners.values()])
        assert.equal(listeners.size, 0);
});

test('input cleanup prevents a pending debounced callback from reaching a disposed component', async () => {
    const { init } = await import('../../Client/Components/Windows/ChannelWindows/InputComponent.razor.js');
    let queued;
    globalThis.window = { setTimeout(fn) { queued = fn; return 1; } };
    globalThis.document = { addEventListener() {}, removeEventListener() {} };
    const element = { addEventListener() {}, removeEventListener() {} };
    let calls = 0;
    const context = init({ async invokeMethodAsync() { calls++; } }, element);
    context.inputHandler({});
    context.cleanup();
    await queued();
    assert.equal(calls, 0);
    assert.equal(init({}, null), null);
});

test('target scanner releases captured dragover listener and its highlighted target', async () => {
    const listeners = new Set(), classes = new Set();
    globalThis.document = {
        addEventListener(name, fn) { listeners.add(fn); },
        removeEventListener(name, fn) { listeners.delete(fn); }
    };
    const { init } = await import('../../Client/Components/DockWindows/WindowTargetScanner.razor.js');
    const target = { classList: { add(name) { classes.add(name); }, remove(name) { classes.delete(name); } } };
    const scanner = init();
    const queued = [...listeners][0];
    queued({ target: { closest: () => target } });
    assert.ok(classes.has('w-target-active'));
    scanner.dispose();
    queued({ target: { closest: () => target } });
    assert.equal(classes.size, 0);
    assert.equal(listeners.size, 0);
});
