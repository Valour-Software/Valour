import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

import {
    TILESET_FORMAT,
    TILESET_FORMAT_VERSION,
    compileTilesetManifest,
    createTilesetPackPlan,
    getReconstructionRegions,
    isCanonicalTilesetManifest
} from '../../Client/wwwroot/ts/VillageTilesetPacking.js';

function sampleManifest() {
    return {
        tileSize: 16,
        image: 'source.png',
        terrains: [{ Key: 'floor', Name: 'Floor', Priority: 0 }],
        definitions: [
            { Key: 'floor.base', X: 2, Y: 3, Width: 1, Height: 1, Collision: ['empty'] },
            { Key: 'floor.alias', X: 2, Y: 3, Width: 1, Height: 1, Collision: ['empty'] },
            { Key: 'desk.large', X: 8, Y: 4, Width: 3, Height: 2, Collision: Array(6).fill('solid') }
        ],
        brushes: [{ Key: 'floor-brush', Size: 1, Cells: [{ TileKey: 'floor.base' }] }],
        wallSets: [{
            Key: 'modern.green',
            Image: '',
            TileSize: 16,
            OriginX: 16,
            OriginY: 112,
            Columns: 8,
            Rows: 7
        }]
    };
}

test('packing is deterministic and deduplicates identical source rectangles', () => {
    const first = createTilesetPackPlan(sampleManifest(), 256, 256);
    const second = createTilesetPackPlan(sampleManifest(), 256, 256);

    assert.deepEqual(first, second);
    assert.equal(first.regions.length, 3);
    assert.deepEqual(
        first.regions.find(region => region.definitionKeys.includes('floor.base')).definitionKeys,
        ['floor.alias', 'floor.base']);
    assert.ok(first.regions.every(region => region.atlasX % 16 === 0 && region.atlasY % 16 === 0));
});

test('the canonical manifest keeps source coordinates while runtime coordinates move', () => {
    const input = sampleManifest();
    input.imageSha256 = 'previous-atlas';
    const plan = createTilesetPackPlan(input, 256, 256);
    const { manifest } = compileTilesetManifest(input, plan, 'official-v1.png', 'source-hash');

    assert.equal(manifest.format, TILESET_FORMAT);
    assert.equal(manifest.version, TILESET_FORMAT_VERSION);
    assert.equal(manifest.image, 'official-v1.png');
    assert.equal(manifest.imageSha256, undefined, 'a repacked manifest cannot retain the old image fingerprint');
    assert.deepEqual(manifest.source, { width: 256, height: 256, sha256: 'source-hash', fileName: '' });
    assert.equal(manifest.atlas.packed, true);

    const floor = manifest.definitions.find(definition => definition.Key === 'floor.base');
    assert.equal(floor.SourceX, 2);
    assert.equal(floor.SourceY, 3);
    assert.notDeepEqual([floor.X, floor.Y], [floor.SourceX, floor.SourceY]);

    const wall = manifest.wallSets[0];
    assert.equal(wall.SourceOriginX, 16);
    assert.equal(wall.SourceOriginY, 112);
    assert.equal(wall.Image, 'official-v1.png');

    const restored = getReconstructionRegions(manifest);
    const floorRegion = restored.find(region => region.definitionKeys.includes('floor.base'));
    assert.deepEqual(
        [floorRegion.sourceX, floorRegion.sourceY],
        [2 * 16, 3 * 16]);
});

test('packing rejects source coordinates outside the uploaded sheet', () => {
    const input = sampleManifest();
    input.definitions[0].X = 99;

    assert.throws(
        () => createTilesetPackPlan(input, 256, 256),
        /Definition floor.base falls outside/);
});

test('packing rejects artwork that crosses between original source sheets', () => {
    const input = sampleManifest();
    input.source = {
        sheets: [
            { width: 256, height: 80, offsetX: 0, offsetY: 0 },
            { width: 256, height: 160, offsetX: 0, offsetY: 96 }
        ]
    };

    assert.throws(
        () => createTilesetPackPlan(input, 256, 256),
        /crosses a source-sheet boundary/);
});

test('the expanded library retains the exterior source and authored brushes', () => {
    const manifest = JSON.parse(fs.readFileSync(
        new URL('../../Client/wwwroot/tilesets/exterior-tileset-0.json', import.meta.url),
        'utf8'));

    assert.equal(isCanonicalTilesetManifest(manifest), true);
    assert.match(manifest.source.sha256, /^[a-f0-9]{64}$/);
    assert.equal(manifest.source.sheets[0].sha256, '1429a07733836963fc6f1bf703bba59e2e766152bea54a9e936a65089c2d0737');
    assert.equal(manifest.source.width, 2816);
    assert.ok(manifest.source.height > 8224);
    assert.ok(manifest.source.sheets.some(sheet => sheet.fileName === "Office objects"));
    assert.deepEqual(
        [manifest.source.sheets[0].width, manifest.source.sheets[0].height, manifest.source.sheets[0].offsetX, manifest.source.sheets[0].offsetY],
        [2816, 8224, 0, 0]);
    assert.ok(manifest.definitions.length >= 257);
    assert.equal(manifest.brushes.length, 3);
    assert.equal(manifest.terrains.length, 11);
    assert.equal(manifest.wallSets.length, 6);
    assert.ok(manifest.definitions.every(definition =>
        Number.isInteger(definition.SourceX) &&
        Number.isInteger(definition.SourceY)));
    assert.ok(manifest.wallSets.every(wallSet =>
        Number.isInteger(wallSet.SourceOriginX) &&
        Number.isInteger(wallSet.SourceOriginY)));
    assert.ok(manifest.definitions.some(definition => definition.Key === 'trees.large-tree'));
    assert.ok(manifest.definitions.some(definition => definition.Key === 'decor.stone-fountain'));
});
