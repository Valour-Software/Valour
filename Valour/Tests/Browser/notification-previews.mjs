import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.E2EE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use a local QA server.');
const output = resolve(process.env.E2EE_QA_OUTPUT || 'TestResults/notification-previews');
await mkdir(output, { recursive: true });

// Snowflake IDs exceed Number.MAX_SAFE_INTEGER, so keep them as strings.
const parse = body => body ? JSON.parse(body.replace(/:\s*(\d{16,})/g, ':"$1"')) : null;

async function api(token, method, path, body) {
    const response = await fetch(origin + path, {
        method,
        headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: token } : {}) },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    const raw = await response.text();
    let parsed = raw;
    try { parsed = parse(raw); } catch { }
    return { status: response.status, body: parsed, raw };
}

async function login(email, password) {
    assert.ok(email && password, 'Set credentials for two verified local accounts that have not signed in to the app yet.');
    const result = await api(null, 'POST', '/api/users/token', { Email: email, Password: password });
    assert.equal(result.status, 200, `Login failed for ${email}`);
    const token = result.body.token.id;
    const me = await api(token, 'GET', '/api/users/me');
    await api(token, 'POST', '/api/users/me/preferences/errorReporting/2');
    await api(token, 'POST', '/api/users/me/tutorials/0/complete');
    return { token, id: me.body.id, name: me.body.name };
}

const alice = await login(process.env.E2EE_QA_EMAIL, process.env.E2EE_QA_PASSWORD);
const bob = await login(process.env.E2EE_QA_GUEST_EMAIL, process.env.E2EE_QA_GUEST_PASSWORD);

await api(alice.token, 'POST', `/api/userfriends/add/${encodeURIComponent(bob.name)}`);
await api(bob.token, 'POST', `/api/userfriends/add/${encodeURIComponent(alice.name)}`);
const dm = await api(alice.token, 'GET', `/api/channels/direct/byUser/${bob.id}?create=true`);
assert.equal(dm.status, 200, dm.raw);
const dmId = dm.body.id;

// The headless shell never grants service workers notification permission,
// so this test uses full Chromium in headless mode.
const browser = await chromium.launch({
    headless: true,
    ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : { channel: 'chromium' })
});
const errors = [];

async function open(token, label, serviceWorkers) {
    const context = await browser.newContext({ viewport: { width: 1400, height: 900 }, serviceWorkers });
    await context.grantPermissions(['notifications'], { origin });
    await context.addInitScript(t => {
        if (!localStorage.getItem('token'))
            localStorage.setItem('token', t);
    }, token);
    const page = await context.newPage();
    page.setDefaultTimeout(60000);
    page.on('pageerror', error => errors.push(`${label}: ${error.message}`));
    page.on('console', message => {
        if (message.type() === 'error' && /Unhandled exception|ErrorBoundary|rendering component/i.test(message.text()))
            errors.push(`${label}: ${message.text()}`);
    });
    await page.goto(origin + '/');
    await page.getByText('Your messages are encrypted').waitFor();
    await page.getByRole('button', { name: 'Save later' }).click();
    return { context, page };
}

// The probe is plain text from the message that its rendered preview shows.
async function send(page, text, probe = text) {
    const input = page.locator('.textbox-inner').first();
    await input.waitFor();
    await input.click();
    await page.keyboard.type(text);
    // The chat input reaches .NET asynchronously; the preview message shows when it has.
    await page.waitForFunction(value => [...document.querySelectorAll('body *')]
        .filter(e => [...e.childNodes].some(n => n.nodeType === Node.TEXT_NODE && n.textContent.includes(value)))
        .length >= 2, probe);
    await input.dispatchEvent('keydown', { code: 'Enter', key: 'Enter', bubbles: true });
}

// The newest stored message, once the server has more than the given count.
async function newestMessage(token, after) {
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
        const history = await api(token, 'GET',
            `/api/channels/direct/${dmId}/messages?index=9223372036854775807&count=20`);
        if (Array.isArray(history.body) && history.body.length > after)
            return history.body.reduce((a, b) => BigInt(a.id) > BigInt(b.id) ? a : b);
        await new Promise(r => setTimeout(r, 250));
    }
    throw new Error('The new message was not stored.');
}

async function poll(check, failure, timeoutMs = 30000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const result = await check();
        if (result)
            return result;
        await new Promise(r => setTimeout(r, 250));
    }
    throw new Error(failure);
}

const shot = (page, name) => page.screenshot({ path: `${output}/${name}.png` });
const record = name => console.log(`PASS ${name}`);

try {
    // Both accounts set up encryption first, so Alice's first message, which
    // creates the chat's key, shares the key with Bob.
    const { context, page: recipient } = await open(bob.token, 'bob', 'allow');
    const { page: sender } = await open(alice.token, 'alice', 'block');
    await sender.goto(`${origin}/directchannels/${dmId}/0`);
    await send(sender, 'first message');
    await sender.getByText('first message').first().waitFor();

    // Reading the chat verifies its keys; the app keeps the content key in
    // IndexedDB for the service worker.
    await recipient.goto(`${origin}/directchannels/${dmId}/0`);
    await recipient.getByText('first message').first().waitFor();
    const kept = await poll(async () => {
        const keys = await recipient.evaluate(() => globalThis.ValourNotificationPreview?.loadKeys() ?? null);
        const parsed = keys ? JSON.parse(keys) : null;
        return parsed?.keys.some(k => k.c === dmId) ? parsed : null;
    }, 'The chat key was not kept in IndexedDB.');
    assert.equal(kept.u, bob.id);
    await shot(recipient, '01-recipient-read-chat');
    record('Reading a chat keeps its content key where the service worker can read it');

    // A new message arrives while Bob is elsewhere.
    const text = 'lunch at **noon**? ||it is a surprise||';
    await send(sender, text, 'lunch at');
    await sender.getByText('lunch at').first().waitFor();
    const message = await newestMessage(bob.token, 1);
    assert.ok(message.envelope, 'The stored message has an envelope');

    const registration = await recipient.evaluate(async () => {
        const ready = await navigator.serviceWorker.ready;
        return ready.active?.scriptURL;
    });
    assert.ok(registration?.endsWith('/service-worker.js'), `Service worker is active: ${registration}`);

    // Deliver the push the server would send straight to the service worker.
    const cdp = await context.newCDPSession(recipient);
    const registrations = new Promise(resolve => cdp.on('ServiceWorker.workerRegistrationUpdated',
        event => {
            const match = event.registrations.find(r => !r.isDeleted && r.scopeURL.startsWith(origin));
            if (match)
                resolve(match.registrationId);
        }));
    await cdp.send('ServiceWorker.enable');
    const registrationId = await registrations;

    async function push(payload) {
        await recipient.evaluate(async () => {
            const ready = await navigator.serviceWorker.ready;
            for (const shown of await ready.getNotifications())
                shown.close();
        });
        await cdp.send('ServiceWorker.deliverPushMessage', { origin, registrationId, data: JSON.stringify(payload) });
        return poll(() => recipient.evaluate(async tag => {
            const ready = await navigator.serviceWorker.ready;
            const shown = (await ready.getNotifications()).find(n => n.tag === tag);
            return shown ? { title: shown.title, body: shown.body } : null;
        }, `source-${payload.sourceId}`), 'The service worker showed no notification.');
    }

    const basePayload = {
        title: `${alice.name} DMed you.`,
        message: 'Encrypted message',
        iconUrl: null,
        url: `/directchannels/${dmId}/${message.id}`,
        notificationId: null,
        sourceId: message.id,
        timeSent: Date.now(),
        channelId: dmId,
    };

    const shown = await push({ ...basePayload, envelope: message.envelope });
    assert.equal(shown.title, `${alice.name} DMed you.`);
    assert.equal(shown.body, 'lunch at noon? (spoiler)');
    record('A push with the envelope shows the decrypted text, with the spoiler hidden');

    const withoutEnvelope = await push({ ...basePayload, sourceId: '1' });
    assert.equal(withoutEnvelope.body, 'Encrypted message');
    const otherChannel = await push({ ...basePayload, sourceId: '2', channelId: '5', envelope: message.envelope });
    assert.equal(otherChannel.body, 'Encrypted message');
    record('A push the device cannot decrypt keeps the body the server sent');

    assert.deepEqual(errors, []);
    record('No client errors');
} finally {
    await browser.close();
}
