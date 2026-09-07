import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { decodeVillageAtlas, isProtectedVillageAtlas, resolveVillageImageUrl, resolveVillagePreviewImages } from '../../Client/wwwroot/ts/VillageAtlasProtection.js';

const vector = JSON.parse(await readFile(new URL('../Fixtures/village-atlas-vector.json', import.meta.url)));
const png = Buffer.from(vector.png, 'base64');
const encoded = Buffer.from(vector.protected, 'base64');

test('the build encoder vector decodes to every original PNG byte', () => {
    assert.deepEqual(Buffer.from(decodeVillageAtlas(encoded)), png);
    const offset = Buffer.concat([Buffer.alloc(17), encoded, Buffer.alloc(9)]);
    assert.deepEqual(Buffer.from(decodeVillageAtlas(offset.subarray(17, -9))), png);
});

for (const [name, mutate] of [
    ['unsupported version', bytes => { bytes[7]++; return bytes; }],
    ['truncated header', bytes => bytes.subarray(0, 19)],
    ['truncated body', bytes => bytes.subarray(0, -1)],
    ['oversized payload', bytes => { bytes.writeUInt32LE(0xffffffff, 8); return bytes; }],
    ['damaged checksum', bytes => { bytes[12] ^= 128; return bytes; }],
    ['damaged payload', bytes => { bytes[50] ^= 1; return bytes; }],
]) test(`rejects ${name}`, () => assert.throws(() => decodeVillageAtlas(mutate(Buffer.from(encoded)))));

test('ordinary and unofficial image URLs keep their existing image loading path', async () => {
    for (const url of ['/tiles.png', 'https://example.test/sheet.webp?v=2', 'blob:local-sheet', 'data:image/png;base64,abc']) {
        assert.equal(isProtectedVillageAtlas(url), false);
        assert.equal(await resolveVillageImageUrl(url), url);
    }
    assert.equal(isProtectedVillageAtlas('/official.vtex.bin?v=1#atlas'), true);
});

test('renderer, CSS previews, and editor share one decode and one in-memory URL', async () => {
    const originalFetch = globalThis.fetch;
    let calls = 0, resolved;
    globalThis.fetch = async () => { calls++; return new Response(encoded); };
    try {
        const source = '/shared.vtex.bin?v=vector';
        const [first, second, previews] = await Promise.all([
            resolveVillageImageUrl(source), resolveVillageImageUrl(source), resolveVillagePreviewImages([source, source, '/unofficial.png'])
        ]);
        resolved = first;
        assert.equal(calls, 1);
        assert.equal(first, second);
        assert.equal(previews[source], first);
        assert.equal(previews['/unofficial.png'], '/unofficial.png');
        const response = await originalFetch(first);
        assert.equal(response.headers.get('content-type'), 'image/png');
        assert.deepEqual(Buffer.from(await response.arrayBuffer()), png);
    } finally {
        globalThis.fetch = originalFetch;
        if (resolved) URL.revokeObjectURL(resolved);
    }
});

test('failed downloads are not cached and a later attempt can recover', async () => {
    const originalFetch = globalThis.fetch;
    let resolved;
    try {
        globalThis.fetch = async () => new Response('Unavailable', { status: 503 });
        await assert.rejects(resolveVillageImageUrl('/retry.vtex.bin'), /503/);
        globalThis.fetch = async () => new Response(encoded);
        resolved = await resolveVillageImageUrl('/retry.vtex.bin');
        assert.match(resolved, /^blob:/);
    } finally {
        globalThis.fetch = originalFetch;
        if (resolved) URL.revokeObjectURL(resolved);
    }
});
