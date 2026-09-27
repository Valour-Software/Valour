// The loading scene shown while the app downloads and signs in: a flight home
// through the brand sky. Stars stream past and slow down as loading finishes,
// and the home planet rises into the place it holds on the sign-in page.
//
// The sky and planet use the same modules and settings as the sign-in page, so
// their renders are cached and the sign-in page appears without a visible change.
//
// The .NET runtime keeps the main thread busy while it starts, so the stars are
// drawn in a worker when the browser allows it, and the planet and sky move with
// CSS transitions, which run without the main thread.

import { init as initSky } from '../Components/Utility/NebulaSky.razor.js';
import { init as initPlanet } from '../Components/Utility/PlanetGlobe.razor.js';
import { createFlight, runFlight } from './boot-flight.js';

const SKY = { seed: 'valour', palette: 'valour', intensity: 1.3, focusX: 0.72, focusY: 0.35, spread: 0.4, scale: 2.6, stars: 260 };
const PLANET = { seed: 'valour-home', lights: 1.4, radius: 0.5, centerX: 0.8, centerY: 1.19, kind: 'terra', plain: true, light: [0.12, -0.38, -0.92] };

const BLURBS = [
    'reticulating splines',
    'fetching victor treats',
    'surviving tsunamis',
    'hacking the mainframe',
    'preaching vooperism',
    'finding new interns',
    'solving the halting problem',
    'debugging the debugger',
    'simulating empires',
    'pretending to work',
    'not slacking',
    'drop-kicking bugs',
];

const root = document.getElementById('boot-scene');
if (root) start(root);

function startFlight(canvas, reducedMotion) {
    const size = () => ({ width: canvas.clientWidth, height: canvas.clientHeight, ratio: Math.min(window.devicePixelRatio || 1, 2) });

    if (typeof canvas.transferControlToOffscreen === 'function' && typeof Worker === 'function') {
        try {
            const worker = new Worker(new URL('./boot-flight-worker.js', import.meta.url), { type: 'module' });
            const offscreen = canvas.transferControlToOffscreen();
            worker.postMessage({ type: 'init', canvas: offscreen, reducedMotion, ...size() }, [offscreen]);
            return {
                resize: () => worker.postMessage({ type: 'resize', ...size() }),
                setProgress: value => worker.postMessage({ type: 'progress', value }),
                stop: () => worker.postMessage({ type: 'stop' }),
            };
        } catch (e) {
            console.warn('Loading scene is drawing on the main thread:', e);
        }
    }

    const flight = createFlight(canvas, reducedMotion);
    const s = size();
    flight.resize(s.width, s.height, s.ratio);
    const stop = runFlight(flight);
    return {
        resize: () => { const n = size(); flight.resize(n.width, n.height, n.ratio); },
        setProgress: value => flight.setProgress(value),
        stop,
    };
}

function start(root) {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const line = root.querySelector('.boot-line');
    const flightCanvas = root.querySelector('.boot-flight');
    const flight = startFlight(flightCanvas, reducedMotion);
    // The stars fade in from when they start moving, not from the first paint,
    // which can come well before this script loads on a slow connection.
    requestAnimationFrame(() => requestAnimationFrame(() => flightCanvas.classList.add('live')));

    let sky = null, planet = null;
    try {
        sky = initSky(root.querySelector('.boot-sky'), SKY);
        planet = initPlanet(root.querySelector('.boot-planet'), PLANET);
    } catch (e) {
        console.warn('Loading scene art unavailable:', e);
    }

    let progress = 0;
    let status = null;
    let finished = false;
    let blurb = BLURBS[Math.floor(Math.random() * BLURBS.length)];

    function render() {
        root.style.setProperty('--boot-p', progress.toFixed(3));
        flight.setProgress(progress);
        if (line) {
            line.textContent = status ? `${status} · ${blurb}` : `${Math.round(progress * 100)}% · ${blurb}`;
        }
    }

    function setProgress(value) {
        if (value <= progress) return;
        progress = value;
        render();
    }

    // Blazor reports download progress as an inline style on the root element.
    // Reading the inline value avoids recalculating styles.
    const readDownload = () => {
        const value = parseFloat(document.documentElement.style.getPropertyValue('--blazor-load-percentage'));
        if (Number.isFinite(value) && !status && !finished) setProgress(value / 100 * 0.85);
    };
    const observer = new MutationObserver(readDownload);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });

    const blurbTimer = setInterval(() => {
        blurb = BLURBS[Math.floor(Math.random() * BLURBS.length)];
        render();
    }, 2600);

    const onResize = () => flight.resize();
    window.addEventListener('resize', onResize);

    function finish() {
        if (finished) return;
        finished = true;
        observer.disconnect();
        clearInterval(blurbTimer);
        setProgress(1);
        root.classList.add('done');
        setTimeout(() => {
            flight.stop();
            sky?.dispose();
            planet?.dispose();
            window.removeEventListener('resize', onResize);
            root.remove();
        }, 900);
    }

    window.valourBoot = {
        status(text) {
            status = text;
            setProgress(0.9);
            render();
        },
        finish,
    };

    readDownload();
    render();

    if (window.__valourBootStatus) window.valourBoot.status(window.__valourBootStatus);
    if (window.__valourBootDone) finish();
}
