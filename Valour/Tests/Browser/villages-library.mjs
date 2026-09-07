import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const engines = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const engine = process.env.BROWSER_ENGINE || 'chromium';
const root = resolve(import.meta.dirname, '../../Client');
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser/library');
const references = resolve(process.env.VILLAGE_QA_REFERENCES || 'TestResults/village-browser/asset-review/references');
const recipes = JSON.parse(await readFile(resolve(import.meta.dirname, '../../..', 'Tools/Villages/curation.json'), 'utf8')).objects;
await mkdir(output, { recursive: true });
for (const entry of recipes) await readFile(`${references}/${entry.key}.png`).catch(() => {
    throw new Error(`Missing original reference for ${entry.key}. Run verify-library.py with --interiors and --reference-dir first.`);
});
const browser = await engines[engine].launch({ headless: true, ...(engine === 'chromium' && process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const page = await browser.newPage({ viewport: { width: 1200, height: 1050 }, deviceScaleFactor: 1 });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
await page.route('http://villages-library.test/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/') return route.fulfill({ contentType: 'text/html', body: '<style>body{margin:0;background:#17232b}canvas{display:block;image-rendering:pixelated}</style><canvas width="1200" height="1050"></canvas>' });
    const file = path === '/renderer.js' ? `${root}/Components/Windows/Villages/VillageWindowComponent.razor.js`
        : path === '/editor.js' ? `${root}/Components/Windows/Villages/TilesetDefinitionWindowComponent.razor.js`
        : path === '/manifest.json' ? `${root}/wwwroot/tilesets/exterior-tileset-0.json`
        : path === '/atlas.png' ? `${root}/wwwroot/media/villages/library-atlas.png`
        : path === '/atlas.vtex.bin' ? `${root}/wwwroot/media/villages/library-atlas.vtex.bin`
        : path.startsWith('/ts/') ? `${root}/wwwroot${path}`
        : path.startsWith('/references/') && /^[a-z0-9.-]+\.png$/.test(path.slice(12)) ? `${references}/${path.slice(12)}` : null;
    assert.ok(file, `Unexpected fixture resource: ${path}`);
    let body = await readFile(file);
    if (path === '/renderer.js' || path === '/editor.js') body = Buffer.from(body.toString().replaceAll('"../../../ts/', '"/ts/'));
    return route.fulfill({ contentType: path.endsWith('.bin') ? 'application/octet-stream' : path.endsWith('.png') ? 'image/png' : path.endsWith('.json') ? 'application/json' : 'text/javascript', body });
});
try {
    await page.goto('http://villages-library.test/');
    const result = await page.evaluate(async entries => {
        const { drawDecoration } = await import('/renderer.js');
        const manifest = await (await fetch('/manifest.json')).json();
        const { loadStandaloneImage } = await import('/ts/VillageTileRendering.js');
        const atlas = await loadStandaloneImage('/atlas.vtex.bin');
        const definitions = new Map(manifest.definitions.map(d => [d.Key, {
            key: d.Key, x: d.X, y: d.Y, width: d.Width, height: d.Height,
            footprintWidth: d.FootprintWidth, footprintHeight: d.FootprintHeight
        }]));
        const state = { renderCameraX: 0, renderCameraY: 0, textureCache: new Map([['/atlas.png', { image: atlas, loaded: true }]]),
            tilesets: new Map([['fixture', { imageUrl: '/atlas.png', tileSize: 16, definitions }]]), wallSets: new Map() };
        const map = { tilesetKey: 'fixture', decorations: [] };
        const original = new Map();
        for (const entry of entries) {
            const image = new Image(); image.src = `/references/${entry.key}.png`; await image.decode(); original.set(entry.key, image);
            const definition = definitions.get(entry.key);
            if (definition.width * 16 !== image.width || definition.height * 16 !== image.height)
                throw new Error(`Incomplete source dimensions: ${entry.key}`);
        }
        const actual = document.createElement('canvas'), expected = document.createElement('canvas');
        actual.width = expected.width = 512; actual.height = expected.height = 512;
        const a = actual.getContext('2d', { willReadFrequently: true }), e = expected.getContext('2d', { willReadFrequently: true });
        a.imageSmoothingEnabled = e.imageSmoothingEnabled = false;
        let comparisons = 0;
        for (const entry of entries) {
            const image = original.get(entry.key), [width, height] = entry.footprint;
            const lift = entry.layer === 'Surface' ? 0.5 : 0;
            const item = { definitionKey: entry.key, x: 5, y: 5, width, height, zIndex: lift ? 5 : entry.layer === 'Floor' ? -10 : 0 };
            map.decorations = [item];
            for (const px of [8, 16, 32, 48]) {
                a.fillStyle = e.fillStyle = '#a8947c'; a.fillRect(0, 0, 512, 512); e.fillRect(0, 0, 512, 512);
                drawDecoration(a, item, map, state, px);
                e.drawImage(image, (5 + (width - image.width / 16) / 2) * px,
                    (5 + height - lift - image.height / 16) * px, image.width * px / 16, image.height * px / 16);
                const aa = a.getImageData(0, 0, 512, 512).data, ee = e.getImageData(0, 0, 512, 512).data;
                for (let i = 0; i < aa.length; i++) if (aa[i] !== ee[i]) throw new Error(`Placed ${entry.key} differs from its complete original at scale ${px / 16}, pixel ${Math.floor(i / 4)}`);
                comparisons++;
            }
        }
        window.renderCatalogPage = start => {
            const canvas = document.querySelector('canvas'), ctx = canvas.getContext('2d');
            ctx.imageSmoothingEnabled = false; ctx.fillStyle = '#25343d'; ctx.fillRect(0, 0, canvas.width, canvas.height);
            entries.slice(start, start + 42).forEach((entry, index) => {
                const [width, height] = entry.footprint;
                const x = (index % 6) * 200, y = Math.floor(index / 6) * 150;
                const definition = definitions.get(entry.key), px = Math.min(32, 100 / definition.height, 170 / definition.width);
                const item = { definitionKey: entry.key, x: (x + 100) / px - width / 2,
                    y: (y + 110) / px - height + (entry.layer === 'Surface' ? 0.5 : 0), width, height,
                    zIndex: entry.layer === 'Surface' ? 5 : entry.layer === 'Floor' ? -10 : 0 };
                map.decorations = [item]; drawDecoration(ctx, item, map, state, px);
                ctx.fillStyle = 'white'; ctx.font = '12px sans-serif'; ctx.fillText(entry.name, x + 8, y + 130, 184);
            });
        };
        return { objects: entries.length, comparisons, scales: [0.5, 1, 2, 3] };
    }, recipes);
    for (let start = 0; start < recipes.length; start += 42) {
        await page.evaluate(start => window.renderCatalogPage(start), start);
        await page.screenshot({ path: `${output}/placed-catalog-${start / 42 + 1}.png` });
    }
    const validation = await page.evaluate(async () => {
        const { init } = await import('/editor.js');
        const canvas = document.createElement('canvas'); canvas.id = 'sprite-guard'; document.body.append(canvas);
        const input = document.createElement('input'); input.type = 'file'; input.id = 'sprite-upload'; document.body.append(input);
        const editor = init(canvas.id, input.id, { invokeMethodAsync: async () => {} }, null);
        try {
            await editor.loadImageUrl('/references/living.cabinet.glass.png');
            return { complete: editor.validateSprite(0, 0, 2, 3), clipped: editor.validateSprite(0, 1, 2, 2),
                outside: editor.validateSprite(0, 0, 3, 3) };
        } finally { editor.dispose(); canvas.remove(); input.remove(); }
    });
    assert.equal(validation.complete, '');
    assert.match(validation.clipped, /cuts through artwork/);
    assert.match(validation.outside, /beyond the source/);
    assert.deepEqual(errors, []);
    await writeFile(`${output}/results.json`, JSON.stringify({ ...result, editorSelection: validation, errors }, null, 2));
    console.log(`PASS ${result.comparisons} pixel comparisons: all ${result.objects} placed objects match complete source images at four scales`);
    console.log('PASS The editor accepts the complete cabinet and rejects its cropped header and an out-of-bounds selection');
} finally {
    await browser.close();
}
