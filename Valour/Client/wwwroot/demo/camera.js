// The camera rig. Drives the app root (.mobile-holder) with a tweened
// perspective transform each frame, plus an optional idle drift oscillator
// layered on top so "static" shots still breathe.

import { engine, addTicker, tween, eases } from './engine.js';

const cam = {
    x: 0, y: 0,           // px translation
    scale: 1,
    rotX: 0, rotY: 0,     // degrees
    persp: 1600,          // px perspective distance
    drift: 0,             // 0..1 blend of the idle oscillator
};

let removeTicker = null;
let driftT = 0;

export const PRESETS = {
    default: { x: 0, y: 0, scale: 1, rotX: 0, rotY: 0, drift: 0 },
    left:    { x: 0, y: 0, scale: 0.88, rotX: 5, rotY: 14, drift: 0 },
    right:   { x: 0, y: 0, scale: 0.88, rotX: 5, rotY: -14, drift: 0 },
    low:     { x: 0, y: -12, scale: 0.9, rotX: 14, rotY: 0, drift: 0 },
    hero:    { x: 0, y: 0, scale: 0.9, rotX: 4, rotY: 7, drift: 1 },
};

export function init() {
    driftT = 0;
    removeTicker = addTicker((now, dt) => {
        const holder = engine.holder;
        if (!holder) return;

        driftT += dt / 1000;
        const d = cam.drift;
        const dRotY = d * Math.sin(driftT * 0.32) * 7;
        const dRotX = d * Math.sin(driftT * 0.21 + 1.3) * 2.5;
        const dScale = d * Math.sin(driftT * 0.18 + 0.6) * 0.012;

        holder.style.transform =
            `perspective(${cam.persp}px) ` +
            `translate3d(${cam.x.toFixed(2)}px, ${cam.y.toFixed(2)}px, 0) ` +
            `rotateX(${(cam.rotX + dRotX).toFixed(3)}deg) ` +
            `rotateY(${(cam.rotY + dRotY).toFixed(3)}deg) ` +
            `scale(${(cam.scale + dScale).toFixed(4)})`;

        // Show the floating-panel frame whenever we're meaningfully off the
        // default shot (not while exploded — layers.js owns the look there).
        const framed = !document.body.classList.contains('demo-exploded') &&
            (Math.abs(cam.rotX) + Math.abs(cam.rotY) > 0.5 || cam.scale < 0.985 || d > 0.01);
        document.body.classList.toggle('demo-framed', framed);
    });
}

export function destroy() {
    removeTicker?.();
    removeTicker = null;
    if (engine.holder) engine.holder.style.transform = '';
    document.body.classList.remove('demo-framed');
}

export function state() {
    return { ...cam };
}

export function moveTo(target, { ms = 2400, ease = eases.cine } = {}) {
    return tween(cam, target, { ms, ease });
}

export function preset(name, opts = {}) {
    const p = PRESETS[name] ?? PRESETS.default;
    return moveTo(p, { ms: opts.ms ?? 2400, ease: opts.ease ?? eases.cine });
}

export function reset(opts = {}) {
    return preset('default', opts);
}

export function drift(on, { ms = 1800 } = {}) {
    return tween(cam, { drift: on ? 1 : 0 }, { ms, ease: eases.inOut });
}

// Zoom the camera so `el` lands framed in the viewport at `scale`.
// Rotation is leveled out — a focus move is a flat, punchy zoom shot.
//
// anchor: 'center' | 'left' | 'right' | 'auto'. Wide elements (like the
// composer) overflow the viewport when scaled, so centering crops both
// ends — 'auto' pins the leading edge instead whenever the scaled element
// won't fit.
//
// Measurement trick: transform is cleared, layout is read, transform is
// restored — all synchronously inside one frame, so nothing ever paints
// untransformed and we get exact untransformed coordinates for the math.
export function focusOn(el, {
    scale = 1.55, ms = 2200, ease = eases.cine,
    offsetY = 0, anchor = 'auto', margin = 64,
} = {}) {
    const holder = engine.holder;
    if (!el || !holder) return Promise.resolve();

    const prev = holder.style.transform;
    holder.style.transform = 'none';
    const r = el.getBoundingClientRect();
    const h = holder.getBoundingClientRect();
    holder.style.transform = prev;

    if (anchor === 'auto')
        anchor = r.width * scale > innerWidth - margin * 2 ? 'left' : 'center';

    // Element anchor point (untransformed) and where it should land on screen
    const target = {
        x: anchor === 'left' ? r.left : anchor === 'right' ? r.right : r.left + r.width / 2,
        y: r.top + r.height / 2 + offsetY,
    };
    const vc = {
        x: anchor === 'left' ? margin : anchor === 'right' ? innerWidth - margin : innerWidth / 2,
        y: innerHeight / 2,
    };
    const origin = { x: h.left + h.width / 2, y: h.top + h.height / 2 };

    // Transform origin is the holder center: p' = O + s·(p − O) + t = vc
    const x = vc.x - origin.x - scale * (target.x - origin.x);
    const y = vc.y - origin.y - scale * (target.y - origin.y);

    return moveTo({ x, y, scale, rotX: 0, rotY: 0, drift: 0 }, { ms, ease });
}
