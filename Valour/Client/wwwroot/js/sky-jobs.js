// Shared plumbing for generated skies and planets: one module worker for the
// expensive per-pixel work, and a small cache of finished images for the session.

const MAX_CACHED = 32;
const cache = new Map();
let worker = null;
let workerFailed = false;
let nextJobId = 1;
const jobs = new Map();

function getWorker() {
    if (worker || workerFailed || typeof Worker === 'undefined')
        return worker;
    try {
        worker = new Worker(new URL('./sky-worker.js', import.meta.url), { type: 'module' });
        worker.onmessage = event => {
            const job = jobs.get(event.data.id);
            if (!job) return;
            jobs.delete(event.data.id);
            job.resolve(event.data.result);
        };
        worker.onerror = () => {
            workerFailed = true;
            worker?.terminate();
            worker = null;
            for (const job of jobs.values()) job.reject(new Error('Sky worker failed'));
            jobs.clear();
        };
    } catch {
        workerFailed = true;
        worker = null;
    }
    return worker;
}

// Runs a job in the worker. Resolves with null when no worker is available, so
// callers can fall back to computing on the main thread.
export async function runInWorker(type, payload) {
    const w = getWorker();
    if (!w) return null;
    try {
        return await new Promise((resolve, reject) => {
            const id = nextJobId++;
            jobs.set(id, { resolve, reject });
            w.postMessage({ id, type, payload });
        });
    } catch {
        return null;
    }
}

export function getCached(key) {
    return cache.get(key);
}

export function remember(key, bitmap) {
    cache.set(key, bitmap);
    while (cache.size > MAX_CACHED) {
        const oldest = cache.keys().next().value;
        cache.get(oldest)?.close?.();
        cache.delete(oldest);
    }
}

export async function toBitmap(canvas) {
    return typeof createImageBitmap === 'function' ? await createImageBitmap(canvas) : canvas;
}

// Sizes snap to 64px steps so small layout changes reuse cached images, and
// stop at a size every browser can allocate for a canvas.
const MAX_SKY_SIZE = 8192;

export function snap(value) {
    return Math.min(MAX_SKY_SIZE, Math.max(64, Math.ceil(value / 64) * 64));
}

// Calls render when the element's size settles after a change.
export function observeSize(element, render) {
    let timer = 0;
    const observer = typeof ResizeObserver === 'function'
        ? new ResizeObserver(() => {
            clearTimeout(timer);
            timer = setTimeout(render, 150);
        })
        : null;
    observer?.observe(element);
    return () => {
        clearTimeout(timer);
        observer?.disconnect();
    };
}
