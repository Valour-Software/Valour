import { test } from 'node:test';
import assert from 'node:assert/strict';

// Context menu submenu boundary correction: computeSubmenuTranslate must
// keep submenus fully inside the viewport on all four edges, including the
// bottom edge (#1622 - submenus hang off screen when their parent button
// is near the bottom, since they anchor via `bottom: -50%`).
const checks = [];
const check = (name, actual, expected) => checks.push([name, actual, expected]);

const { computeSubmenuTranslate } = await import('../../Client/ContextMenu/ContextMenuRoot.razor.js');

const windowWidth = 1280;
const windowHeight = 720;

check('1. submenu fully in bounds gets no correction',
  computeSubmenuTranslate({ right: 500, top: 100, bottom: 300 }, windowWidth, windowHeight),
  { translateX: 0, translateY: 0 });

check('2. submenu overflowing the right edge is pulled left by the overflow plus margin',
  computeSubmenuTranslate({ right: 1300, top: 100, bottom: 300 }, windowWidth, windowHeight),
  { translateX: -30, translateY: 0 });

check('3. submenu overflowing the top edge is pushed down by the overflow plus margin',
  computeSubmenuTranslate({ right: 500, top: -15, bottom: 300 }, windowWidth, windowHeight),
  { translateX: 0, translateY: 25 });

check('4. submenu overflowing the bottom edge is pulled up by the overflow plus margin (#1622)',
  computeSubmenuTranslate({ right: 500, top: 650, bottom: 760 }, windowWidth, windowHeight),
  { translateX: 0, translateY: -50 });

check('5. right and bottom overflow are corrected independently at once',
  computeSubmenuTranslate({ right: 1320, top: 650, bottom: 780 }, windowWidth, windowHeight),
  { translateX: -50, translateY: -70 });

check('6. top-edge overflow takes precedence over bottom-edge overflow',
  computeSubmenuTranslate({ right: 500, top: -5, bottom: 900 }, windowWidth, windowHeight),
  { translateX: 0, translateY: 15 });

check('7. custom margin is honored',
  computeSubmenuTranslate({ right: 500, top: 100, bottom: 740 }, windowWidth, windowHeight, 0),
  { translateX: 0, translateY: -20 });

for (const [name, actual, expected] of checks) {
    test(name, () => assert.deepEqual(actual, expected));
}
