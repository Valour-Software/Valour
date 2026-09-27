import { computeGas } from './nebula.js';
import { renderPlanet } from './planet.js';

self.onmessage = event => {
    const { id, type, payload } = event.data;
    if (type === 'gas') {
        const { options, gw, gh, width, height } = payload;
        const pixels = computeGas(options, gw, gh, width, height);
        self.postMessage({ id, result: pixels }, [pixels.buffer]);
    } else if (type === 'planet') {
        const { genome, radius, lights, light } = payload;
        const result = renderPlanet(genome, radius, lights, light);
        self.postMessage({ id, result }, [result.pixels.buffer]);
    }
};
