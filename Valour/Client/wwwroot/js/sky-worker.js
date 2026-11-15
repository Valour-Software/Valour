import { computeGas, compose } from './nebula.js';
import { renderPlanet } from './planet.js';

// Composes the finished sky here when the browser lets a worker draw to its own
// canvas. Returns null otherwise, and the page composes it from the gas pixels.
function composeSky(options, pixels, gw, gh, width, height, resolution) {
    if (typeof OffscreenCanvas !== 'function') return null;
    try {
        const canvas = new OffscreenCanvas(width, height);
        if (!compose(canvas, options, pixels, gw, gh, resolution, (w, h) => new OffscreenCanvas(w, h)))
            return null;
        return canvas.transferToImageBitmap();
    } catch {
        return null;
    }
}

self.onmessage = event => {
    const { id, type, payload } = event.data;
    if (type === 'sky') {
        const { options, gw, gh, width, height, resolution, composeInWorker } = payload;
        const pixels = computeGas(options, gw, gh, width, height);
        const bitmap = composeInWorker ? composeSky(options, pixels, gw, gh, width, height, resolution) : null;
        if (bitmap)
            self.postMessage({ id, result: { bitmap } }, [bitmap]);
        else
            self.postMessage({ id, result: { pixels } }, [pixels.buffer]);
    } else if (type === 'gas') {
        const { options, gw, gh, width, height } = payload;
        const pixels = computeGas(options, gw, gh, width, height);
        self.postMessage({ id, result: pixels }, [pixels.buffer]);
    } else if (type === 'planet') {
        const { genome, radius, lights, light } = payload;
        const result = renderPlanet(genome, radius, lights, light);
        self.postMessage({ id, result }, [result.pixels.buffer]);
    }
};
