// Cinematic overlay kit: spotlight, captions/title cards, feature callouts
// with leader lines, letterbox bars, vignette, and the animated aurora
// backdrop. Everything lives in fixed full-screen containers so it composites
// over (or behind) the 3D stage without touching app DOM.

import { engine, addTicker, sleep, resolve } from './engine.js';

const NS = 'http://www.w3.org/2000/svg';

let root = null;        // #demo-overlay
let svg = null;
let dimRect = null;     // spotlight dim layer
let spotHole = null;    // mask cutout
let spotRing = null;
let backdrop = null;    // #demo-backdrop (aurora)
let vignetteEl = null;
let bars = {};
let removeTicker = null;

const spot = { on: false, target: null, pad: 14, cur: null };
const callouts = new Map();   // el -> {box, line, dot, side, text}

export function init() {
    backdrop = document.createElement('div');
    backdrop.id = 'demo-backdrop';
    backdrop.innerHTML = '<div class="demo-aurora a"></div><div class="demo-aurora b"></div><div class="demo-aurora c"></div>';
    document.body.appendChild(backdrop);

    root = document.createElement('div');
    root.id = 'demo-overlay';
    document.body.appendChild(root);

    svg = document.createElementNS(NS, 'svg');
    const defs = document.createElementNS(NS, 'defs');
    const mask = document.createElementNS(NS, 'mask');
    mask.id = 'demo-spot-mask';

    const maskBg = document.createElementNS(NS, 'rect');
    maskBg.setAttribute('width', '100%');
    maskBg.setAttribute('height', '100%');
    maskBg.setAttribute('fill', 'white');

    spotHole = document.createElementNS(NS, 'rect');
    spotHole.setAttribute('fill', 'black');
    spotHole.setAttribute('rx', '12');

    mask.append(maskBg, spotHole);
    defs.appendChild(mask);

    dimRect = document.createElementNS(NS, 'rect');
    dimRect.setAttribute('class', 'demo-dim');
    dimRect.setAttribute('width', '100%');
    dimRect.setAttribute('height', '100%');
    dimRect.setAttribute('mask', 'url(#demo-spot-mask)');
    dimRect.setAttribute('opacity', '0');

    spotRing = document.createElementNS(NS, 'rect');
    spotRing.setAttribute('class', 'demo-spot-ring');
    spotRing.setAttribute('rx', '12');
    spotRing.setAttribute('opacity', '0');

    svg.append(defs, dimRect, spotRing);
    root.appendChild(svg);

    vignetteEl = document.createElement('div');
    vignetteEl.id = 'demo-vignette';
    root.appendChild(vignetteEl);

    for (const side of ['top', 'bottom', 'left', 'right']) {
        const bar = document.createElement('div');
        bar.className = `demo-bar ${side}`;
        root.appendChild(bar);
        bars[side] = bar;
    }

    removeTicker = addTicker(track);
}

export function destroy() {
    removeTicker?.();
    removeTicker = null;
    callouts.clear();
    root?.remove();
    backdrop?.remove();
    root = svg = dimRect = spotHole = spotRing = backdrop = vignetteEl = null;
    bars = {};
}

// Per-frame: spotlight follows its target (even through 3D moves, since
// getBoundingClientRect returns the projected quad), callout leader lines
// stay pinned to their layers.
function track() {
    if (spot.on && spot.target?.isConnected) {
        const r = spot.target.getBoundingClientRect();
        const goal = {
            x: r.left - spot.pad,
            y: r.top - spot.pad,
            w: r.width + spot.pad * 2,
            h: r.height + spot.pad * 2,
        };
        if (!spot.cur) spot.cur = { ...goal };
        const c = spot.cur;
        const k = 0.16;
        c.x += (goal.x - c.x) * k;
        c.y += (goal.y - c.y) * k;
        c.w += (goal.w - c.w) * k;
        c.h += (goal.h - c.h) * k;

        for (const rect of [spotHole, spotRing]) {
            rect.setAttribute('x', c.x.toFixed(1));
            rect.setAttribute('y', c.y.toFixed(1));
            rect.setAttribute('width', Math.max(0, c.w).toFixed(1));
            rect.setAttribute('height', Math.max(0, c.h).toFixed(1));
        }
    }

    for (const [el, co] of callouts) {
        if (!el.isConnected) { removeCallout(el); continue; }
        const r = el.getBoundingClientRect();
        const midY = r.top + r.height * co.anchor;
        const onLeft = co.side === 'left';

        const boxW = co.box.offsetWidth;
        const boxH = co.box.offsetHeight;
        const gap = 46;
        const bx = onLeft
            ? Math.max(10, r.left - gap - boxW)
            : Math.min(innerWidth - boxW - 10, r.right + gap);
        const by = Math.min(innerHeight - boxH - 10, Math.max(10, midY - boxH / 2));

        co.box.style.left = `${bx}px`;
        co.box.style.top = `${by}px`;

        const x1 = onLeft ? bx + boxW : bx;
        const y1 = by + boxH / 2;
        const x2 = onLeft ? r.left + 6 : r.right - 6;
        const y2 = midY;
        const cx = (x1 + x2) / 2;
        co.line.setAttribute('d', `M ${x1} ${y1} C ${cx} ${y1}, ${cx} ${y2}, ${x2} ${y2}`);
        co.dot.setAttribute('cx', x2);
        co.dot.setAttribute('cy', y2);
    }
}

// ------------------------------------------------------------- spotlight --

export function spotlight(target, { pad = 14 } = {}) {
    const el = resolve(target);
    if (!el) {
        spot.on = false;
        spot.target = null;
        spot.cur = null;
        dimRect?.setAttribute('opacity', '0');
        spotRing?.setAttribute('opacity', '0');
        return;
    }
    spot.on = true;
    spot.target = el;
    spot.pad = pad;
    if (!spot.cur) {
        // First reveal starts from a slightly inflated hole for a settle-in
        const r = el.getBoundingClientRect();
        spot.cur = { x: r.left - 120, y: r.top - 120, w: r.width + 240, h: r.height + 240 };
    }
    dimRect.setAttribute('opacity', '1');
    spotRing.setAttribute('opacity', '1');
}

// -------------------------------------------------------------- captions --

export async function caption({ title, sub = '', pos = 'center', hold = 2400 } = {}) {
    const el = document.createElement('div');
    el.className = `demo-caption ${pos}`;
    const t = document.createElement('div');
    t.className = 'demo-caption-title';
    t.textContent = title ?? '';
    el.appendChild(t);
    if (sub) {
        const s = document.createElement('div');
        s.className = 'demo-caption-sub';
        s.textContent = sub;
        el.appendChild(s);
    }
    root.appendChild(el);

    // double rAF so the entrance transition actually runs
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    el.classList.add('shown');
    await sleep(900 + hold);
    if (engine.aborted) { el.remove(); return; }
    el.classList.add('leaving');
    await sleep(650);
    el.remove();
}

// -------------------------------------------------------------- callouts --

export function callout(target, text, { side = 'auto', anchor = 0.5 } = {}) {
    const el = resolve(target);
    if (!el || callouts.has(el)) return;

    const r = el.getBoundingClientRect();
    const resolvedSide = side === 'auto'
        ? (r.left + r.width / 2 > innerWidth / 2 ? 'right' : 'left')
        : side;

    const box = document.createElement('div');
    box.className = 'demo-callout';
    box.textContent = text;
    root.appendChild(box);

    const line = document.createElementNS(NS, 'path');
    line.setAttribute('class', 'demo-leader');
    line.setAttribute('opacity', '0');
    const dot = document.createElementNS(NS, 'circle');
    dot.setAttribute('class', 'demo-leader-dot');
    dot.setAttribute('r', '3.5');
    dot.setAttribute('opacity', '0');
    svg.append(line, dot);

    callouts.set(el, { box, line, dot, side: resolvedSide, anchor, text });

    requestAnimationFrame(() => requestAnimationFrame(() => {
        box.classList.add('shown');
        line.style.transition = 'opacity 600ms ease';
        dot.style.transition = 'opacity 600ms ease';
        line.setAttribute('opacity', '1');
        dot.setAttribute('opacity', '1');
    }));
}

function removeCallout(el) {
    const co = callouts.get(el);
    if (!co) return;
    callouts.delete(el);
    co.box.classList.remove('shown');
    co.line.setAttribute('opacity', '0');
    co.dot.setAttribute('opacity', '0');
    setTimeout(() => { co.box.remove(); co.line.remove(); co.dot.remove(); }, 650);
}

export function clearCallouts() {
    for (const el of [...callouts.keys()]) removeCallout(el);
}

// ------------------------------------------------------- letterbox & mood --

// mode: 'off' | 'wide' (2.39:1 cinema bars) | 'tall' (9:16 pillars for shorts)
export function letterbox(mode = 'off') {
    const wideBar = Math.max(0, (innerHeight - innerWidth / 2.39) / 2);
    const tallBar = Math.max(0, (innerWidth - innerHeight * 9 / 16) / 2);

    bars.top.style.height = mode === 'wide' ? `${wideBar}px` : '0';
    bars.bottom.style.height = mode === 'wide' ? `${wideBar}px` : '0';
    bars.left.style.width = mode === 'tall' ? `${tallBar}px` : '0';
    bars.right.style.width = mode === 'tall' ? `${tallBar}px` : '0';
}

export function vignette(on) {
    vignetteEl?.classList.toggle('on', on);
}

export function aurora(on) {
    backdrop?.classList.toggle('on', on);
}
