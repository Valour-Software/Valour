import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const path = new URL('../../Client/Components/Windows/Villages/TilesetDefinitionWindowComponent.razor.js', import.meta.url);
const source = (await readFile(path, 'utf8')).replaceAll('"../../../ts/', `"${new URL('../../Client/wwwroot/ts/', import.meta.url).href}`);
const { inspectSpriteSelection } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function sample(pixels) {
    const image = { width: 8, height: 8, data: new Uint8ClampedArray(8 * 8 * 4) };
    for (const [x, y] of pixels) image.data[(y * 8 + x) * 4 + 3] = 255;
    return image;
}

test('a furniture selection that drops its top trim cannot be saved as a whole sprite', () => {
    assert.match(inspectSpriteSelection(sample([[3, 0], [3, 1], [3, 2]])), /top edge/);
});

test('a cut through the desk edge is detected across a diagonal pixel', () => {
    assert.match(inspectSpriteSelection(sample([[6, 3], [7, 4]])), /right edge/);
});

test('complete objects with disconnected details and transparent margins are accepted', () => {
    assert.equal(inspectSpriteSelection(sample([[1, 3], [2, 3], [4, 5], [6, 6]])), '');
});

test('empty furniture selections are rejected', () => {
    assert.match(inspectSpriteSelection(sample([])), /empty/);
});
