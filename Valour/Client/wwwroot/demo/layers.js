// Exploded layer mode: lifts live UI regions apart in Z while the camera
// orbits, dev-tools-3D style — except the app keeps running inside the
// layers (messages stream, presence updates, animations play).
//
// Layer discovery is procedural: a grid of hit-tests (elementsFromPoint)
// across the holder finds every visually meaningful element — visible, big
// enough, and either painting a background, holding direct content, or
// scrolling content. Each element is scored by how many qualifying elements
// paint beneath it on average (its paint stratum), scores are bucketed into
// at most `layers` strata, and every element is lifted by its stratum.
// Nested elements lift relative to their nearest lifted ancestor, so a
// window rises and its chat region rises further out of it. Any screen, any
// moment — no curated selector lists required (though named presets remain
// for scripted, deterministic ads).
//
// All Z offsets are >= 0: negative Z would put layers behind the app's
// opaque z=0 background planes, where true 3D depth sorting hides them
// entirely (this is what used to swallow the sidebar).
//
// The other hard part is that translateZ only survives if every ancestor
// between the app root and the layer preserves 3D and doesn't clip or
// flatten (overflow, filter, clip-path, contain, opacity < 1 all flatten).
// We fix the exact ancestor chains at runtime, remember what we touched,
// and restore everything on collapse.

import { engine, find, sleep } from './engine.js';
import * as camera from './camera.js';
import * as overlay from './overlay.js';

// Curated layer sets, back-to-front, for scripted scenes that need
// deterministic framing. First visible selector wins; missing layers skip.
export const PRESETS = {
    shell: [
        { sel: ['.sidebar-container'], label: 'Communities' },
        { sel: ['.topbar'], label: 'Quick Actions' },
        { sel: ['.window-dock'], label: 'Your Workspace' },
    ],
    chat: [
        { sel: ['.sidebar-container'], label: 'Communities' },
        { sel: ['.tab-wrapper'], label: 'Multi-Window Tabs' },
        { sel: ['.chat-scroll-region'], label: 'Real-Time Chat' },
        { sel: ['.role-list-wrapper'], label: 'Members & Roles' },
        { sel: ['.textbox-holder'], label: 'Rich Composer' },
    ],
};

// Friendly names for procedurally-discovered layers that match known
// regions. First match wins, so keep specific selectors before generic ones.
const LABEL_HINTS = [
    ['.sidebar-container, .sidebar', 'Communities'],
    ['.tab-wrapper, .tabstrip', 'Multi-Window Tabs'],
    ['.chat-scroll-region, .chat-holder', 'Real-Time Chat'],
    ['.role-list-wrapper, .role-list', 'Members & Roles'],
    ['.textbox-holder, .textbox', 'Rich Composer'],
    ['.full-channel-list', 'Channels'],
    ['.topbar', 'Quick Actions'],
    ['.window', 'Your Workspace'],
];

const state = {
    exploded: false,
    entries: [],        // { el, base, layer, parentLayer, label, prev, index }
    touched: new Map(), // ancestor -> saved inline style props
    spread: 90,
    holderPrev: null,
};

export function isExploded() {
    return state.exploded;
}

// ------------------------------------------------------------- discovery --

const MIN_AREA = 4500;
const MIN_DIM = 36;
const MAX_ELEMENTS = 36;

function bgAlpha(color) {
    const m = /rgba?\(\s*[\d.]+\s*,\s*[\d.]+\s*,\s*[\d.]+\s*(?:,\s*([\d.]+))?\)/.exec(color);
    if (!m) return color === 'transparent' ? 0 : 1;
    return m[1] === undefined ? 1 : parseFloat(m[1]);
}

function paintsSomething(cs) {
    return bgAlpha(cs.backgroundColor) > 0.02 ||
        cs.backgroundImage !== 'none' ||
        cs.boxShadow !== 'none';
}

function hasDirectContent(el) {
    if (['IMG', 'VIDEO', 'CANVAS', 'svg', 'SVG', 'PICTURE'].includes(el.tagName)) return true;
    for (const n of el.childNodes)
        if (n.nodeType === Node.TEXT_NODE && n.textContent.trim()) return true;
    return false;
}

// True only when the element genuinely hides scrolled content. A few px of
// overflow under `hidden` is layout slack (e.g. .channel-and-topbar carries
// ~30px), and treating that as a scroller would absorb the whole workspace
// into one atomic card.
function isScroller(el, cs) {
    if (el.scrollTop > 2 || el.scrollLeft > 2) return true;
    const scrollable = o => o === 'auto' || o === 'scroll';
    if (scrollable(cs.overflowY) && el.scrollHeight > el.clientHeight + 4) return true;
    if (scrollable(cs.overflowX) && el.scrollWidth > el.clientWidth + 4) return true;
    if (cs.overflowY === 'hidden' && el.scrollHeight > el.clientHeight + 60) return true;
    if (cs.overflowX === 'hidden' && el.scrollWidth > el.clientWidth + 60) return true;
    return false;
}

// Grid raycast over the holder. Returns [{el, score, area, depth}] where
// score is the element's average paint stratum among selected elements.
// Exported so ad scripts (and debugging) can inspect what would lift.
export function discover({ step = 48, layers = 6 } = {}) {
    const holder = engine.holder;
    const hr = holder.getBoundingClientRect();
    const x0 = Math.max(8, hr.left), x1 = Math.min(innerWidth - 8, hr.right);
    const y0 = Math.max(8, hr.top), y1 = Math.min(innerHeight - 8, hr.bottom);

    const cache = new Map();  // el -> {ok, scroller}
    const qualify = el => {
        let q = cache.get(el);
        if (q) return q;
        q = { ok: false, scroller: false };
        cache.set(el, q);

        if (!(el instanceof Element)) return q;
        if (el === holder || !holder.contains(el)) return q;
        if (el.closest('#demo-deck, #demo-overlay, #demo-cursor, #demo-backdrop')) return q;

        const r = el.getBoundingClientRect();
        if (r.width < MIN_DIM || r.height < MIN_DIM || r.width * r.height < MIN_AREA) return q;

        const cs = getComputedStyle(el);
        if (cs.visibility === 'hidden' || parseFloat(cs.opacity) < 0.05) return q;

        q.scroller = isScroller(el, cs);
        q.ok = q.scroller || paintsSomething(cs) || hasDirectContent(el);
        return q;
    };

    // Pass 1: collect the qualifying stack at every grid point
    const stacks = [];
    const hits = new Map();   // el -> hit count
    for (let y = y0; y <= y1; y += step) {
        for (let x = x0; x <= x1; x += step) {
            const stack = document.elementsFromPoint(x, y).filter(el => qualify(el).ok);
            if (!stack.length) continue;
            stacks.push(stack);
            for (const el of stack) hits.set(el, (hits.get(el) ?? 0) + 1);
        }
    }

    // Selection: drop elements trapped inside a scroll container (their clip
    // cannot be removed without scrolled-away content bleeding out, so the
    // scroller explodes as one atomic card), then cap by painted area.
    let selected = [...hits.keys()].filter(el => {
        for (let n = el.parentElement; n && n !== holder; n = n.parentElement)
            if (cache.get(n)?.scroller && cache.get(n)?.ok) return false;
        return true;
    });
    selected.sort((a, b) => {
        const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
        return rb.width * rb.height - ra.width * ra.height;
    });
    selected = selected.slice(0, MAX_ELEMENTS);
    const chosen = new Set(selected);

    // Pass 2: paint-depth score = mean position from the bottom, counted
    // among chosen elements only
    const stats = new Map();  // el -> {sum, n}
    for (const stack of stacks) {
        const q = stack.filter(el => chosen.has(el));  // top -> bottom
        for (let i = 0; i < q.length; i++) {
            const el = q[i];
            const s = stats.get(el) ?? { sum: 0, n: 0 };
            s.sum += q.length - 1 - i;
            s.n++;
            stats.set(el, s);
        }
    }

    // Bucket rounded scores into at most `layers` strata
    const scored = selected.map(el => ({
        el,
        score: Math.round((stats.get(el)?.sum ?? 0) / (stats.get(el)?.n || 1)),
        depth: domDepth(el),
    }));
    const strata = [...new Set(scored.map(s => s.score))].sort((a, b) => a - b);
    for (const s of scored)
        s.layer = Math.min(strata.indexOf(s.score), layers - 1);

    // Ancestors must be processed before descendants (style capture order)
    scored.sort((a, b) => a.depth - b.depth);
    return scored;
}

function domDepth(el) {
    let d = 0;
    for (let n = el.parentElement; n; n = n.parentElement) d++;
    return d;
}

// ---------------------------------------------------------- 3D chain fix --

const CHAIN_PROPS = ['transformStyle', 'overflow', 'overflowX', 'overflowY',
    'filter', 'backdropFilter', 'clipPath', 'contain', 'isolation', 'opacity'];

function fixChain(el) {
    const holder = engine.holder;
    for (let n = el.parentElement; n; n = n.parentElement) {
        if (!state.touched.has(n)) {
            const saved = {};
            for (const p of CHAIN_PROPS) saved[p] = n.style[p];
            state.touched.set(n, saved);

            const cs = getComputedStyle(n);
            n.style.transformStyle = 'preserve-3d';
            if (cs.overflowX !== 'visible' || cs.overflowY !== 'visible')
                n.style.overflow = 'visible';
            if (cs.filter !== 'none') n.style.filter = 'none';
            if (cs.backdropFilter && cs.backdropFilter !== 'none')
                n.style.backdropFilter = 'none';
            if (cs.clipPath !== 'none') n.style.clipPath = 'none';
            if (cs.contain && cs.contain !== 'none') n.style.contain = 'none';
            if (cs.isolation === 'isolate') n.style.isolation = 'auto';
            if (parseFloat(cs.opacity) < 1) n.style.opacity = '1';
        }
        if (n === holder) break;
    }
}

// Compose our Z lift with the transform the element had before we touched
// it. The base is captured once at explode time — reading it later would
// include our own lift.
function layerTransform(base, z) {
    return `translateZ(${z}px)` + (base !== 'none' ? ` ${base}` : '');
}

// Z is relative to the nearest lifted ancestor's plane (transforms nest),
// so subtract its stratum; never negative — see header note.
function zFor(entry) {
    return Math.max(0, (entry.layer - entry.parentLayer) * state.spread);
}

// ----------------------------------------------------------------- lift --

export async function explode(presetName = 'auto', opts = {}) {
    const holder = engine.holder;
    if (!holder || state.exploded) return;

    const { ms = 1600, stagger = 90, tilt = true, labels = true, spread, layers = 6 } = opts;
    state.spread = spread ?? (presetName === 'auto' ? 80 : 110);

    // Build the lift list: [{el, layer, label}]
    let lifts = [];
    if (presetName === 'auto') {
        for (const s of discover({ step: opts.step, layers })) {
            const hint = LABEL_HINTS.find(([sel]) => s.el.matches(sel));
            lifts.push({ el: s.el, layer: s.layer, label: hint?.[1] });
        }
    } else {
        const defs = Array.isArray(presetName) ? presetName : PRESETS[presetName];
        if (!defs?.length) return;
        for (const [i, def] of defs.entries()) {
            const sels = Array.isArray(def.sel) ? def.sel : [def.sel];
            const el = sels.map(find).find(Boolean);
            if (el && !lifts.some(l => l.el === el))
                lifts.push({ el, layer: i, label: def.label });
        }
    }
    if (!lifts.length) return;

    state.exploded = true;
    document.body.classList.add('demo-exploded');

    state.holderPrev = { transformStyle: holder.style.transformStyle };
    holder.style.transformStyle = 'preserve-3d';

    const byEl = new Map(lifts.map(l => [l.el, l]));
    const hasLiftedDescendant = el =>
        lifts.some(l => l.el !== el && el.contains(l.el));
    const nearestLiftedAncestor = el => {
        for (let n = el.parentElement; n && n !== holder; n = n.parentElement)
            if (byEl.has(n)) return byEl.get(n);
        return null;
    };

    let maxLayer = 0;
    for (const lift of lifts) {
        const { el, layer, label } = lift;
        fixChain(el);
        const cs = getComputedStyle(el);
        const base = cs.transform;
        const parentLayer = nearestLiftedAncestor(el)?.layer ?? 0;

        const entry = {
            el, label, layer, parentLayer, base,
            prev: {
                transform: el.style.transform,
                transition: el.style.transition,
                overflow: el.style.overflow,
                transformStyle: el.style.transformStyle,
            },
        };
        state.entries.push(entry);
        maxLayer = Math.max(maxLayer, layer);

        // The chain fix removes ancestor clips (they would flatten 3D), so a
        // layer must clip its own content or scrolled-away messages bleed
        // outside the card. Exception: a layer with lifted descendants must
        // NOT clip — overflow != visible flattens their 3D. (Safe: scroller
        // contents are absorbed during discovery, so such a layer never
        // hides scrolled-away content.)
        if (hasLiftedDescendant(el)) {
            el.style.transformStyle = 'preserve-3d';
            if (cs.overflowX !== 'visible' || cs.overflowY !== 'visible')
                el.style.overflow = 'visible';
        } else if (cs.overflowX === 'visible' || cs.overflowY === 'visible') {
            el.style.overflow = 'hidden';
        }

        el.classList.add('demo-layer-card');
        el.style.transition = `transform ${ms}ms cubic-bezier(0.22, 1, 0.36, 1) ${layer * stagger}ms`;
        // Force a style flush so the transition picks up the starting state
        void el.offsetWidth;
        el.style.transform = layerTransform(base, zFor(entry));
    }

    if (tilt)
        camera.moveTo({ rotY: -17, rotX: 9, scale: 0.75, x: 0, y: 0, drift: 0.35 },
            { ms: Math.max(ms, 2000) });

    if (labels) {
        // Let the layers separate before pinning labels to them
        sleep(ms * 0.6).then(() => {
            if (!state.exploded || engine.aborted) return;
            for (const e of state.entries)
                if (e.label) overlay.callout(e.el, e.label, { side: 'auto' });
        });
    }

    await sleep(ms + stagger * (maxLayer + 1));
}

export async function collapse(opts = {}) {
    if (!state.exploded) return;
    const { ms = 1200 } = opts;

    overlay.clearCallouts();

    const entries = state.entries;
    const maxLayer = Math.max(0, ...entries.map(e => e.layer));
    for (const e of entries) {
        const delay = (maxLayer - e.layer) * 60;
        e.el.style.transition = `transform ${ms}ms cubic-bezier(0.22, 1, 0.36, 1) ${delay}ms`;
        e.el.style.transform = layerTransform(e.base, 0);
    }

    camera.moveTo({ rotY: 0, rotX: 0, scale: 1, x: 0, y: 0, drift: 0 }, { ms: ms + 400 });

    await sleep(ms + (maxLayer + 1) * 60 + 50);
    restore();
}

// Immediate restore of every style we touched — used by collapse, Escape,
// and cleanup. Safe to call twice. Entries restore in reverse so descendant
// styles unwind before their ancestors'.
export function restore() {
    for (const e of [...state.entries].reverse()) {
        e.el.classList.remove('demo-layer-card');
        e.el.style.transform = e.prev.transform;
        e.el.style.transition = e.prev.transition;
        e.el.style.overflow = e.prev.overflow;
        e.el.style.transformStyle = e.prev.transformStyle;
    }
    state.entries = [];

    for (const [n, saved] of state.touched)
        for (const p of CHAIN_PROPS) n.style[p] = saved[p];
    state.touched.clear();

    if (engine.holder && state.holderPrev) {
        engine.holder.style.transformStyle = state.holderPrev.transformStyle;
        state.holderPrev = null;
    }

    document.body.classList.remove('demo-exploded');
    state.exploded = false;
}

// Live-adjust layer separation (deck slider / z & c hotkeys)
export function setSpread(px) {
    state.spread = Math.max(20, Math.min(400, px));
    for (const e of state.entries) {
        e.el.style.transition = 'transform 500ms cubic-bezier(0.22, 1, 0.36, 1)';
        e.el.style.transform = layerTransform(e.base, zFor(e));
    }
    return state.spread;
}

export function getSpread() {
    return state.spread;
}
