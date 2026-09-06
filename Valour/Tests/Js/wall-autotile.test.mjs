import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
    WALL_EAST,
    WALL_NORTH,
    WALL_NORTH_EAST,
    makeWallDefinitionKey,
    normalizeWallMask,
    parseWallDefinitionKey,
    resolveWallFrame
} from '../../Client/wwwroot/ts/VillageWallRendering.js';

test('all 256 raw neighbor masks collapse to the 47 authored frames', () => {
    const frames = new Set(Array.from({ length: 256 }, (_, mask) => resolveWallFrame(mask)));
    assert.deepEqual([...frames].sort((a, b) => a - b), Array.from({ length: 47 }, (_, frame) => frame));
});

test('frame numbers match the row-major 8x6 GameMaker layout', () => {
    assert.equal(resolveWallFrame(255), 0);
    assert.equal(resolveWallFrame(0), 46);
    assert.equal(resolveWallFrame(WALL_NORTH), 44);
    assert.equal(resolveWallFrame(WALL_EAST), 43);
});

test('a diagonal is gated by both adjoining cardinal neighbors', () => {
    assert.equal(normalizeWallMask(WALL_NORTH_EAST), 0);
    assert.equal(normalizeWallMask(WALL_NORTH | WALL_NORTH_EAST | WALL_EAST), 7);
});

test('semantic wall keys round-trip and reject unsafe or helper frames', () => {
    const key = makeWallDefinitionKey('modern.green', 12);
    assert.equal(key, 'wall:modern.green:12');
    assert.deepEqual(parseWallDefinitionKey(key), { wallSetKey: 'modern.green', frame: 12 });
    assert.equal(parseWallDefinitionKey('wall:../../private:12'), null);
    assert.equal(parseWallDefinitionKey('wall:modern.green:47'), null);
    assert.equal(makeWallDefinitionKey('modern.green', 54), null);
});
