// Draws the generated skies and planets on the website. Canvases declare what
// they show with a data-sky or data-planet attribute holding JSON options, in the
// same shape the app's NebulaSky and PlanetGlobe components pass to these modules.
// An optional "narrow" object overrides options on small screens.

import { resolveOptions, gasSize, computeGasRows, compose } from './sky/nebula.js';
import { planetGenome, renderPlanet, DEFAULT_LIGHT } from './sky/planet.js';
import { runInWorker, toBitmap, snap, observeSize } from './sky/sky-jobs.js';

const NARROW_QUERY = window.matchMedia('(max-width: 760px)');
const MAX_PLANET_RADIUS = 420;

function pixelRatio() {
    return Math.min(window.devicePixelRatio || 1, 1.5);
}

function optionsFor(canvas, attribute) {
    const options = JSON.parse(canvas.getAttribute(attribute) || '{}');
    const narrow = options.narrow;
    delete options.narrow;
    return NARROW_QUERY.matches && narrow ? { ...options, ...narrow } : options;
}

async function computeGasOnMainThread(o, gw, gh, width, height) {
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

async function drawSky(canvas) {
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) return;
    const ratio = pixelRatio();
    const width = snap(rect.width * ratio), height = snap(rect.height * ratio);
    const o = resolveOptions(optionsFor(canvas, 'data-sky'));
    const { gw, gh, resolution } = gasSize(o, width, height);
    const gas = await runInWorker('gas', { options: o, gw, gh, width, height })
        ?? await computeGasOnMainThread(o, gw, gh, width, height);
    const scratch = document.createElement('canvas');
    scratch.width = width;
    scratch.height = height;
    compose(scratch, o, gas, gw, gh, resolution);
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d').drawImage(scratch, 0, 0);
}

async function drawPlanet(canvas) {
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) return;
    const ratio = pixelRatio();
    const width = Math.round(rect.width * ratio), height = Math.round(rect.height * ratio);
    const options = optionsFor(canvas, 'data-planet');
    const genome = planetGenome(options.seed);
    if (options.kind) genome.kind = options.kind;
    if (options.plain) {
        genome.rings = null;
        genome.moons = [];
    }
    const radius = Math.max(4, Math.round(height * (options.radius ?? 0.4)));
    const renderRadius = Math.min(radius, MAX_PLANET_RADIUS);
    const lights = options.lights ?? 0.6;
    const light = options.light ?? DEFAULT_LIGHT;
    const { pixels, size } = await runInWorker('planet', { genome, radius: renderRadius, lights, light })
        ?? renderPlanet(genome, renderRadius, lights, light);
    const scratch = document.createElement('canvas');
    scratch.width = size;
    scratch.height = size;
    scratch.getContext('2d').putImageData(new ImageData(pixels, size, size), 0, 0);
    const bitmap = await toBitmap(scratch);
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    const drawSize = size * radius / renderRadius;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(bitmap,
        Math.round(width * (options.centerX ?? 0.5) - drawSize / 2),
        Math.round(height * (options.centerY ?? 0.5) - drawSize / 2),
        drawSize, drawSize);
}

// Renders are queued one at a time so the hero finishes before the canvases
// further down the page start.
let queue = Promise.resolve();

function schedule(canvas, draw) {
    let lastSize = '';
    const run = () => {
        const rect = canvas.getBoundingClientRect();
        const size = `${Math.round(rect.width)}x${Math.round(rect.height)}|${NARROW_QUERY.matches}`;
        if (size === lastSize) return;
        lastSize = size;
        queue = queue.then(() => draw(canvas)).then(() => {
            canvas.dataset.ready = 'true';
        }).catch(() => {});
    };
    return run;
}

function start(canvas, draw) {
    const run = schedule(canvas, draw);
    const begin = () => {
        run();
        observeSize(canvas, run);
    };
    if (canvas.hasAttribute('data-eager') || typeof IntersectionObserver !== 'function') {
        begin();
        return;
    }
    const observer = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting)) {
            observer.disconnect();
            begin();
        }
    }, { rootMargin: '400px 0px' });
    observer.observe(canvas);
}

document.querySelectorAll('canvas[data-sky]').forEach(canvas => start(canvas, drawSky));
document.querySelectorAll('canvas[data-planet]').forEach(canvas => start(canvas, drawPlanet));

document.querySelectorAll('[data-planet-caption]').forEach(element => {
    const genome = planetGenome(element.getAttribute('data-planet-caption'));
    const parts = [genome.kind];
    if (genome.rings) parts.push('rings');
    if (genome.moons.length) parts.push(`${genome.moons.length} moon${genome.moons.length > 1 ? 's' : ''}`);
    parts.push(genome.palette);
    element.textContent = parts.join(' · ');
});
