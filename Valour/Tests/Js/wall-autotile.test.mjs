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

test('room outlines leave the interior open and work in every drag direction', async () => {
    const { buildRectangleCells, getWallNeighborMask, wallMaskForFrame } = await import('../../Client/wwwroot/ts/VillageWallRendering.js');
    const cells = buildRectangleCells({x: 6, y: 7}, {x: 2, y: 3}, true);
    assert.equal(cells.length, 16);
    assert.ok(!cells.some(cell => cell.x === 4 && cell.y === 5));
    assert.equal(buildRectangleCells({x: 2, y: 3}, {x: 6, y: 7}, false).length, 25);
    assert.equal(buildRectangleCells({x: 1, y: 1}, {x: 1, y: 5}, true).length, 5);
    const positions = new Set(cells.map(cell => `${cell.x},${cell.y}`));
    const mask = getWallNeighborMask((x, y) => positions.has(`${x},${y}`), 2, 3);
    assert.equal(mask, 20);
    assert.equal(wallMaskForFrame(resolveWallFrame(mask)), mask);
});


test('Room Builder faces use native pixel proportions and only authored face regions', async () => {
    const {renderRoomWall, wallMaskForFrame} = await import('../../Client/wwwroot/ts/VillageWallRendering.js');
    for (let frame=0; frame<47; frame++) {
        const draws=[];
        const ctx={drawImage(...args){draws.push(args)},fillRect(){}};
        renderRoomWall(ctx, {}, 128, 112, wallMaskForFrame(frame), '#fafafa');
        for (const [,x,y,w,h,dx,dy,dw,dh] of draws) {
            assert.ok(x>=128 && y>=112 && x+w<=256 && y+h<=224);
            assert.equal(w,dw); assert.equal(h,dh);
            assert.ok([x,y,w,h,dx,dy,dw,dh].every(Number.isInteger));
        }
    }
});

test('vertical wall caps form a narrow continuous strip and a solid block fills its corners', async () => {
    const {roomWallContains} = await import('../../Client/wwwroot/ts/VillageWallRendering.js');
    for(let y=-1;y<=16;y++) for(let x=0;x<16;x++)
        assert.equal(roomWallContains(17,x,y), x>=5 && x<11);
    for(let y=0;y<16;y++) for(let x=0;x<16;x++) assert.ok(roomWallContains(255,x,y));
});
