// Demo engine core: shared state, the render loop, easing, tweens, and DOM
// helpers used by every other module. No Valour-specific knowledge lives here.

export const engine = {
    active: false,
    running: false,   // a scene is currently playing
    aborted: false,   // set by Escape / stop() — scenes and tweens bail out
    holder: null,     // the app root the camera drives (.mobile-holder)
    opts: {},         // parsed query options (planet, scene, autoplay)
    mouseX: innerWidth / 2,
    mouseY: innerHeight / 2,
};

// ------------------------------------------------------------------ loop --

const tickers = new Set();
let rafId = 0;
let lastNow = 0;

function frame(now) {
    const dt = Math.min(64, now - lastNow || 16);
    lastNow = now;
    for (const t of [...tickers]) {
        try { t(now, dt); }
        catch (e) { console.error('[demo] ticker failed', e); tickers.delete(t); }
    }
    rafId = requestAnimationFrame(frame);
}

export function addTicker(fn) {
    tickers.add(fn);
    if (!rafId) rafId = requestAnimationFrame(frame);
    return () => tickers.delete(fn);
}

export function stopLoop() {
    tickers.clear();
    cancelAnimationFrame(rafId);
    rafId = 0;
}

// ---------------------------------------------------------------- easing --

export const eases = {
    linear: t => t,
    out: t => 1 - Math.pow(1 - t, 3),
    inOut: t => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2),
    // Long settle — the default "cinematic" camera feel
    cine: t => (t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.pow(-2 * t + 2, 5) / 2),
    outBack: t => 1 + 2.7 * Math.pow(t - 1, 3) + 1.7 * Math.pow(t - 1, 2),
};

// ----------------------------------------------------------------- tween --

// Tween numeric keys of `state` toward `target`. Starting a new tween on the
// same state object cancels the previous one (it resolves early, mid-value),
// which makes camera moves interruptible without snapping.
const activeTweens = new Map();

export function tween(state, target, { ms = 1200, ease = eases.cine, onDone } = {}) {
    activeTweens.get(state)?.();

    const keys = Object.keys(target).filter(k => typeof target[k] === 'number');
    const from = {};
    for (const k of keys) from[k] = state[k] ?? 0;

    return new Promise(resolve => {
        const start = performance.now();
        let removed = false;

        const finish = () => {
            if (removed) return;
            removed = true;
            remove();
            activeTweens.delete(state);
            onDone?.();
            resolve();
        };

        activeTweens.set(state, finish);

        const remove = addTicker(now => {
            const t = Math.min(1, (now - start) / ms);
            const v = ease(t);
            for (const k of keys) state[k] = from[k] + (target[k] - from[k]) * v;
            if (t >= 1) finish();
        });
    });
}

export function cancelTweens() {
    for (const cancel of [...activeTweens.values()]) cancel();
    activeTweens.clear();
}

// ------------------------------------------------------------ DOM helpers --

export function sleep(ms) {
    return new Promise(r => setTimeout(r, ms));
}

export function visible(el) {
    return el && el.offsetParent !== null && el.getClientRects().length > 0;
}

export function find(selector) {
    return [...document.querySelectorAll(selector)].find(visible) ?? null;
}

export function findAll(selector) {
    return [...document.querySelectorAll(selector)].filter(visible);
}

export function findByText(selector, text) {
    const lower = text.toLowerCase();
    return [...document.querySelectorAll(selector)]
        .filter(visible)
        .find(el => (el.textContent ?? '').toLowerCase().includes(lower)) ?? null;
}

export async function waitFor(getter, timeoutMs = 8000) {
    const start = performance.now();
    while (performance.now() - start < timeoutMs) {
        if (engine.aborted) return null;
        const el = getter();
        if (el) return el;
        await sleep(120);
    }
    console.warn('[demo] waitFor timed out');
    return null;
}

export function center(el) {
    const r = el.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

// Resolve a selector, element, or {text, sel} descriptor to a visible element.
export function resolve(target) {
    if (!target) return null;
    if (target instanceof Element) return target;
    if (typeof target === 'string') return find(target);
    if (target.text) return findByText(target.sel ?? '*', target.text);
    if (target.sel) return find(target.sel);
    return null;
}
