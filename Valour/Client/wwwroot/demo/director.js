// Demo engine entry point, loaded by DemoDirector.razor when ?demo=1.
//
// Hotkeys (ignored while typing in an input):
//   0-7        play scene by registry order (0 = full reel)
//   q/w/e/r/t  camera presets: default / left / right / low / hero drift
//   x          toggle exploded layers      z / c   spread - / +
//   f          pick-to-focus (click any element to zoom the camera onto it)
//   s          pick-to-spotlight           S / n   spotlight off / clear callouts
//   b          letterbox cycle off→wide→tall       v  vignette   a  aurora
//   h          toggle director deck        Escape  stop scene / reset stage
//
// Console API: window.valourDemo — play(steps), run(name), camera, layers,
// overlay, cursor, scenes. Iterate on ad scripts live without rebuilding.
//
// URL params: ?demo=1&planet=Name&scene=ad&deck=0

import { engine, sleep, waitFor, find, stopLoop, cancelTweens } from './engine.js';
import * as camera from './camera.js';
import * as layers from './layers.js';
import * as overlay from './overlay.js';
import * as cursor from './cursor.js';
import * as timeline from './timeline.js';
import { scenes } from './scenes.js';

const BASE = './_content/Valour.Client/demo/';

let keyHandler = null;
let pickHandler = null;
let pickMode = null;   // 'focus' | 'spotlight'
let deck = null;
let cssLink = null;

// Pick targets snap up to a meaningful region instead of a tiny icon
const PICK_CONTAINERS = [
    '.textbox-holder', '.role-list-wrapper', '.chat-scroll-region',
    '.tab-wrapper', '.planet-row', '.channel', '.message-holder',
    '.sidebar-container', '.window-wrapper', '.topbar',
].join(', ');

export async function init(planetName) {
    if (engine.active) return;
    engine.active = true;

    const query = new URLSearchParams(location.search);
    engine.opts = {
        planet: planetName || query.get('planet') || 'Valour Central',
        scene: query.get('scene'),
        deck: query.get('deck') !== '0',
    };

    cssLink = document.createElement('link');
    cssLink.rel = 'stylesheet';
    cssLink.href = `${BASE}demo.css`;
    document.head.appendChild(cssLink);
    await new Promise(r => { cssLink.onload = r; cssLink.onerror = r; });

    document.body.classList.add('demo-stage');

    // The holder only exists once logged in; keep looking so the engine
    // comes alive the moment the app shell renders.
    engine.holder = document.querySelector('.mobile-holder');
    if (!engine.holder) {
        waitFor(() => document.querySelector('.mobile-holder'), 120000)
            .then(h => { if (h) engine.holder = h; });
    }

    camera.init();
    overlay.init();
    cursor.init();
    if (engine.opts.deck) buildDeck();

    keyHandler = onKey;
    window.addEventListener('keydown', keyHandler);

    window.valourDemo = {
        play: steps => runScene(steps),
        run: name => runScene(name),
        stop, camera, layers, overlay, cursor, timeline, scenes, engine,
    };

    console.log('[demo] director ready — press 0 for the full reel, h for the deck');

    if (engine.opts.scene) {
        await waitFor(() => engine.holder && find('.window-dock'), 60000);
        await sleep(2500); // let bootstrap data settle before recording starts
        runScene(engine.opts.scene);
    }
}

export function cleanup() {
    if (!engine.active) return;
    stop();
    layers.restore();
    overlay.destroy();
    cursor.destroy();
    camera.destroy();
    deck?.remove();
    deck = null;
    window.removeEventListener('keydown', keyHandler);
    endPick();
    cssLink?.remove();
    document.body.classList.remove('demo-stage', 'demo-running', 'demo-exploded', 'demo-framed');
    stopLoop();
    delete window.valourDemo;
    engine.active = false;
    engine.holder = null;
}

// ------------------------------------------------------------ scene runs --

async function runScene(sceneOrSteps) {
    if (!engine.active || engine.running) return;

    let steps = sceneOrSteps;
    if (typeof sceneOrSteps === 'string') {
        const scene = scenes[sceneOrSteps];
        if (!scene) { console.warn(`[demo] unknown scene: ${sceneOrSteps}`); return; }
        steps = scene.steps;
    }

    engine.running = true;
    engine.aborted = false;
    document.body.classList.add('demo-running');
    try {
        await timeline.play(steps);
    } catch (e) {
        console.error('[demo] scene failed', e);
    }
    document.body.classList.remove('demo-running');
    cursor.hide();
    engine.running = false;
}

function stop() {
    engine.aborted = true;
    cancelTweens();
}

// Full stage reset: collapse layers, level the camera, kill overlays
async function resetStage() {
    stop();
    overlay.spotlight(null);
    overlay.clearCallouts();
    overlay.letterbox('off');
    overlay.vignette(false);
    await sleep(30);
    engine.aborted = false;
    if (layers.isExploded()) await layers.collapse({ ms: 900 });
    camera.reset({ ms: 1200 });
}

// -------------------------------------------------------------- pick mode --

function startPick(mode) {
    pickMode = mode;
    document.body.classList.add('demo-picking');
    pickHandler = e => {
        e.preventDefault();
        e.stopPropagation();
        const target = e.target.closest(PICK_CONTAINERS) ?? e.target;
        const mode_ = pickMode;
        endPick();
        if (mode_ === 'focus') camera.focusOn(target, { scale: 1.6 });
        else overlay.spotlight(target);
    };
    window.addEventListener('click', pickHandler, { capture: true, once: true });
}

function endPick() {
    if (pickHandler) window.removeEventListener('click', pickHandler, { capture: true });
    pickHandler = null;
    pickMode = null;
    document.body.classList.remove('demo-picking');
}

// --------------------------------------------------------------- hotkeys --

let letterboxMode = 'off';
let vignetteOn = false;
let auroraOn = false;

function onKey(e) {
    const t = e.target;
    if (t && (t.isContentEditable || t.tagName === 'INPUT' || t.tagName === 'TEXTAREA')) return;
    if (!e.isTrusted) return;

    const names = Object.keys(scenes);
    if (/^[0-9]$/.test(e.key)) {
        const name = names[Number(e.key)];
        if (name) runScene(name);
        return;
    }

    switch (e.key) {
        case 'q': camera.preset('default'); break;
        case 'w': camera.preset('left'); break;
        case 'e': camera.preset('right'); break;
        case 'r': camera.preset('low'); break;
        case 't': camera.preset('hero'); break;
        case 'x':
            layers.isExploded() ? layers.collapse() : layers.explode('auto');
            break;
        case 'z': layers.setSpread(layers.getSpread() - 25); break;
        case 'c': layers.setSpread(layers.getSpread() + 25); break;
        case 'f': pickMode ? endPick() : startPick('focus'); break;
        case 's': pickMode ? endPick() : startPick('spotlight'); break;
        case 'S': overlay.spotlight(null); break;
        case 'n': overlay.clearCallouts(); break;
        case 'b':
            letterboxMode = letterboxMode === 'off' ? 'wide' : letterboxMode === 'wide' ? 'tall' : 'off';
            overlay.letterbox(letterboxMode);
            break;
        case 'v': vignetteOn = !vignetteOn; overlay.vignette(vignetteOn); break;
        case 'a': auroraOn = !auroraOn; overlay.aurora(auroraOn); break;
        case 'h':
        case 'd':
            deck?.classList.toggle('hidden');
            break;
        case 'Escape':
            if (pickMode) { endPick(); break; }
            if (engine.running) stop();
            else resetStage();
            break;
    }
}

// ------------------------------------------------------------------ deck --

function buildDeck() {
    deck = document.createElement('div');
    deck.id = 'demo-deck';

    const row = label => {
        const r = document.createElement('div');
        r.className = 'deck-row';
        const l = document.createElement('span');
        l.className = 'deck-label';
        l.textContent = label;
        r.appendChild(l);
        deck.appendChild(r);
        return r;
    };

    const btn = (parent, text, onClick) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = text;
        b.addEventListener('click', onClick);
        parent.appendChild(b);
        return b;
    };

    const sceneRow = row('scene');
    for (const [name, scene] of Object.entries(scenes))
        btn(sceneRow, scene.label ?? name, () => runScene(name));

    const camRow = row('cam');
    for (const name of Object.keys(camera.PRESETS))
        btn(camRow, name, () => camera.preset(name));

    const layerRow = row('layers');
    btn(layerRow, 'explode', () => layers.explode('auto'));
    btn(layerRow, 'collapse', () => layers.collapse());
    const spread = document.createElement('input');
    spread.type = 'range';
    spread.min = '30';
    spread.max = '300';
    spread.value = String(layers.getSpread());
    spread.addEventListener('input', () => layers.setSpread(Number(spread.value)));
    layerRow.appendChild(spread);

    const fxRow = row('fx');
    btn(fxRow, 'letterbox', function () {
        letterboxMode = letterboxMode === 'off' ? 'wide' : letterboxMode === 'wide' ? 'tall' : 'off';
        overlay.letterbox(letterboxMode);
        this.classList.toggle('active', letterboxMode !== 'off');
    });
    btn(fxRow, 'vignette', function () {
        vignetteOn = !vignetteOn;
        overlay.vignette(vignetteOn);
        this.classList.toggle('active', vignetteOn);
    });
    btn(fxRow, 'aurora', function () {
        auroraOn = !auroraOn;
        overlay.aurora(auroraOn);
        this.classList.toggle('active', auroraOn);
    });
    btn(fxRow, 'reset', () => resetStage());

    const hint = document.createElement('div');
    hint.className = 'deck-hint';
    hint.textContent = '0-7 scenes  q-t cam  x explode  z/c spread\nf focus-pick  s spot-pick  b bars  esc reset';
    deck.appendChild(hint);

    document.body.appendChild(deck);
}
