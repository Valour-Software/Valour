import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PALETTES, PLANET_PALETTES, hashSeed, paletteFor, resolveOptions, cacheKey, gasSize, computeGas, computeGasRows } from '../../Client/wwwroot/js/nebula.js';

test('the brand palette is not assigned to planets', () => {
    assert.ok(PALETTES.valour);
    assert.ok(!PLANET_PALETTES.includes('valour'));
    for (let i = 0; i < 500; i++)
        assert.notEqual(paletteFor(`${12215159187308544n + BigInt(i)}`), 'valour');
});

test('palettes are chosen deterministically from the seed', () => {
    assert.equal(hashSeed('12215159187308544'), hashSeed('12215159187308544'));
    assert.equal(paletteFor('12215159187308544'), paletteFor('12215159187308544'));
    const used = new Set(Array.from({ length: 200 }, (_, i) => paletteFor(`planet-${i}`)));
    assert.ok(used.size > PLANET_PALETTES.length / 2, 'seeds should spread across palettes');
});

test('resolveOptions keeps named palettes and falls back for unknown names', () => {
    assert.equal(resolveOptions({ seed: 'a', palette: 'Carina' }).palette, 'carina');
    const fallback = resolveOptions({ seed: 'a', palette: 'not-a-nebula' });
    assert.equal(fallback.palette, paletteFor('a'));
    assert.deepEqual(fallback.hues, PALETTES[fallback.palette]);
    assert.equal(resolveOptions({ seed: 'a', intensity: null }).intensity, 1);
});

test('the cache key changes with anything that changes the image', () => {
    const o = resolveOptions({ seed: 'a', palette: 'eagle' });
    assert.equal(cacheKey(o, 640, 320), cacheKey(resolveOptions({ seed: 'a', palette: 'eagle' }), 640, 320));
    assert.notEqual(cacheKey(o, 640, 320), cacheKey(o, 704, 320));
    assert.notEqual(cacheKey(o, 640, 320), cacheKey(resolveOptions({ seed: 'b', palette: 'eagle' }), 640, 320));
    assert.notEqual(cacheKey(o, 640, 320), cacheKey(resolveOptions({ seed: 'a', palette: 'helix' }), 640, 320));
});

test('large skies are computed within the pixel budget', () => {
    const o = resolveOptions({ seed: 'a' });
    const { gw, gh, resolution } = gasSize(o, 3840, 2160, 320000);
    assert.ok(gw * gh <= 320000);
    assert.ok(resolution > o.resolution);
    assert.deepEqual(gasSize(o, 400, 200, 320000), { gw: 200, gh: 100, resolution: o.resolution });
});

test('the gas field is deterministic and matches when computed in slices', () => {
    const o = resolveOptions({ seed: '12215159187308544', palette: 'rosette' });
    const whole = computeGas(o, 48, 24, 96, 48);
    assert.deepEqual(computeGas(o, 48, 24, 96, 48), whole);

    const sliced = new Uint8ClampedArray(48 * 24 * 4);
    for (let row = 0; row < 24; row += 5)
        computeGasRows(o, 48, 24, 96, 48, sliced, row, Math.min(24, row + 5));
    assert.deepEqual(sliced, whole);

    for (let k = 3; k < whole.length; k += 4)
        assert.equal(whole[k], 255);
    assert.notDeepEqual(computeGas(resolveOptions({ seed: 'other', palette: 'rosette' }), 48, 24, 96, 48), whole);
});

test('palette choices match the values pinned in NebulaPaletteTests', () => {
    const vectors = [
        ['12215159187308544', 1317615479, 'tarantula'],
        ['47213464583929856', 3563017055, 'tarantula'],
        ['planet-7', 3238120247, 'veil'],
        ['12215159187308544~1', 1301529070, 'eagle'],
        ['', 2166136261, 'orion']
    ];
    for (const [seed, hash, palette] of vectors) {
        assert.equal(hashSeed(seed), hash);
        assert.equal(paletteFor(seed), palette);
    }
    assert.deepEqual(PLANET_PALETTES, ['carina', 'eagle', 'rosette', 'crab', 'helix', 'veil', 'lagoon', 'orion', 'tarantula']);
    assert.deepEqual(PALETTES.carina, [205, 280, 350]);
    assert.deepEqual(PALETTES.crab, [30, 60, 210]);
    assert.deepEqual(PALETTES.tarantula, [180, 290, 45]);
    assert.deepEqual(PALETTES.valour, [300, 265, 205]);
});
