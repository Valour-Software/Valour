// Scene registry. Order matters: the digit hotkeys 0-9 map to registry
// order, and the deck renders buttons from it. Scenes are plain step arrays
// (see timeline.js) — write new ad scripts here or live via
// window.valourDemo.play([...]) in the console.

export const scenes = {

    // 0 — the full product reel, now with cinematic bookends
    reel: {
        label: 'Full Reel',
        steps: [
            { aurora: true },
            { letterbox: 'wide' },
            { cam: { preset: 'low', ms: 2600 }, async: true },
            { caption: { title: 'Your community.\nYour world.', sub: 'V A L O U R', hold: 2200 } },
            { letterbox: 'off' },
            { run: 'tabs' },
            { run: 'planet' },
            { run: 'messages' },
            { run: 'channels' },
            { explode: { preset: 'auto', ms: 1800 } },
            { wait: 3600 },
            { collapse: true },
            { letterbox: 'wide' },
            { caption: { title: 'Valour', sub: 'OPEN SOURCE • valour.gg', hold: 2600 } },
            { letterbox: 'off' },
            { aurora: false },
        ],
    },

    // 1 — sidebar tab tour
    tabs: {
        label: 'Tab Tour',
        steps: [
            { sidebar: true },
            { cam: { preset: 'left' } },
            { tab: 1 }, { wait: 650 },
            { tab: 3 }, { wait: 650 },
            { tab: 2 }, { wait: 650 },
            { tab: 0 }, { wait: 650 },
            { cam: { reset: true } },
        ],
    },

    // 2 — open the target planet from the sidebar
    planet: {
        label: 'Open Planet',
        steps: [
            { openPlanet: true },
        ],
    },

    // 3 — type and send a couple of messages
    messages: {
        label: 'Messages',
        steps: [
            { cam: { preset: 'right' } },
            { wait: 1200 },
            { type: { text: 'Valour is looking incredible lately' } },
            { wait: 1100 },
            { type: { text: 'Native apps, real-time sync, and it is all open source' } },
            { wait: 1100 },
            { cam: { reset: true } },
        ],
    },

    // 4 — channel list scroll + category open/close
    channels: {
        label: 'Channels',
        steps: [
            { sidebar: true },
            { tab: 2 },
            { cam: { preset: 'left' } },
            { scroll: { target: '.full-channel-list', to: 'end', ms: 2600 } },
            { wait: 500 },
            { scroll: { target: '.full-channel-list', to: 'start', ms: 2200 } },
            async ({ cursor, engine, helpers: { find, visible, sleep } }) => {
                const list = find('.full-channel-list');
                if (!list) return;
                const categories = [...list.querySelectorAll('.channel')]
                    .filter(visible)
                    .filter(el => el.querySelector('.channel-icon[class*="bi-folder"]'))
                    .slice(0, 2);
                for (const category of categories) {
                    if (engine.aborted) return;
                    await cursor.clickEl(category);
                    await sleep(950);
                    await cursor.clickEl(category);
                    await sleep(950);
                }
            },
            { cam: { preset: 'hero' } },
        ],
    },

    // 5 — exploded shell beauty shot (hold, then collapse)
    shell3d: {
        label: 'Shell 3D',
        steps: [
            { aurora: true },
            { explode: { preset: 'shell', ms: 1800 } },
            { wait: 4200 },
            { collapse: true },
        ],
    },

    // 6 — exploded chat view with feature callouts
    chat3d: {
        label: 'Chat 3D',
        steps: [
            { aurora: true },
            { explode: { preset: 'chat', ms: 1800, spread: 130 } },
            { wait: 4800 },
            { collapse: true },
        ],
    },

    // 7 — a ~30s ad: hook, product, feature focus, 3D finale, end card
    ad: {
        label: 'Ad 30s',
        steps: [
            { aurora: true },
            { vignette: true },
            { letterbox: 'wide' },
            { cam: { preset: 'hero', ms: 3000 }, async: true },
            { caption: { title: 'Communities,\nreimagined.', sub: 'V A L O U R', hold: 2000 } },
            { letterbox: 'off' },
            { openPlanet: true },
            { cam: { focus: '.textbox-holder', scale: 1.7, ms: 2000 } },
            { type: { text: 'This is where your community lives ✨' } },
            { wait: 900 },
            { cam: { reset: true, ms: 1800 } },
            { spotlight: { target: '.chat-scroll-region', pad: 10 } },
            { wait: 2200 },
            { spotlight: null },
            { explode: { preset: 'chat', ms: 1900, spread: 140 } },
            { wait: 4200 },
            { collapse: true },
            { letterbox: 'wide' },
            { caption: { title: 'Valour', sub: 'FREE • OPEN SOURCE • VALOUR.GG', hold: 3000 } },
            { letterbox: 'off' },
            { vignette: false },
            { aurora: false },
        ],
    },
};
