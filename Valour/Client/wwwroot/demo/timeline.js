// Declarative timeline runner. A scene is an array of steps; each step is
// either an async function (called with the full engine api) or an object
// with one verb key. This is what lets ad scripts live as plain data:
//
//   [
//     { letterbox: 'wide' },
//     { caption: { title: 'Your community. Your world.', sub: 'VALOUR' } },
//     { cam: { preset: 'hero' } },
//     { openPlanet: true },
//     { type: { target: '.textbox-inner', text: 'hello world' } },
//     { explode: 'chat' }, { wait: 3000 }, { collapse: true },
//   ]

import { engine, sleep, waitFor, resolve, find, findByText, findAll, visible } from './engine.js';
import * as camera from './camera.js';
import * as layers from './layers.js';
import * as overlay from './overlay.js';
import * as cursor from './cursor.js';
import { scenes } from './scenes.js';

// Everything a scene step function gets to work with
const api = {
    camera, layers, overlay, cursor, engine,
    helpers: { sleep, waitFor, resolve, find, findByText, findAll, visible },
};

async function ensureSidebarOpen() {
    // Mobile only: the burger toggle exists in the topbar
    const toggle = find('.sidebar-toggle');
    if (toggle && !find('.sidebar-container.sidebar-active .tabstrip'))
        await cursor.clickEl(toggle);
    await sleep(500);
}

async function selectSidebarTab(index) {
    const tabs = findAll('.tabstrip .item');
    if (tabs[index])
        await cursor.clickEl(tabs[index]);
    await sleep(700);
}

async function openPlanet(name) {
    await ensureSidebarOpen();
    await selectSidebarTab(0);
    const row = await waitFor(() => findByText('.planet-row', name ?? engine.opts.planet));
    if (!row) return;
    await cursor.clickEl(row);
    await waitFor(() => find('.textbox-inner'));
    await sleep(1200);
}

const verbs = {
    wait: ms => sleep(ms),

    log: msg => { console.log(`[demo] ${msg}`); },

    cam: async v => {
        if (typeof v === 'string') return camera.preset(v);
        if (v.focus) {
            const el = await waitFor(() => resolve(v.focus), v.timeout ?? 4000);
            return el ? camera.focusOn(el, v) : null;
        }
        if (v.preset) return camera.preset(v.preset, v);
        if (v.reset) return camera.reset(v);
        if (v.drift !== undefined) return camera.drift(!!v.drift, v);
        return camera.moveTo(v, v);
    },

    explode: v => layers.explode(typeof v === 'string' ? v : (v.preset ?? 'auto'),
        typeof v === 'object' ? v : {}),
    collapse: v => layers.collapse(typeof v === 'object' ? v : {}),
    spread: v => { layers.setSpread(v); },

    spotlight: async v => {
        if (!v) return overlay.spotlight(null);
        const target = typeof v === 'string' ? v : v.target;
        const el = await waitFor(() => resolve(target), 4000);
        if (el) overlay.spotlight(el, typeof v === 'object' ? v : {});
    },

    caption: v => overlay.caption(v),
    callout: async v => {
        const el = await waitFor(() => resolve(v.target), 4000);
        if (el) overlay.callout(el, v.text, v);
    },
    clearCallouts: () => overlay.clearCallouts(),
    letterbox: v => { overlay.letterbox(v === true ? 'wide' : v); },
    vignette: v => { overlay.vignette(!!v); },
    aurora: v => { overlay.aurora(!!v); },

    click: async v => {
        const el = await waitFor(() => resolve(v), v?.timeout ?? 6000);
        if (el) await cursor.clickEl(el);
    },

    type: async v => {
        const el = await waitFor(() => resolve(v.target ?? '.textbox-inner'), 6000);
        if (!el) return;
        await cursor.typeInto(el, v.text);
        if (v.send !== false) {
            const send = await waitFor(() => find('.send-wrapper'), 2500);
            if (send) await cursor.clickEl(send);
            else cursor.press(el, 'Enter');
        }
    },

    scroll: async v => {
        const el = await waitFor(() => resolve(v.target), 4000);
        if (!el) return;
        const to = v.to === 'end' ? Math.max(0, el.scrollHeight - el.clientHeight)
            : v.to === 'start' ? 0 : v.to;
        await cursor.smoothScroll(el, to, v.ms ?? 2200);
    },

    sidebar: () => ensureSidebarOpen(),
    tab: i => selectSidebarTab(i),
    openPlanet: v => openPlanet(typeof v === 'string' ? v : undefined),

    // Scene composition: { run: 'tabs' } plays another registered scene
    run: name => {
        const scene = scenes[name];
        if (!scene) { console.warn(`[demo] unknown scene: ${name}`); return; }
        return play(scene.steps);
    },
};

export async function play(steps) {
    for (const step of steps) {
        if (engine.aborted) return;

        if (typeof step === 'function') {
            await step(api);
            continue;
        }

        for (const [verb, value] of Object.entries(step)) {
            if (engine.aborted) return;
            // "async: true" on a step runs its verb without awaiting it
            // (e.g. start a camera move while a caption plays).
            if (verb === 'async') continue;
            const fn = verbs[verb];
            if (!fn) { console.warn(`[demo] unknown verb: ${verb}`); continue; }
            if (step.async) fn(value);
            else await fn(value);
        }
    }
}

export { ensureSidebarOpen, selectSidebarTab, openPlanet };
