import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

// The service worker loads these as classic scripts with importScripts, so
// they run here the same way, in the global scope.
for (const script of ['lib/noble-chacha.js', 'notification-preview.js']) {
    const url = new URL(`../../Client.Blazor/wwwroot/${script}`, import.meta.url);
    vm.runInThisContext(await readFile(url, 'utf8'), { filename: url.pathname });
}

const preview = globalThis.ValourNotificationPreview;
const vector = JSON.parse(await readFile(new URL('../Fixtures/notification-preview-vector.json', import.meta.url)));

test('messages sealed by the SDK decrypt to the same notification text', async () => {
    for (const message of vector.messages) {
        const decrypted = await preview.decryptPreview(vector.keySet, vector.planetId, vector.channelId, message.envelope);
        assert.equal(preview.describe(decrypted), message.text);
    }
});

test('markdown is flattened with the SDK rules', () => {
    for (const { input, expected } of vector.formatCases)
        assert.equal(preview.formatText(input), expected, JSON.stringify(input));
});

test('the payload must name the channel and planet the message was sealed for', async () => {
    const { envelope } = vector.messages[0];
    const otherChannel = (BigInt(vector.channelId) + 1n).toString();

    assert.equal(await preview.decryptPreview(vector.keySet, vector.planetId, otherChannel, envelope), null);
    assert.equal(await preview.decryptPreview(vector.keySet, '0', vector.channelId, envelope), null);
});

test('missing keys and tampered envelopes give no text', async () => {
    const { envelope } = vector.messages[0];
    const emptySet = JSON.stringify({ v: 1, u: '1', keys: [] });
    assert.equal(await preview.decryptPreview(emptySet, vector.planetId, vector.channelId, envelope), null);
    assert.equal(await preview.decryptPreview(null, vector.planetId, vector.channelId, envelope), null);

    // Flip a byte inside the encrypted body. The signature at the end is not checked.
    const bytes = Buffer.from(envelope, 'base64');
    bytes[bytes.length - 80] ^= 1;
    assert.equal(await preview.decryptPreview(vector.keySet, vector.planetId, vector.channelId,
        bytes.toString('base64')), null);

    assert.equal(await preview.decryptPreview(vector.keySet, vector.planetId, vector.channelId, 'AAAA'), null);
    assert.equal(await preview.decryptPreview(vector.keySet, vector.planetId, vector.channelId, 'not base64!'), null);
});

test('a payload without an envelope keeps the server body', async () => {
    assert.equal(await preview.textForPayload({ channelId: vector.channelId, message: 'Encrypted message' }), null);
});
