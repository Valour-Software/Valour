// Shows the text of end-to-end encrypted messages in push notifications.
//
// The app keeps the content keys of channel key generations it verified in
// IndexedDB (see INotificationKeyStore in the SDK). When a push arrives, the
// service worker decrypts the message envelope in the payload with one of
// those keys. The formats match Valour.Sdk.E2ee: MessageEnvelope,
// MessagePayload, Franking, and NotificationPreviewText.
//
// This is a classic script so the service worker can load it with
// importScripts. The page imports it as a module to write keys. It defines
// globalThis.ValourNotificationPreview. Decrypting also needs
// globalThis.nobleCiphers from lib/noble-chacha.js.
(function (scope) {
    'use strict';

    const DB_NAME = 'valour-notifications';
    const STORE_NAME = 'keys';
    const RECORD_KEY = 'current';
    const KEY_SET_VERSION = 1;

    const KEY_SIZE = 32;
    const HASH_SIZE = 32;
    const NONCE_SIZE = 12;
    const TAG_SIZE = 16;
    const SIGNATURE_SIZE = 64;
    const MESSAGE_NONCE_SIZE = 16;
    const MAX_FIELD_LENGTH = 1024 * 1024;
    const MAX_ATTACHMENTS = 64;
    const MAX_EXTENSIONS = 32;
    const MAX_CONTENT_LENGTH = 2048;
    const MAX_PREVIEW_LENGTH = 300;

    const encoder = new TextEncoder();
    const decoder = new TextDecoder();

    // Key storage

    function openDb() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(DB_NAME, 1);
            request.onupgradeneeded = () => request.result.createObjectStore(STORE_NAME);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function withStore(mode, action) {
        const db = await openDb();
        try {
            return await new Promise((resolve, reject) => {
                const transaction = db.transaction(STORE_NAME, mode);
                const request = action(transaction.objectStore(STORE_NAME));
                transaction.oncomplete = () => resolve(request.result);
                transaction.onerror = () => reject(transaction.error);
                transaction.onabort = () => reject(transaction.error);
            });
        } finally {
            db.close();
        }
    }

    /** Returns the stored key set JSON, or null. */
    async function loadKeys() {
        const value = await withStore('readonly', store => store.get(RECORD_KEY));
        return typeof value === 'string' ? value : null;
    }

    async function saveKeys(keySet) {
        await withStore('readwrite', store => store.put(keySet, RECORD_KEY));
    }

    async function clearKeys() {
        await withStore('readwrite', store => store.delete(RECORD_KEY));
    }

    function parseKeySet(json) {
        if (!json)
            return null;
        try {
            const set = JSON.parse(json);
            return set && set.v === KEY_SET_VERSION && Array.isArray(set.keys) ? set : null;
        } catch {
            return null;
        }
    }

    function findKey(keySet, planetId, channelId, generation) {
        const entry = keySet.keys.find(k => k.p === planetId && k.c === channelId && k.g === generation);
        if (!entry || typeof entry.k !== 'string')
            return null;
        try {
            const bytes = fromBase64(entry.k);
            return bytes.length === KEY_SIZE ? bytes : null;
        } catch {
            return null;
        }
    }

    // Encoding, the same as E2eeReader and E2eeWriter: big-endian integers,
    // and fields prefixed with their int32 length.

    function fromBase64(text) {
        const binary = atob(text);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++)
            bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    class Reader {
        constructor(bytes) {
            this.bytes = bytes;
            this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
            this.offset = 0;
        }

        take(length) {
            if (length < 0 || this.offset + length > this.bytes.length)
                throw new Error('Unexpected end of data.');
            const slice = this.bytes.subarray(this.offset, this.offset + length);
            this.offset += length;
            return slice;
        }

        readMagic(...expected) {
            const magic = String.fromCharCode(...this.take(4));
            if (!expected.includes(magic))
                throw new Error('Unexpected record type.');
            return magic;
        }

        readInt32() {
            const value = this.view.getInt32(this.offset);
            this.take(4);
            return value;
        }

        readInt64() {
            const value = this.view.getBigInt64(this.offset);
            this.take(8);
            return value.toString();
        }

        readBool() {
            const value = this.take(1)[0];
            if (value > 1)
                throw new Error('Invalid boolean value.');
            return value === 1;
        }

        readFixed(length) {
            return this.take(length);
        }

        readBytes() {
            const length = this.readInt32();
            if (length < 0 || length > MAX_FIELD_LENGTH)
                throw new Error('Invalid field length.');
            return this.take(length);
        }

        readString() {
            return decoder.decode(this.readBytes());
        }

        ensureEnd() {
            if (this.offset !== this.bytes.length)
                throw new Error('Unexpected data after record.');
        }
    }

    class Writer {
        constructor() {
            this.parts = [];
        }

        magic(text) {
            this.parts.push(encoder.encode(text));
            return this;
        }

        int32(value) {
            const bytes = new Uint8Array(4);
            new DataView(bytes.buffer).setInt32(0, value);
            this.parts.push(bytes);
            return this;
        }

        int64(value) {
            const bytes = new Uint8Array(8);
            new DataView(bytes.buffer).setBigInt64(0, BigInt(value));
            this.parts.push(bytes);
            return this;
        }

        bool(value) {
            this.parts.push(new Uint8Array([value ? 1 : 0]));
            return this;
        }

        fixed(bytes) {
            this.parts.push(bytes);
            return this;
        }

        string(text) {
            const bytes = encoder.encode(text);
            return this.int32(bytes.length).fixed(bytes);
        }

        toBytes() {
            const length = this.parts.reduce((sum, part) => sum + part.length, 0);
            const bytes = new Uint8Array(length);
            let offset = 0;
            for (const part of this.parts) {
                bytes.set(part, offset);
                offset += part.length;
            }
            return bytes;
        }
    }

    // Records

    function decodeEnvelope(bytes) {
        const reader = new Reader(bytes);
        reader.readMagic('VME1');
        const envelope = {
            header: reader.readBytes(),
            body: reader.readBytes(),
            signature: reader.readFixed(SIGNATURE_SIZE),
        };
        reader.ensureEnd();
        return envelope;
    }

    function decodeHeader(bytes) {
        const reader = new Reader(bytes);
        reader.readMagic('VMH1');
        const header = {
            channelId: reader.readInt64(),
            planetId: reader.readInt64(),
            generation: reader.readInt32(),
            messageNonce: reader.readFixed(MESSAGE_NONCE_SIZE),
            revision: reader.readInt32(),
            authorUserId: reader.readInt64(),
            authorDeviceId: reader.readString(),
            replyToId: reader.readInt64(),
            timestampMs: reader.readInt64(),
            frankingCommitment: reader.readFixed(HASH_SIZE),
            termsHash: reader.readFixed(HASH_SIZE),
        };
        reader.ensureEnd();
        if (header.generation < 1 || header.revision < 0)
            throw new Error('Invalid message header.');
        return header;
    }

    function decodePayload(bytes) {
        const reader = new Reader(bytes);
        // VMP2 is the same format without extensions.
        const magic = reader.readMagic('VMP3', 'VMP2');
        const content = reader.readString();
        const hasEmbed = reader.readBool();
        const embed = reader.readString();
        const frankingKey = reader.readFixed(KEY_SIZE);
        const attachmentCount = reader.readInt32();
        if (attachmentCount < 0 || attachmentCount > MAX_ATTACHMENTS)
            throw new Error('A message has too many attachments.');
        for (let i = 0; i < attachmentCount; i++)
            reader.readFixed(HASH_SIZE);

        let requiresExtension = false;
        if (magic === 'VMP3') {
            const extensionCount = reader.readInt32();
            if (extensionCount < 0 || extensionCount > MAX_EXTENSIONS)
                throw new Error('A message has too many extensions.');
            let previousTag = 0;
            for (let i = 0; i < extensionCount; i++) {
                const tag = reader.readInt32();
                const required = reader.readBool();
                reader.readBytes();
                if (tag <= previousTag)
                    throw new Error('Message extensions must have increasing positive tags.');
                previousTag = tag;
                // This version reads no extensions.
                requiresExtension ||= required;
            }
        }

        reader.ensureEnd();
        return { content, embed: hasEmbed ? embed : null, frankingKey, attachmentCount, requiresExtension };
    }

    // Crypto

    async function hkdf(inputKey, salt, info) {
        const key = await crypto.subtle.importKey('raw', inputKey, 'HKDF', false, ['deriveBits']);
        const bits = await crypto.subtle.deriveBits(
            { name: 'HKDF', hash: 'SHA-256', salt, info: encoder.encode(info) }, key, KEY_SIZE * 8);
        return new Uint8Array(bits);
    }

    async function hmacSha256(key, data) {
        const hmacKey = await crypto.subtle.importKey('raw', key, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
        return new Uint8Array(await crypto.subtle.sign('HMAC', hmacKey, data));
    }

    function equalBytes(a, b) {
        if (a.length !== b.length)
            return false;
        let difference = 0;
        for (let i = 0; i < a.length; i++)
            difference |= a[i] ^ b[i];
        return difference === 0;
    }

    function aeadDecrypt(key, encrypted, associatedData) {
        if (encrypted.length < NONCE_SIZE + TAG_SIZE)
            throw new Error('Encrypted data is too short.');
        const nonce = encrypted.subarray(0, NONCE_SIZE);
        return scope.nobleCiphers.chacha20poly1305(key, nonce, associatedData).decrypt(encrypted.subarray(NONCE_SIZE));
    }

    async function verifyFranking(header, payload) {
        const commitment = await hmacSha256(payload.frankingKey, new Writer()
            .magic('VFC1')
            .int64(header.channelId)
            .fixed(header.messageNonce)
            .int32(header.revision)
            .int64(header.authorUserId)
            .string(payload.content)
            .bool(payload.embed !== null)
            .string(payload.embed ?? '')
            .toBytes());
        return equalBytes(commitment, header.frankingCommitment);
    }

    /**
     * Decrypts a message envelope (base64) with a key from the key set JSON.
     * Returns { content, hasEmbed, attachmentCount }, or null when the set
     * has no key for it or it does not decrypt. Like the SDK's
     * NotificationPreviewDecryptor, it checks the channel key and the
     * franking commitment but not the author's signature.
     */
    async function decryptPreview(keySetJson, planetId, channelId, envelopeBase64) {
        const keySet = parseKeySet(keySetJson);
        if (!keySet || !envelopeBase64 || !channelId)
            return null;

        try {
            const envelope = decodeEnvelope(fromBase64(envelopeBase64));
            const header = decodeHeader(envelope.header);
            if (header.channelId !== String(channelId) || header.planetId !== String(planetId || '0'))
                return null;

            const contentKey = findKey(keySet, header.planetId, header.channelId, header.generation);
            if (!contentKey)
                return null;

            const messageKey = await hkdf(contentKey, header.messageNonce, 'valour-e2ee/message/v1|' + header.revision);
            const payload = decodePayload(aeadDecrypt(messageKey, envelope.body, envelope.header));

            if (payload.requiresExtension || payload.content.length > MAX_CONTENT_LENGTH ||
                !await verifyFranking(header, payload))
                return null;

            return { content: payload.content, hasEmbed: payload.embed !== null, attachmentCount: payload.attachmentCount };
        } catch {
            return null;
        }
    }

    // Text, the same rules as NotificationPreviewText in the SDK

    const TEXT_RULES = [
        [/```[A-Za-z0-9_+-]*/g, ' '],
        // Spoilers can nest, so everything from the first marker to the last is hidden.
        [/\|\|[\s\S]+\|\|/g, '(spoiler)'],
        [/«@[mu]-[0-9]{1,20}»/g, '@user'],
        [/«@r-[0-9]{1,20}»/g, '@role'],
        [/«@c-[0-9]{1,20}»/g, '#channel'],
        [/«e-:([a-z0-9_]{2,32}):~[0-9]{1,20}»/g, ':$1:'],
        [/\[([^\]\n]*)\]\([^)\s]*\)/g, '$1'],
        [/`([^`\n]+)`/g, '$1'],
        [/\*\*([^*\n]+)\*\*/g, '$1'],
        [/__([^_\n]+)__/g, '$1'],
        [/~~([^~\n]+)~~/g, '$1'],
        [/\*([^*\n]+)\*/g, '$1'],
        [/^[ \t]*(#{1,6}[ \t]+|>[ \t]?|-#[ \t]+)/gm, ''],
    ];

    function formatText(content, maxLength = MAX_PREVIEW_LENGTH) {
        if (!content || !content.trim())
            return '';

        let text = content;
        for (const [pattern, replacement] of TEXT_RULES)
            text = text.replace(pattern, replacement);
        text = text.replace(/\s+/g, ' ').trim();

        if (text.length <= maxLength)
            return text;

        let cut = maxLength - 1;
        const previous = text.charCodeAt(cut - 1);
        if (cut > 0 && previous >= 0xd800 && previous <= 0xdbff)
            cut--;
        return text.slice(0, cut).trimEnd() + '…';
    }

    function describe(preview) {
        if (!preview)
            return null;
        const text = formatText(preview.content);
        if (text)
            return text;
        if (preview.attachmentCount === 1)
            return 'Sent an attachment';
        if (preview.attachmentCount > 1)
            return `Sent ${preview.attachmentCount} attachments`;
        return preview.hasEmbed ? 'Sent an embed' : null;
    }

    /**
     * Returns the text to show for a push payload, or null to keep the body
     * the server sent.
     */
    async function textForPayload(payload) {
        if (!payload || !payload.envelope || !payload.channelId || !scope.nobleCiphers)
            return null;
        const keySet = await loadKeys();
        return describe(await decryptPreview(keySet, payload.planetId || '0', payload.channelId, payload.envelope));
    }

    scope.ValourNotificationPreview = {
        loadKeys,
        saveKeys,
        clearKeys,
        decryptPreview,
        formatText,
        describe,
        textForPayload,
    };
})(globalThis);
