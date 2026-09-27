import { planetGenome, renderPlanet, DEFAULT_LIGHT } from '../../js/planet.js';
import { runInWorker, getCached, remember, toBitmap, observeSize } from '../../js/sky-jobs.js';

// Lights are rounded so that small changes in the online count reuse the cached
// image instead of rendering the planet again.
// Hero-sized planets are rendered at a capped radius and scaled up, which keeps a
// single render predictable on slow devices.
const MAX_RENDER_RADIUS = 420;

function quantizeLights(value) {
    return Math.round(Math.max(0, Math.min(1.5, value ?? 0)) * 10) / 10;
}

// Scenes can pin the kind of world (a rock moon to stand on) or drop rings and
// moons when the planet is used as ground or a horizon.
function genomeFor(options) {
    const genome = planetGenome(options.seed);
    if (options.kind) genome.kind = options.kind;
    if (options.plain) {
        genome.rings = null;
        genome.moons = [];
    }
    return genome;
}

async function render(genome, radius, lights, light) {
    return await runInWorker('planet', { genome, radius, lights, light })
        ?? renderPlanet(genome, radius, lights, light);
}

export function init(canvas, options) {
    let disposed = false;
    let current = options;
    let renderToken = 0;
    let lastKey = '';

    async function draw() {
        if (disposed) return;
        const rect = canvas.getBoundingClientRect();
        if (rect.width < 1 || rect.height < 1) return;

        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const width = Math.round(rect.width * ratio), height = Math.round(rect.height * ratio);
        const radius = Math.max(4, Math.round(height * (current.radius ?? 0.4)));
        const renderRadius = Math.min(radius, MAX_RENDER_RADIUS);
        const lights = quantizeLights(current.lights);
        const light = current.light ?? DEFAULT_LIGHT;
        const key = ['planet', current.seed, current.kind ?? '', current.plain ? 1 : 0, renderRadius, lights, light.join(',')].join('|');
        const drawKey = `${key}|${width}x${height}|${current.centerX}|${current.centerY}`;
        if (drawKey === lastKey) return;
        lastKey = drawKey;

        const token = ++renderToken;
        let bitmap = getCached(key);
        if (!bitmap) {
            const { pixels, size } = await render(genomeFor(current), renderRadius, lights, light);
            if (disposed || token !== renderToken) return;
            const scratch = document.createElement('canvas');
            scratch.width = size;
            scratch.height = size;
            scratch.getContext('2d').putImageData(new ImageData(pixels, size, size), 0, 0);
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
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, width, height);
        const scale = radius / renderRadius, drawSize = bitmap.width * scale;
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(bitmap,
            Math.round(width * (current.centerX ?? 0.5) - drawSize / 2),
            Math.round(height * (current.centerY ?? 0.5) - drawSize / 2),
            drawSize, drawSize);
        canvas.dataset.ready = 'true';
    }

    const stopObserving = observeSize(canvas, draw);
    draw();

    return {
        update(options) {
            current = options;
            lastKey = '';
            draw();
        },
        dispose() {
            disposed = true;
            renderToken++;
            stopObserving();
        }
    };
}
