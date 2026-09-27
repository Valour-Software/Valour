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

async function computeGas(o, gw, gh, width, height) {
    return await runInWorker('gas', { options: o, gw, gh, width, height })
        ?? await computeOnMainThread(o, gw, gh, width, height);
}

export function init(canvas, options) {
    let disposed = false;
    let current = options;
    let renderToken = 0;
    let lastKey = '';

    async function render() {
        if (disposed) return;
        const rect = canvas.getBoundingClientRect();
        if (rect.width < 1 || rect.height < 1) return;

        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const width = snap(rect.width * ratio), height = snap(rect.height * ratio);
        const o = resolveOptions(current);
        const key = cacheKey(o, width, height);
        if (key === lastKey) return;
        lastKey = key;

        const token = ++renderToken;
        let bitmap = getCached(key);
        if (!bitmap) {
            const { gw, gh, resolution } = gasSize(o, width, height);
            const gas = await computeGas(o, gw, gh, width, height);
            if (disposed || token !== renderToken) return;
            const scratch = document.createElement('canvas');
            scratch.width = width;
            scratch.height = height;
            compose(scratch, o, gas, gw, gh, resolution);
            bitmap = await toBitmap(scratch);
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
