import { resolveOptions, cacheKey, gasSize, computeGasRows, compose } from '../../js/nebula.js';
import { runInWorker, getCached, remember, toBitmap, snap, observeSize } from '../../js/sky-jobs.js';

// Fallback when workers are unavailable: compute in slices so a large sky never
// blocks input for more than a few milliseconds at a time.
async function computeOnMainThread(o, gw, gh, width, height) {
    const pixels = new Uint8ClampedArray(gw * gh * 4);
    let row = 0;
    while (row < gh) {
        const started = performance.now();
        while (row < gh && performance.now() - started < 8) {
            computeGasRows(o, gw, gh, width, height, pixels, row, row + 1);
            row++;
        }
        await new Promise(resolve => setTimeout(resolve, 0));
    }
    return pixels;
}

// Renders a finished sky. The worker computes the gas and, where it can draw to
// a canvas of its own, composes the whole image, so the page only draws the
// result. Hosts whose web view crashes on worker canvases set a flag to keep
// the composing step on the page.
async function renderSky(o, width, height) {
    const { gw, gh, resolution } = gasSize(o, width, height);
    const composeInWorker = !window.valourDisableOffscreenCanvasWorkers;
    const result = await runInWorker('sky', { options: o, gw, gh, width, height, resolution, composeInWorker });
    if (result?.bitmap) return result.bitmap;

    const gas = result?.pixels ?? await computeOnMainThread(o, gw, gh, width, height);
    const scratch = document.createElement('canvas');
    scratch.width = width;
    scratch.height = height;
    compose(scratch, o, gas, gw, gh, resolution);
    return await toBitmap(scratch);
}

export function init(canvas, options) {
    let disposed = false;
    let current = options;
    let renderToken = 0;
    let lastKey = '';

    async function render() {
        if (disposed) return;
        // The layout size, not the box on screen: a sky that a CSS transform
        // scales keeps one size, and one render, while the transform animates.
        const boxWidth = canvas.clientWidth, boxHeight = canvas.clientHeight;
        if (boxWidth < 1 || boxHeight < 1) return;

        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const width = snap(boxWidth * ratio), height = snap(boxHeight * ratio);
        const o = resolveOptions(current);
        const key = cacheKey(o, width, height);
        if (key === lastKey) return;
        lastKey = key;

        const token = ++renderToken;
        let bitmap = getCached(key);
        if (!bitmap) {
            bitmap = await renderSky(o, width, height);
            if (disposed) {
                bitmap.close?.();
                return;
            }
            remember(key, bitmap);
            if (token !== renderToken) return;
        }

        canvas.width = width;
        canvas.height = height;
        canvas.getContext('2d').drawImage(bitmap, 0, 0);
        canvas.dataset.ready = 'true';
    }

    const stopObserving = observeSize(canvas, render);
    render();

    return {
        update(options) {
            current = options;
            lastKey = '';
            render();
        },
        dispose() {
            disposed = true;
            renderToken++;
            stopObserving();
        }
    };
}
