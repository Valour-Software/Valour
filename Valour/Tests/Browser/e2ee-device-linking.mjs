import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.E2EE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use a local QA server.');
const output = resolve(process.env.E2EE_QA_OUTPUT || 'TestResults/e2ee-browser');
await mkdir(output, { recursive: true });

const text = 'the meeting moved to friday';

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
    // Answer the error reporting prompt so it does not cover the encryption setup.
    await api(token, 'POST', '/api/users/me/preferences/errorReporting/2');
    // Finish first-run onboarding (tutorial 0). Encryption prompts wait until
    // the onboarding window is done so the two never stack.
    await api(token, 'POST', '/api/users/me/tutorials/0/complete');
    return { token, id: me.body.id, name: me.body.name };
}

const alice = await login(process.env.E2EE_QA_EMAIL, process.env.E2EE_QA_PASSWORD);
const bob = await login(process.env.E2EE_QA_GUEST_EMAIL, process.env.E2EE_QA_GUEST_PASSWORD);
// A second login session stands in for a second device.
const aliceSecond = await login(process.env.E2EE_QA_EMAIL, process.env.E2EE_QA_PASSWORD);

for (const user of [alice, bob]) {
    const keys = await api(user.token, 'GET', `/api/e2ee/users/${user.id}/log`);
    assert.ok(keys.status !== 200 || !Array.isArray(keys.body) || keys.body.length === 0,
        `${user.name} already uses encryption. Use fresh accounts.`);
}

await api(alice.token, 'POST', `/api/userfriends/add/${encodeURIComponent(bob.name)}`);
await api(bob.token, 'POST', `/api/userfriends/add/${encodeURIComponent(alice.name)}`);
const dm = await api(alice.token, 'GET', `/api/channels/direct/byUser/${bob.id}?create=true`);
assert.equal(dm.status, 200, dm.raw);
const dmId = dm.body.id;

const browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const errors = [];

async function open(token, label) {
    const context = await browser.newContext({ viewport: { width: 1400, height: 900 }, serviceWorkers: 'block' });
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
    return page;
}

const shot = (page, name) => page.screenshot({ path: `${output}/${name}.png` });
const record = name => console.log(`PASS ${name}`);

try {
    const laptop = await open(alice.token, 'alice-laptop');
    await laptop.getByText('Your messages are encrypted').waitFor();
    const code = await laptop.locator('.code-groups').innerText();
    assert.match(code.replace(/\s+/g, ''), /^[0-9A-Z]{32}$/);
    await shot(laptop, '01-recovery-code');
    await laptop.getByText('I saved my recovery code somewhere safe').click();
    await laptop.getByRole('button', { name: 'Done' }).click();
    record('The first login sets up encryption and shows the recovery code');

    const guest = await open(bob.token, 'bob');
    await guest.getByText('Your messages are encrypted').waitFor();
    await guest.getByRole('button', { name: 'Save later' }).click();
    await guest.getByText('Recovery code not saved').waitFor();
    record('The recovery code can be saved later');

    await laptop.goto(`${origin}/directchannels/${dmId}/0`);
    const input = laptop.locator('.textbox-inner').first();
    await input.waitFor();
    await input.click();
    await laptop.keyboard.type(text);
    // The chat input reaches .NET asynchronously; the preview message shows when it has.
    await laptop.waitForFunction(value => [...document.querySelectorAll('body *')]
        .filter(e => e.childElementCount === 0 && e.textContent.includes(value)).length >= 2, text);
    await input.dispatchEvent('keydown', { code: 'Enter', key: 'Enter', bubbles: true });
    await laptop.getByText(text).first().waitFor();
    await laptop.locator('.encryption-btn.on').first().waitFor();
    await shot(laptop, '02-direct-message-encrypted');
    record('A direct message is encrypted and the header shows the lock');

    await guest.goto(`${origin}/directchannels/${dmId}/0`);
    await guest.getByText(text).first().waitFor();
    await shot(guest, '03-recipient-reads');
    record('The recipient decrypts the message');

    // The laptop shows a link code; the phone, standing in for a device
    // without a camera, types it.
    const phone = await open(aliceSecond.token, 'alice-phone');
    await phone.getByText('Enter a code from another device').waitFor();

    await laptop.locator('#user-edit-button').first().click();
    await laptop.getByText('Encryption', { exact: true }).first().click();
    await laptop.getByRole('button', { name: 'Link a device' }).click();
    const typed = laptop.locator('.typed-code-value');
    await typed.waitFor();
    const typedCode = (await typed.innerText()).trim();
    assert.match(typedCode, /^[0-9A-Z]{4}(-[0-9A-Z]{4}){3}$/);
    await laptop.locator('.qr-code').waitFor();
    await shot(laptop, '04-existing-device-code');

    await phone.getByText('Enter a code from another device').click();
    await phone.locator('#verify-link-code').fill(typedCode.toLowerCase());
    await phone.getByRole('button', { name: 'Link', exact: true }).click();
    await phone.getByText('Approve this device on your other device to finish.').waitFor();
    await shot(phone, '05-new-device-typed-code');

    await laptop.getByText('Used this code and wants to read your encrypted messages.').waitFor();
    await laptop.getByRole('button', { name: 'Approve', exact: true }).click();
    await laptop.locator('.done').getByText('is linked').waitFor();
    record('An existing device approves a new one that typed its code');

    await phone.getByText('This device can now read and send encrypted messages.').waitFor();
    await phone.getByRole('button', { name: 'Done' }).click();
    await laptop.getByRole('button', { name: 'Done' }).click();
    await phone.goto(`${origin}/directchannels/${dmId}/0`);
    await phone.getByText(text).first().waitFor();
    await shot(phone, '06-new-device-reads-history');
    record('The linked device reads earlier encrypted history');

    const stored = await api(alice.token, 'GET', `/api/channels/direct/${dmId}/messages?index=${Number.MAX_SAFE_INTEGER}&count=10`);
    assert.equal(stored.status, 200, stored.raw);
    assert.ok(!stored.raw.includes(text), 'The server returned readable message text.');
    record('Server responses carry ciphertext only');

    assert.deepEqual(errors, []);
} finally {
    await browser.close();
}
