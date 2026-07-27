// Synthetic-input primitives: a fake cursor that travels curved, humanized
// paths, click ripples, character-by-character typing, and smooth scrolling.
// All input is dispatched as real DOM events so the live app responds.

import { engine, addTicker, sleep, center, eases } from './engine.js';

let cursorEl = null;
const pos = { x: innerWidth / 2, y: innerHeight / 2 };

export function init() {
    cursorEl = document.createElement('div');
    cursorEl.id = 'demo-cursor';
    document.body.appendChild(cursorEl);
}

export function destroy() {
    cursorEl?.remove();
    cursorEl = null;
}

export function show() { if (cursorEl) cursorEl.style.opacity = '1'; }
export function hide() { if (cursorEl) cursorEl.style.opacity = '0'; }

// Quadratic-bezier glide with distance-scaled duration and a hint of curve,
// so moves read as human rather than robotic straight lines.
export async function moveTo(x, y) {
    if (!cursorEl) return;
    show();

    const from = { ...pos };
    const dist = Math.hypot(x - from.x, y - from.y);
    if (dist < 2) return;

    const ms = Math.min(1300, Math.max(320, dist * 1.1));
    const bend = Math.min(90, dist * 0.18) * (((from.x + from.y) | 0) % 2 ? 1 : -1);
    const mx = (from.x + x) / 2 - (y - from.y) / dist * bend;
    const my = (from.y + y) / 2 + (x - from.x) / dist * bend;

    await new Promise(resolve => {
        const start = performance.now();
        const remove = addTicker(now => {
            const t = Math.min(1, (now - start) / ms);
            const e = eases.inOut(t);
            const inv = 1 - e;
            pos.x = inv * inv * from.x + 2 * inv * e * mx + e * e * x;
            pos.y = inv * inv * from.y + 2 * inv * e * my + e * e * y;
            cursorEl.style.transform = `translate3d(${pos.x.toFixed(1)}px, ${pos.y.toFixed(1)}px, 0)`;
            if (t >= 1) { remove(); resolve(); }
        });
    });
    await sleep(60);
}

function dispatchPointerSequence(el, x, y, types) {
    for (const type of types) {
        const init = {
            bubbles: true, cancelable: true, view: window,
            clientX: x, clientY: y, button: 0,
            buttons: type.includes('down') ? 1 : 0,
        };
        if (type.startsWith('pointer'))
            el.dispatchEvent(new PointerEvent(type, { ...init, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
        else
            el.dispatchEvent(new MouseEvent(type, init));
    }
}

export async function clickEl(el) {
    if (!el) return false;
    el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    await sleep(350);
    const { x, y } = center(el);
    await moveTo(x, y);

    cursorEl?.classList.add('pressed');
    const ripple = document.createElement('div');
    ripple.className = 'demo-ripple';
    ripple.style.left = `${x}px`;
    ripple.style.top = `${y}px`;
    document.body.appendChild(ripple);
    setTimeout(() => ripple.remove(), 700);

    dispatchPointerSequence(el, x, y, ['pointerdown', 'mousedown']);
    await sleep(90);
    dispatchPointerSequence(el, x, y, ['pointerup', 'mouseup', 'click']);
    cursorEl?.classList.remove('pressed');
    await sleep(150);
    return true;
}

export async function typeInto(el, text) {
    if (!el) return false;
    await clickEl(el);
    el.focus();

    for (const char of text) {
        if (engine.aborted) return false;
        el.appendChild(document.createTextNode(char));
        el.dispatchEvent(new InputEvent('input', { bubbles: true, data: char, inputType: 'insertText' }));
        await sleep(35 + Math.random() * 75);
    }

    // Let the debounced input handler flush to Blazor
    await sleep(300);
    return true;
}

export function press(el, key) {
    el.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key, code: key }));
}

export async function smoothScroll(el, to, ms) {
    const from = el.scrollTop;
    const start = performance.now();
    return new Promise(resolve => {
        const remove = addTicker(now => {
            if (engine.aborted) { remove(); return resolve(); }
            const t = Math.min(1, (now - start) / ms);
            el.scrollTop = from + (to - from) * eases.inOut(t);
            if (t >= 1) { remove(); resolve(); }
        });
    });
}
