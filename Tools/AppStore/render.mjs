// Builds the captioned App Store screenshots in screenshots/framed from the
// captures in screenshots/raw.
//
//   PLAYWRIGHT_REQUIRE_FROM=/path/to/a/package.json node render.mjs
//
// Needs Playwright with WebKit. Set PLAYWRIGHT_REQUIRE_FROM to a package.json
// path whose node_modules contains playwright if it is not installed here.
import { createRequire } from 'module';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(process.env.PLAYWRIGHT_REQUIRE_FROM ?? path.join(here, 'package.json'));
const { webkit } = require('playwright');

const font = fs.readFileSync(path.join(here, '../../Valour/Client/wwwroot/css/fonts/instrument-sans-latin.woff2')).toString('base64');
const data = (file) => `data:image/png;base64,${fs.readFileSync(file).toString('base64')}`;

const shots = [
  ['iphone-1-chat', 'Chat with your communities', 'Every message is end-to-end encrypted'],
  ['iphone-2-planets', 'All your planets in one place', 'Communities built around what you love'],
  ['iphone-3-threads', 'Threads worth coming back to', 'Posts from every planet you join'],
  ['iphone-4-channels', 'Organized your way', 'Channels, threads, a wiki and voice'],
  ['iphone-5-wiki', 'A wiki for every community', 'Guides, rules and resources in one place'],
  ['iphone-6-onboarding', 'Find your people', 'Pick your interests and jump right in'],
  ['ipad-1-chat', 'Chat with your communities', 'Every message is end-to-end encrypted'],
  ['ipad-2-threads', 'Threads worth coming back to', 'Posts from every planet you join'],
  ['ipad-3-onboarding', 'Find your people', 'Pick your interests and jump right in'],
];

const sizes = {
  iphone: { w: 1320, h: 2868, title: 118, sub: 56, top: 150, deviceTop: 600, deviceW: 1000, radius: 96 },
  ipad: { w: 2064, h: 2752, title: 120, sub: 60, top: 120, deviceTop: 420, deviceW: 1700, radius: 60 },
};

const html = (img, title, subtitle, s) => `<!doctype html><html><head><style>
  @font-face { font-family: 'Instrument Sans'; src: url(data:font/woff2;base64,${font}) format('woff2'); font-weight: 400 700; }
  * { margin: 0; box-sizing: border-box; }
  body { width: ${s.w}px; height: ${s.h}px; overflow: hidden; font-family: 'Instrument Sans', sans-serif;
    background:
      radial-gradient(${s.w * 0.9}px ${s.h * 0.35}px at 10% 0%, rgba(0, 250, 255, 0.20), transparent 60%),
      radial-gradient(${s.w * 0.9}px ${s.h * 0.4}px at 100% 100%, rgba(191, 6, 253, 0.26), transparent 60%),
      #040d14; }
  .caption { position: absolute; top: ${s.top}px; left: 8%; right: 8%; text-align: center; }
  h1 { font-size: ${s.title}px; line-height: 1.08; font-weight: 700; color: #fff; letter-spacing: -0.02em; }
  p { margin-top: ${s.sub * 0.6}px; font-size: ${s.sub}px; font-weight: 500; color: rgba(255,255,255,0.72); }
  .device { position: absolute; top: ${s.deviceTop}px; left: 50%; width: ${s.deviceW}px; transform: translateX(-50%);
    border-radius: ${s.radius}px; overflow: hidden; border: 6px solid rgba(255,255,255,0.14);
    box-shadow: 0 60px 160px rgba(0,0,0,0.6), 0 0 120px rgba(0,250,255,0.14); }
  .device img { display: block; width: 100%; }
</style></head><body>
  <div class="caption"><h1>${title}</h1><p>${subtitle}</p></div>
  <div class="device"><img src="${img}"></div>
</body></html>`;

fs.mkdirSync(path.join(here, 'screenshots/framed'), { recursive: true });
const browser = await webkit.launch();
for (const [name, title, subtitle] of shots) {
  const s = sizes[name.startsWith('ipad') ? 'ipad' : 'iphone'];
  const page = await browser.newPage({ viewport: { width: s.w, height: s.h }, deviceScaleFactor: 1 });
  await page.setContent(html(data(path.join(here, 'screenshots/raw', `${name}.png`)), title, subtitle, s), { waitUntil: 'load' });
  await page.waitForTimeout(300);
  // Playwright writes RGBA PNGs. Convert them to RGB before uploading (see README).
  await page.screenshot({ path: path.join(here, 'screenshots/framed', `${name}.png`) });
  await page.close();
  console.log('wrote', name);
}
await browser.close();
