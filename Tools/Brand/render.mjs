// Renders the brand masters (icons, social avatar, social card, wordmarks, Twitter
// header and the native splash) into media/socials. The pages in this folder draw
// with the app's own nebula, planet and Victor sources, which this script serves
// from the repository root.
//
// Needs Playwright. Set PLAYWRIGHT_MODULE to its path if it is not installed where
// Node can resolve it.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');

const root = normalize(join(dirname(fileURLToPath(import.meta.url)), '..', '..'));
const out = join(root, 'media', 'socials');
const types = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
    '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.css': 'text/css',
};

const server = createServer(async (request, response) => {
    const path = normalize(join(root, decodeURIComponent(new URL(request.url, 'http://x').pathname)));
    if (!path.startsWith(root)) {
        response.writeHead(403).end();
        return;
    }
    try {
        const body = await readFile(path);
        response.writeHead(200, { 'content-type': types[extname(path)] || 'application/octet-stream', 'cache-control': 'no-store' });
        response.end(body);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1500, height: 1200 } });

async function open(path) {
    await page.goto(`${origin}/Tools/Brand/${path}`);
    await page.waitForFunction(() => document.body.dataset.done === '1' || document.images.length > 0 && [...document.images].every(i => i.complete), null, { timeout: 120000 });
    await page.waitForTimeout(300);
}

const icons = [
    ['outline', 'icon-outline.png'],
    ['bleed', 'icon-full-bleed.png'],
    ['small', 'icon-small.png'],
    ['bg', 'icon-android-background.png'],
    ['avatar&k=1.8', 'social-avatar.png'],
];
for (const [variant, file] of icons) {
    await open(`icon.html?v=${variant}`);
    await page.locator('#c').screenshot({ path: join(out, file), omitBackground: true });
    console.log('rendered', file);
}

await open('card.html');
await page.locator('#card').screenshot({ path: join(out, 'social-card.png') });
await page.locator('#wm-dark').screenshot({ path: join(out, 'wordmark-dark.png'), omitBackground: true });
await page.locator('#wm-light').screenshot({ path: join(out, 'wordmark-light.png'), omitBackground: true });
console.log('rendered social-card.png, wordmark-dark.png, wordmark-light.png');

await open('header.html');
await page.locator('#h').screenshot({ path: join(out, 'twitter-header.png') });
console.log('rendered twitter-header.png');

await page.setViewportSize({ width: 1024, height: 1024 });
await open('splash.html');
await page.screenshot({ path: join(out, 'splash-victor.png'), omitBackground: true });
console.log('rendered splash-victor.png');

await browser.close();
server.close();
