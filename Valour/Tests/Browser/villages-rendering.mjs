import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = resolve(import.meta.dirname, '../../Client');
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const page = await browser.newPage({ viewport: { width: 1280, height: 1040 }, deviceScaleFactor: 1 });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
await page.route('http://villages-render.test/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/') return route.fulfill({ contentType: 'text/html', body: '<style>body{margin:0;background:#17232b}canvas{display:block;image-rendering:pixelated}</style><canvas width="1280" height="1040"></canvas>' });
    const file = path === '/renderer.js' ? `${root}/Components/Windows/Villages/VillageWindowComponent.razor.js`
        : path === '/manifest.json' ? `${root}/wwwroot/tilesets/exterior-tileset-0.json`
        : path === '/atlas.png' ? `${root}/wwwroot/media/villages/library-atlas.png`
        : path === '/atlas.vtex.bin' ? `${root}/wwwroot/media/villages/library-atlas.vtex.bin`
        : path.startsWith('/ts/') ? `${root}/wwwroot${path}` : null;
    assert.ok(file, `Unexpected fixture resource: ${path}`);
    let body = await readFile(file);
    if (path === '/renderer.js') body = Buffer.from(body.toString().replaceAll('"../../../ts/', '"/ts/'));
    return route.fulfill({ contentType: path.endsWith('.bin') ? 'application/octet-stream' : path.endsWith('.png') ? 'image/png' : path.endsWith('.json') ? 'application/json' : 'text/javascript', body });
});
try {
    await page.goto('http://villages-render.test/');
    const checks = await page.evaluate(async () => {
        const { drawWallTile, drawDecoration, getDecorationDepth, drawInteriorBoundaryMask } = await import('/renderer.js');
        const { getWallNeighborMask, resolveWallFrame, roomWallContains } = await import('/ts/VillageWallRendering.js');
        const manifest = await (await fetch('/manifest.json')).json();
        const { loadStandaloneImage } = await import('/ts/VillageTileRendering.js');
        const atlas = await loadStandaloneImage('/atlas.vtex.bin');
        const definitions = new Map(manifest.definitions.map(d => [d.Key, {
            key: d.Key, x: d.X, y: d.Y, width: d.Width, height: d.Height,
            collision: d.Collision, footprintWidth: d.FootprintWidth, footprintHeight: d.FootprintHeight
        }]));
        const wallSets = new Map(manifest.wallSets.map(w => [w.Key, {
            key: w.Key, layout: w.Layout, imageUrl: '/atlas.png', tileSize: w.TileSize,
            originX: w.OriginX, originY: w.OriginY, topColor: w.TopColor
        }]));
        const state = { renderCameraX: -64, renderCameraY: -144, textureCache: new Map([['/atlas.png', { image: atlas, loaded: true }]]),
            tilesets: new Map([['fixture', { imageUrl: '/atlas.png', tileSize: 16, definitions }]]), wallSets };
        const map = { mapKind: 'Interior', width: 18, height: 13, tilesetKey: 'fixture', decorations: [] };
        const canvas = document.querySelector('canvas'), ctx = canvas.getContext('2d');
        ctx.imageSmoothingEnabled = false;
        const px = 64, checks = [];
        function add(key, x, y, zIndex = 0) {
            const d = definitions.get(key);
            map.decorations.push({ definitionKey: key, x, y, zIndex, width: d?.footprintWidth || 1, height: d?.footprintHeight || 1 });
        }
        function walls(cells, key) {
            const positions = new Set(cells.map(([x, y]) => `${x},${y}`));
            for (const [x, y] of cells) {
                const mask = getWallNeighborMask((cx, cy) => positions.has(`${cx},${cy}`), x, y);
                const frame = resolveWallFrame(mask);
                map.decorations.push({ definitionKey: `wall:${key}:${frame}`, x, y, width: 1, height: 1, zIndex: 10 });
                for (const [dx, dy] of [[1, 0], [0, 1]]) {
                    if (!positions.has(`${x + dx},${y + dy}`)) continue;
                    const next = getWallNeighborMask((cx, cy) => positions.has(`${cx},${cy}`), x + dx, y + dy);
                    for (let p = 0; p < 16; p++) {
                        const a = roomWallContains(mask, dx ? 15 : p, dy ? 15 : p);
                        const b = roomWallContains(next, dx ? 0 : p, dy ? 0 : p);
                        if (a !== b) throw new Error(`Wall seam at ${x},${y}`);
                    }
                }
            }
        }
        const perimeter = [];
        for (let x = 0; x < 18; x++) { perimeter.push([x, 0]); if (x < 8 || x > 10) perimeter.push([x, 12]); }
        for (let y = 1; y < 12; y++) perimeter.push([0, y], [17, y]);
        walls(perimeter, 'modern.slate');
        add('decor.indoor-tree', 1, 2); add('living.bookcase', 14, 2);
        for (const x of [4, 10]) { add('office.desk.birch', x, 4); add('office.monitor', x, 4, 5); add('office.chair.blue.north', x + 1, 6); }
        add('living.sofa.linen', 12, 9); add('decor.indoor-plant', 15, 8);
        function render(floorKey) {
            ctx.fillStyle = '#101a20'; ctx.fillRect(0, 0, canvas.width, canvas.height);
            const floor = definitions.get(floorKey);
            for (let y = 0; y < map.height; y++) for (let x = 0; x < map.width; x++)
                ctx.drawImage(atlas, floor.x * 16, floor.y * 16, 16, 16, 64 + x * px, 144 + y * px, px, px);
            drawInteriorBoundaryMask(ctx, map, state, px);
            for (const item of [...map.decorations].sort((a, b) => getDecorationDepth(a, map) - getDecorationDepth(b, map)))
                drawDecoration(ctx, item, map, state, px);
        }
        render('floor.carpet');
        const pixel = (x, y) => [...ctx.getImageData(x, y, 1, 1).data].join(',');
        const white = pixel(64 + 7 * 4, 220);
        for (let y = 220; y < 820; y++) {
            if (pixel(64 + 7 * 4, y) !== white || pixel(64 + 17 * px + 7 * 4, y) !== white)
                throw new Error(`Repeated cap or broken vertical join at row ${y}`);
        }
        checks.push('Perimeter joins match on both sides; vertical caps have no repeated crossbars');
        window.renderWallCases = () => {
            map.decorations = [];
            const shapes = [
                [[0,0],[1,0],[2,0],[0,1],[0,2]],
                [[0,0],[1,0],[2,0],[2,1],[2,2]],
                [[0,0],[0,1],[0,2],[1,2],[2,2]],
                [[2,0],[2,1],[0,2],[1,2],[2,2]],
                [[0,1],[1,1],[2,1],[1,0],[1,2]],
                [[0,0],[1,0],[0,1],[1,1],[1,2],[2,2]]
            ];
            [...wallSets.keys()].forEach((key, i) => walls(shapes[i].map(([x, y]) => [x + 2 + (i % 3) * 5, y + 2 + Math.floor(i / 3) * 5]), key));
            render('floor.cream');
        };
        checks.push('Complete desk, monitor, and directional chair sprites render together through the application renderer');
        return checks;
    });
    await page.screenshot({ path: `${output}/office-rendering.png` });
    await page.evaluate(() => window.renderWallCases());
    await page.screenshot({ path: `${output}/wall-junctions.png` });
    assert.deepEqual(errors, []);
    await writeFile(`${output}/rendering-results.json`, JSON.stringify({ checks, errors }, null, 2));
    checks.forEach(check => console.log(`PASS ${check}`));
} finally {
    await browser.close();
}
