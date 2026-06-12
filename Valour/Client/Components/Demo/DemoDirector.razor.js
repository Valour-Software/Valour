// Demo reel director. Drives the real app with a fake cursor, scripted
// clicks/typing/scrolling, and a 3D "camera" for recording videos.
// Activated by DemoDirector.razor when the page has ?demo=1.
//
// Hotkeys (ignored while focus is in an input):
//   0 - full reel (sidebar tour -> open planet -> messages -> channel list)
//   1 - sidebar tab tour
//   2 - open target planet from sidebar
//   3 - type + send a couple of messages
//   4 - channel list scroll + open/close categories
//   q/w/e/r/t - camera presets (default / left / right / low / hero drift)
//   h - toggle HUD     Escape - abort current scene

let ctx = null;

const CAMERA_PRESETS = ['cam-default', 'cam-left', 'cam-right', 'cam-low', 'cam-hero'];

const STYLE = `
body.demo-stage {
    background: radial-gradient(120% 130% at 18% 0%, #14283f 0%, #070d18 55%, #02050a 100%) !important;
    overflow: hidden;
}
body.demo-stage .mobile-holder {
    transition: transform 2400ms cubic-bezier(0.22, 1, 0.36, 1), border-radius 900ms ease, box-shadow 900ms ease;
    will-change: transform;
}
body.demo-stage.cam-left .mobile-holder {
    transform: perspective(1500px) rotateY(14deg) rotateX(5deg) scale(0.88);
}
body.demo-stage.cam-right .mobile-holder {
    transform: perspective(1500px) rotateY(-14deg) rotateX(5deg) scale(0.88);
}
body.demo-stage.cam-low .mobile-holder {
    transform: perspective(1500px) rotateX(14deg) scale(0.9) translateY(-2%);
}
body.demo-stage.cam-hero .mobile-holder {
    animation: demo-hero-drift 14s ease-in-out infinite alternate;
}
body.demo-stage.cam-left .mobile-holder,
body.demo-stage.cam-right .mobile-holder,
body.demo-stage.cam-low .mobile-holder,
body.demo-stage.cam-hero .mobile-holder {
    border-radius: 18px;
    overflow: hidden;
    box-shadow: 0 70px 130px -50px rgba(0, 0, 0, 0.85), 0 0 0 1px rgba(255, 255, 255, 0.07),
                0 0 90px -20px rgba(64, 140, 255, 0.18);
}
@keyframes demo-hero-drift {
    0%   { transform: perspective(1600px) rotateY(7deg) rotateX(3deg) scale(0.9); }
    100% { transform: perspective(1600px) rotateY(-7deg) rotateX(5deg) scale(0.92); }
}
body.demo-running, body.demo-running * {
    cursor: none !important;
}
#demo-cursor {
    position: fixed;
    z-index: 100000;
    width: 22px;
    height: 22px;
    margin: -11px 0 0 -11px;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.85);
    box-shadow: 0 0 0 2px rgba(0, 0, 0, 0.25), 0 4px 14px rgba(0, 0, 0, 0.45);
    pointer-events: none;
    opacity: 0;
    transition: opacity 300ms ease;
    transform: translate3d(-100px, -100px, 0);
}
#demo-cursor.pressed {
    background: rgba(120, 190, 255, 0.95);
}
.demo-ripple {
    position: fixed;
    z-index: 99999;
    width: 14px;
    height: 14px;
    margin: -7px 0 0 -7px;
    border-radius: 50%;
    border: 2px solid rgba(140, 200, 255, 0.9);
    pointer-events: none;
    animation: demo-ripple 600ms ease-out forwards;
}
@keyframes demo-ripple {
    from { transform: scale(1); opacity: 1; }
    to   { transform: scale(4.5); opacity: 0; }
}
#demo-hud {
    position: fixed;
    top: 10px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 100001;
    background: rgba(8, 14, 24, 0.85);
    color: #cfe2ff;
    border: 1px solid rgba(140, 200, 255, 0.2);
    border-radius: 999px;
    padding: 4px 14px;
    font-family: monospace;
    font-size: 11px;
    line-height: 1.5;
    pointer-events: none;
    white-space: pre;
    opacity: 1;
    transition: opacity 600ms ease;
}
#demo-hud.faded {
    opacity: 0;
}
`;

function sleep(ms) {
    return new Promise(r => setTimeout(r, ms));
}

function visible(el) {
    return el && el.offsetParent !== null && el.getClientRects().length > 0;
}

function find(selector) {
    return [...document.querySelectorAll(selector)].find(visible) ?? null;
}

function findByText(selector, text) {
    const lower = text.toLowerCase();
    return [...document.querySelectorAll(selector)]
        .filter(visible)
        .find(el => (el.textContent ?? '').toLowerCase().includes(lower)) ?? null;
}

async function waitFor(getter, timeoutMs = 8000) {
    const start = performance.now();
    while (performance.now() - start < timeoutMs) {
        if (ctx?.aborted) return null;
        const el = getter();
        if (el) return el;
        await sleep(120);
    }
    console.warn('[demo] waitFor timed out');
    return null;
}

function center(el) {
    const r = el.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

export function init(planetName) {
    if (ctx) return;

    const style = document.createElement('style');
    style.id = 'demo-style';
    style.textContent = STYLE;
    document.head.appendChild(style);

    const cursor = document.createElement('div');
    cursor.id = 'demo-cursor';
    document.body.appendChild(cursor);

    const hud = document.createElement('div');
    hud.id = 'demo-hud';
    hud.textContent = `DEMO [${planetName}]  0:reel 1:tabs 2:planet 3:msgs 4:channels  q/w/e/r/t:cam  h:hud  esc:stop`;
    document.body.appendChild(hud);
    // Auto-fade so it never sits over the UI; press h to bring it back
    const hudFadeTimer = setTimeout(() => hud.classList.add('faded'), 6000);

    document.body.classList.add('demo-stage');

    ctx = {
        planetName,
        cursor,
        hud,
        running: false,
        aborted: false,
        cursorX: innerWidth / 2,
        cursorY: innerHeight / 2,
        keyHandler: null,
    };

    ctx.keyHandler = e => {
        const t = e.target;
        if (t && (t.isContentEditable || t.tagName === 'INPUT' || t.tagName === 'TEXTAREA')) return;
        if (!e.isTrusted) return;

        switch (e.key) {
            case '0': runScene(sceneFullReel); break;
            case '1': runScene(sceneSidebarTabs); break;
            case '2': runScene(sceneOpenPlanet); break;
            case '3': runScene(sceneMessages); break;
            case '4': runScene(sceneChannelList); break;
            case 'q': camera('cam-default'); break;
            case 'w': camera('cam-left'); break;
            case 'e': camera('cam-right'); break;
            case 'r': camera('cam-low'); break;
            case 't': camera('cam-hero'); break;
            case 'h':
                clearTimeout(hudFadeTimer);
                hud.classList.toggle('faded');
                break;
            case 'Escape': ctx.aborted = true; break;
        }
    };
    window.addEventListener('keydown', ctx.keyHandler);
    console.log('[demo] director ready - press 0 for the full reel');
}

export function cleanup() {
    if (!ctx) return;
    ctx.aborted = true;
    window.removeEventListener('keydown', ctx.keyHandler);
    document.getElementById('demo-style')?.remove();
    ctx.cursor.remove();
    ctx.hud.remove();
    document.body.classList.remove('demo-stage', 'demo-running', ...CAMERA_PRESETS);
    ctx = null;
}

// ---------------------------------------------------------------------------
// Primitives
// ---------------------------------------------------------------------------

function camera(preset) {
    document.body.classList.remove(...CAMERA_PRESETS);
    if (preset !== 'cam-default')
        document.body.classList.add(preset);
}

function setCursor(x, y, ms) {
    ctx.cursor.style.transition = `transform ${ms}ms cubic-bezier(0.3, 0.05, 0.2, 1), opacity 300ms ease`;
    ctx.cursor.style.transform = `translate3d(${x}px, ${y}px, 0)`;
    ctx.cursorX = x;
    ctx.cursorY = y;
}

async function moveCursorTo(x, y) {
    const dist = Math.hypot(x - ctx.cursorX, y - ctx.cursorY);
    const ms = Math.min(1300, Math.max(350, dist * 1.1));
    ctx.cursor.style.opacity = '1';
    setCursor(x, y, ms);
    await sleep(ms + 60);
}

function dispatchPointerSequence(el, x, y, types) {
    for (const type of types) {
        const init = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, button: 0, buttons: type.includes('down') ? 1 : 0 };
        if (type.startsWith('pointer'))
            el.dispatchEvent(new PointerEvent(type, { ...init, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
        else
            el.dispatchEvent(new MouseEvent(type, init));
    }
}

async function clickEl(el) {
    if (!el) return false;
    el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    await sleep(350);
    const { x, y } = center(el);
    await moveCursorTo(x, y);

    ctx.cursor.classList.add('pressed');
    const ripple = document.createElement('div');
    ripple.className = 'demo-ripple';
    ripple.style.left = `${x}px`;
    ripple.style.top = `${y}px`;
    document.body.appendChild(ripple);
    setTimeout(() => ripple.remove(), 700);

    dispatchPointerSequence(el, x, y, ['pointerdown', 'mousedown']);
    await sleep(90);
    dispatchPointerSequence(el, x, y, ['pointerup', 'mouseup', 'click']);
    ctx.cursor.classList.remove('pressed');
    await sleep(150);
    return true;
}

async function typeInto(el, text) {
    if (!el) return false;
    await clickEl(el);
    el.focus();

    for (const char of text) {
        if (ctx.aborted) return false;
        el.appendChild(document.createTextNode(char));
        el.dispatchEvent(new InputEvent('input', { bubbles: true, data: char, inputType: 'insertText' }));
        await sleep(35 + Math.random() * 75);
    }

    // Let the debounced input handler flush to Blazor
    await sleep(300);
    return true;
}

async function smoothScroll(el, to, ms) {
    const from = el.scrollTop;
    const start = performance.now();
    return new Promise(resolve => {
        function frame(now) {
            if (ctx.aborted) return resolve();
            const t = Math.min(1, (now - start) / ms);
            const eased = t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
            el.scrollTop = from + (to - from) * eased;
            t < 1 ? requestAnimationFrame(frame) : resolve();
        }
        requestAnimationFrame(frame);
    });
}

async function ensureSidebarOpen() {
    // Mobile only: the burger toggle exists in the topbar
    const toggle = find('.sidebar-toggle');
    if (toggle && !find('.sidebar-container.sidebar-active .tabstrip'))
        await clickEl(toggle);
    await sleep(500);
}

async function selectSidebarTab(index) {
    const tabs = [...document.querySelectorAll('.tabstrip .item')].filter(visible);
    if (tabs[index])
        await clickEl(tabs[index]);
    await sleep(700);
}

async function runScene(scene) {
    if (!ctx || ctx.running) return;
    ctx.running = true;
    ctx.aborted = false;
    ctx.hud.classList.add('faded');
    document.body.classList.add('demo-running');
    try {
        await scene();
    } catch (e) {
        console.error('[demo] scene failed', e);
    }
    document.body.classList.remove('demo-running');
    ctx.cursor.style.opacity = '0';
    ctx.running = false;
}

// ---------------------------------------------------------------------------
// Scenes
// ---------------------------------------------------------------------------

async function sceneSidebarTabs() {
    await ensureSidebarOpen();
    camera('cam-left');
    for (const i of [1, 3, 2, 0]) {
        if (ctx.aborted) return;
        await selectSidebarTab(i);
        await sleep(650);
    }
    camera('cam-default');
}

async function sceneOpenPlanet() {
    await ensureSidebarOpen();
    await selectSidebarTab(0);

    const row = await waitFor(() => findByText('.planet-row', ctx.planetName));
    if (!row) return;
    await clickEl(row);

    await waitFor(() => find('.textbox-inner'));
    await sleep(1200);
}

async function sceneMessages() {
    const messages = [
        'Valour is looking incredible lately',
        'Native apps, real-time sync, and it is all open source'
    ];

    camera('cam-right');
    await sleep(1500);

    for (const message of messages) {
        if (ctx.aborted) return;
        const input = await waitFor(() => find('.textbox-inner'));
        if (!input) return;

        await typeInto(input, message);

        const send = await waitFor(() => find('.send-wrapper'), 3000);
        if (send) {
            await clickEl(send);
        } else {
            input.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter', code: 'Enter' }));
        }
        await sleep(1100);
    }

    camera('cam-default');
}

async function sceneChannelList() {
    await ensureSidebarOpen();
    await selectSidebarTab(2);
    camera('cam-left');

    const list = await waitFor(() => find('.full-channel-list'));
    if (!list) return;

    const maxScroll = Math.max(0, list.scrollHeight - list.clientHeight);
    if (maxScroll > 40) {
        await smoothScroll(list, maxScroll, 2600);
        await sleep(500);
        await smoothScroll(list, 0, 2200);
    }

    const categories = [...list.querySelectorAll('.channel')]
        .filter(visible)
        .filter(el => el.querySelector('.channel-icon[class*="bi-folder"]'))
        .slice(0, 2);

    for (const category of categories) {
        if (ctx.aborted) return;
        await clickEl(category);   // close
        await sleep(950);
        await clickEl(category);   // open
        await sleep(950);
    }

    camera('cam-hero');
}

async function sceneFullReel() {
    camera('cam-low');
    await sleep(2600);
    await sceneSidebarTabs();
    if (ctx.aborted) return;
    await sceneOpenPlanet();
    if (ctx.aborted) return;
    await sceneMessages();
    if (ctx.aborted) return;
    await sceneChannelList();
}
