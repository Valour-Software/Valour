import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PLANET_KINDS, planetGenome, planetExtent, renderPlanet } from '../../Client/wwwroot/js/planet.js';
import { paletteFor } from '../../Client/wwwroot/js/nebula.js';

const ids = Array.from({ length: 400 }, (_, i) => String(47000000000000000n + BigInt(i) * 7919n));

test('a planet id always produces the same genome', () => {
    assert.deepEqual(planetGenome('12215159187308544'), planetGenome('12215159187308544'));
    assert.notDeepEqual(planetGenome('12215159187308544'), planetGenome('12215159187308545'));
});

test('each world variant of a planet produces a different genome', () => {
    const variants = ['12215159187308544', ...Array.from({ length: 20 }, (_, i) => `12215159187308544~${i + 1}`)];
    const signatures = new Set(variants.map(seed => JSON.stringify(planetGenome(seed))));
    assert.equal(signatures.size, variants.length);
});

test('genomes cover every kind of world and vary continuously', () => {
    const genomes = ids.map(planetGenome);
    for (const kind of PLANET_KINDS)
        assert.ok(genomes.some(g => g.kind === kind), `no ${kind} planet in the sample`);
    assert.ok(genomes.some(g => g.rings) && genomes.some(g => !g.rings));
    assert.ok(genomes.some(g => g.moons.length === 2) && genomes.some(g => g.moons.length === 0));
    const signatures = new Set(genomes.map(g => `${g.kind}|${g.hues.map(h => h.toFixed(1)).join(',')}|${g.spin.toFixed(3)}`));
    assert.equal(signatures.size, genomes.length, 'two planets share a genome');
});

test('a planet uses the same palette as its sky', () => {
    for (const id of ids.slice(0, 50))
        assert.equal(planetGenome(id).palette, paletteFor(id));
});

test('hues stay out of the muddy yellow-green range', () => {
    for (const g of ids.map(planetGenome))
        for (const h of g.hues.slice(0, 2))
            assert.ok(!(h > 95 && h < 150), `hue ${h} for a ${g.kind} planet`);
});

test('the extent covers rings and moons', () => {
    for (const g of ids.map(planetGenome)) {
        const extent = planetExtent(g);
        assert.ok(extent >= 1.25);
        if (g.rings) assert.ok(extent > g.rings.outer);
        for (const m of g.moons) assert.ok(extent > m.distance + m.size);
    }
});

test('rendering is deterministic, sized to the extent, and transparent around the planet', () => {
    const g = planetGenome('12215159187308544');
    const first = renderPlanet(g, 24, 0.8);
    const second = renderPlanet(g, 24, 0.8);
    assert.equal(first.size, Math.ceil(2 * 24 * planetExtent(g)));
    assert.deepEqual(first.pixels, second.pixels);
    assert.equal(first.pixels[3], 0, 'the corner should be transparent');
    const center = ((first.size >> 1) * first.size + (first.size >> 1)) * 4;
    assert.equal(first.pixels[center + 3], 255, 'the planet should be opaque');
});

test('the night side is brighter when more people are online', () => {
    const id = ids.map(id => [id, planetGenome(id)]).find(([, g]) => g.kind === 'terra' && !g.rings && g.moons.length === 0)[0];
    const g = planetGenome(id);
    const brightness = lights => renderPlanet(g, 40, lights, [0.9, 0, 0.1]).pixels.reduce((sum, v, i) => i % 4 === 3 ? sum : sum + v, 0);
    assert.ok(brightness(1.2) > brightness(0));
});

test('every planet is bright enough on its lit side to stand out from the sky', () => {
    const toLinear = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
    const litLuminance = g => {
        const radius = 20, { pixels, size } = renderPlanet({ ...g, rings: null, moons: [] }, radius, 0, [-1, 0, 0.35]);
        const center = size / 2, limit = (radius * g.size * 0.8) ** 2;
        let sum = 0, count = 0;
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                const dx = x + 0.5 - center, dy = y + 0.5 - center;
                if (dx * dx + dy * dy > limit || dx > -radius * 0.2) continue;
                const k = (y * size + x) * 4;
                sum += 0.2126 * toLinear(pixels[k]) + 0.7152 * toLinear(pixels[k + 1]) + 0.0722 * toLinear(pixels[k + 2]);
                count++;
            }
        }
        return sum / count;
    };
    for (let i = 0; i < 600; i++) {
        const g = planetGenome(String(47000000000000000n + BigInt(i) * 104729n));
        const value = litLuminance(g);
        assert.ok(value >= 0.1, `${g.kind} planet has lit-side luminance ${value.toFixed(3)}`);
    }
});
