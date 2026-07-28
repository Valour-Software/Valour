import { test } from 'node:test';
import assert from 'node:assert/strict';

// Terrain autotiling: the resolver turns a grid of terrain keys into concrete
// tile definitions by looking at which materials touch. These tests pin the
// side rule (who draws the fringe), the bitmask ladder, and the fail-soft
// fallbacks that keep a partially-authored ruleset rendering.
import {
    normalizeTileDefinitions,
    normalizeTerrainDefinitions,
    buildTerrainIndex,
    resolveTerrainCell,
    resolveTerrainGrid
} from '../../Client/wwwroot/ts/VillageTileRendering.js';

const TERRAINS = normalizeTerrainDefinitions([
    { Key: 'grass', Name: 'Grass', Priority: 0 },
    { Key: 'tall', Name: 'Tall Grass', Priority: 10 },
    { Key: 'dark', Name: 'Dark Grass', Priority: 15 },
    { Key: 'path', Name: 'Dirt Path', Priority: 20 }
]);

function makeDef(key, terrain) {
    return {
        Kind: 'Tile',
        Name: key,
        Key: key,
        X: 0,
        Y: 0,
        Width: 1,
        Height: 1,
        Collision: [false],
        TerrainKey: terrain.key ?? '',
        TerrainRole: terrain.role ?? 'Base',
        TerrainDirection: terrain.direction ?? 'None',
        TerrainAgainst: terrain.against ?? '',
        TerrainWeight: terrain.weight ?? 1
    };
}

function makeTransitionDefs(terrainKey, against, { inners = [] } = {}) {
    const defs = [];
    for (const direction of ['N', 'E', 'S', 'W']) {
        defs.push(makeDef(`${terrainKey}.edge.${direction.toLowerCase()}`, {
            key: terrainKey, role: 'Edge', direction, against
        }));
    }
    for (const direction of ['NE', 'SE', 'SW', 'NW']) {
        defs.push(makeDef(`${terrainKey}.corner.${direction.toLowerCase()}`, {
            key: terrainKey, role: 'Corner', direction, against
        }));
    }
    for (const direction of inners) {
        defs.push(makeDef(`${terrainKey}.inner.${direction.toLowerCase()}`, {
            key: terrainKey, role: 'InnerCorner', direction, against
        }));
    }
    return defs;
}

const DEFINITIONS = normalizeTileDefinitions([
    makeDef('grass.base', { key: 'grass', weight: 9 }),
    makeDef('grass.tuft', { key: 'grass', role: 'Variant', weight: 1 }),
    makeDef('path.base', { key: 'path', weight: 9 }),
    makeDef('path.pebbles', { key: 'path', weight: 1 }),
    // Path fringes against anything (wildcard); inner corner only for NE.
    ...makeTransitionDefs('path', '', { inners: ['NE'] }),
    // Dark grass fringes specifically against path.
    makeDef('dark.base', { key: 'dark' }),
    ...makeTransitionDefs('dark', 'path'),
    // Tall grass fringes specifically against plain grass.
    makeDef('tall.base', { key: 'tall' }),
    ...makeTransitionDefs('tall', 'grass')
]);

const INDEX = buildTerrainIndex(TERRAINS, DEFINITIONS);

// Grid helper: rows of single chars, '.' = unpainted.
const CHAR_TERRAINS = { g: 'grass', p: 'path', d: 'dark', t: 'tall', x: 'lava' };
function grid(rows) {
    const cells = rows.join('').split('').map(c => c === '.' ? '' : CHAR_TERRAINS[c]);
    return { cells, width: rows[0].length, height: rows.length };
}

function keyAt(g, x, y) {
    return resolveTerrainCell(g.cells, g.width, g.height, x, y, INDEX)?.key ?? null;
}

test('normalization: keyless terrains dropped, aliases mapped', () => {
    assert.equal(normalizeTerrainDefinitions([{ Name: 'nameless' }, null, 'junk']).length, 0);
    assert.equal(normalizeTerrainDefinitions('not-an-array').length, 0);

    const [def] = normalizeTileDefinitions([
        makeDef('aliased', { key: 't', role: 'OuterCorner', direction: 'UpRight' })
    ]);
    assert.equal(def.terrainRole, 'Corner');
    assert.equal(def.terrainDirection, 'NE');

    const [variant] = normalizeTileDefinitions([makeDef('v', { key: 't', role: 'Variant', direction: 'Up' })]);
    assert.equal(variant.terrainRole, 'Base');
    assert.equal(variant.terrainDirection, 'N');
});

test('index auto-creates terrains referenced only by tiles', () => {
    const index = buildTerrainIndex([], normalizeTileDefinitions([makeDef('solo.base', { key: 'solo' })]));
    assert.ok(index.get('solo'));
    assert.equal(index.get('solo').terrain.priority, 0);
});

test('a path patch in grass resolves edges and outer corners', () => {
    const g = grid([
        'ggggg',
        'gpppg',
        'gpppg',
        'gpppg',
        'ggggg'
    ]);
    assert.equal(keyAt(g, 2, 2), 'path.base');
    assert.equal(keyAt(g, 2, 1), 'path.edge.n');
    assert.equal(keyAt(g, 2, 3), 'path.edge.s');
    assert.equal(keyAt(g, 1, 2), 'path.edge.w');
    assert.equal(keyAt(g, 3, 2), 'path.edge.e');
    assert.equal(keyAt(g, 1, 1), 'path.corner.nw');
    assert.equal(keyAt(g, 3, 1), 'path.corner.ne');
    assert.equal(keyAt(g, 1, 3), 'path.corner.sw');
    assert.equal(keyAt(g, 3, 3), 'path.corner.se');
    // The grass side of the boundary stays base: only one side fringes.
    assert.equal(keyAt(g, 2, 0), 'grass.base');
});

test('concave corner picks inner art when present, base when missing', () => {
    const ne = grid([
        'ppg',
        'ppp',
        'ppp'
    ]);
    assert.equal(keyAt(ne, 1, 1), 'path.inner.ne');

    const sw = grid([
        'ppp',
        'ppp',
        'gpp'
    ]);
    // No SW inner authored: fail-soft renders the base tile, not a hole.
    assert.equal(keyAt(sw, 1, 1), 'path.base');
});

test('unmatched shapes fall back to an edge, then base', () => {
    // 1-wide strip: foreign on opposite sides has no art in a minimal set.
    const strip = grid([
        'ggg',
        'ppp',
        'ggg'
    ]);
    assert.equal(keyAt(strip, 1, 1), 'path.edge.n');
});

test('specific pair art beats wildcard on the other side', () => {
    // Dark grass (priority 15, specific vs path) must out-draw the higher
    // priority path (20, wildcard): the authored pair is the better art.
    const g = grid([
        'ddpp',
        'ddpp'
    ]);
    assert.equal(keyAt(g, 1, 0), 'dark.edge.e');
    assert.equal(keyAt(g, 2, 0), 'path.base');
});

test('one-sided art draws regardless of priority', () => {
    // Tall grass has art against grass; grass has none: tall fringes.
    const tallVsGrass = grid([
        'ttgg'
    ]);
    assert.equal(keyAt(tallVsGrass, 1, 0), 'tall.edge.e');
    assert.equal(keyAt(tallVsGrass, 2, 0), 'grass.base');

    // Tall's set is specific to grass, so against path only the path
    // wildcard applies: path fringes, tall stays base.
    const tallVsPath = grid([
        'ttpp'
    ]);
    assert.equal(keyAt(tallVsPath, 1, 0), 'tall.base');
    assert.equal(keyAt(tallVsPath, 2, 0), 'path.edge.w');
});

test('map edges and unpainted cells are inert', () => {
    const g = grid([
        'p.',
        '..'
    ]);
    assert.equal(keyAt(g, 0, 0), 'path.base');
});

test('unknown painted terrain resolves null but still attracts wildcard fringe', () => {
    const g = grid([
        'xpp'
    ]);
    assert.equal(keyAt(g, 0, 0), null);
    assert.equal(keyAt(g, 1, 0), 'path.edge.w');
});

test('weighted base variants are deterministic and roughly proportional', () => {
    const rows = Array.from({ length: 10 }, () => 'pppppppppp');
    const g = grid(rows);
    const first = resolveTerrainGrid(g.cells, g.width, g.height, INDEX).map(t => t?.key);
    const second = resolveTerrainGrid(g.cells, g.width, g.height, INDEX).map(t => t?.key);
    assert.deepEqual(first, second);

    const interior = [];
    for (let y = 1; y < 9; y++) {
        for (let x = 1; x < 9; x++) {
            interior.push(first[y * 10 + x]);
        }
    }
    const pebbles = interior.filter(key => key === 'path.pebbles').length;
    assert.ok(interior.every(key => key === 'path.base' || key === 'path.pebbles'));
    assert.ok(pebbles > 0, 'low-weight variant never appeared');
    assert.ok(pebbles < interior.length / 3, `low-weight variant appeared ${pebbles}/${interior.length} times`);
});

test('resolveTerrainGrid leaves unpainted cells null', () => {
    const g = grid([
        'pg.',
        '...'
    ]);
    const resolved = resolveTerrainGrid(g.cells, g.width, g.height, INDEX);
    assert.equal(resolved.length, 6);
    assert.ok(resolved[0]);
    assert.ok(resolved[1]);
    assert.equal(resolved[2], null);
    assert.equal(resolved[5], null);
});
