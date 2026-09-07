import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium, devices } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const origin = process.env.VILLAGE_QA_URL || 'http://localhost:5100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Use a local QA server.');
const manifest = await (await fetch(`${origin}/.well-known/valour-instance`)).json();
assert.equal(manifest.capabilities?.voiceProvider, 'livekit', 'This test requires an isolated local LiveKit server.');
assert.ok(['localhost', '127.0.0.1'].includes(new URL(manifest.capabilities.voiceEndpoint).hostname));
const planet = process.env.VILLAGE_QA_PLANET || 'Village Release QA';
const output = resolve(process.env.VILLAGE_QA_OUTPUT || 'TestResults/village-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
    headless: true,
    args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream', '--autoplay-policy=no-user-gesture-required'],
    ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {})
});
const errors = [], checks = [], participants = [], evidence = {};
const record = name => { checks.push(name); console.log(`PASS ${name}`); };

async function join(email, password, mobile = false) {
    assert.ok(email && password, 'Set both local QA accounts.');
    const context = await browser.newContext({ ...(mobile ? { ...devices['iPhone 13'], deviceScaleFactor: 1 } : { viewport: { width: 1440, height: 1000 } }), permissions: ['microphone', 'camera'] });
    const page = await context.newPage();
    participants.push({ page, context });
    page.setDefaultTimeout(30000);
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
        window.__qaPeerConnections = [];
        window.__qaCaptureTracks = [];
        const PeerConnection = window.RTCPeerConnection;
        window.RTCPeerConnection = class extends PeerConnection {
            constructor(...args) { super(...args); window.__qaPeerConnections.push(this); }
        };
        const getUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
        navigator.mediaDevices.getUserMedia = async constraints => {
            const stream = await getUserMedia(constraints);
            window.__qaCaptureTracks.push(...stream.getTracks());
            return stream;
        };
        // Only screen acquisition is substituted. No desktop or personal window
        // is captured; the generated tracks use the real SDK/SFU/rendering path.
        navigator.mediaDevices.getDisplayMedia = async () => {
            const canvas = document.createElement('canvas');
            canvas.width = 640;
            canvas.height = 360;
            const ctx = canvas.getContext('2d');
            let frame = 0;
            const paint = () => {
                ctx.fillStyle = '#123b39';
                ctx.fillRect(0, 0, 640, 360);
                ctx.fillStyle = '#70d6ab';
                ctx.fillRect((frame++ * 8) % 580, 260, 60, 40);
                ctx.font = 'bold 32px sans-serif';
                ctx.fillText('Villages synthetic screen', 40, 100);
                ctx.font = '20px sans-serif';
                ctx.fillText('Local release QA · no desktop capture', 40, 150);
            };
            paint();
            const timer = setInterval(paint, 100);
            const stream = canvas.captureStream(10);
            window.__qaCaptureTracks.push(...stream.getTracks());
            stream.getVideoTracks()[0].addEventListener('ended', () => clearInterval(timer));
            return stream;
        };
        window.__qaInbound = async kind => {
            const records = [];
            for (const pc of window.__qaPeerConnections) {
                if (pc.connectionState === 'closed') continue;
                for (const stat of (await pc.getStats()).values())
                    if (stat.type === 'inbound-rtp' && stat.kind === kind)
                        records.push({ bytes: stat.bytesReceived || 0, packets: stat.packetsReceived || 0,
                            frames: stat.framesDecoded || 0, width: stat.frameWidth || 0, height: stat.frameHeight || 0 });
            }
            return records;
        };
    });
    await page.goto(origin);
    await page.getByRole('button', { name: 'Log in', exact: true }).waitFor({ timeout: 90000 });
    await page.locator('input[type=email]').fill(email);
    await page.locator('input[type=password]').fill(password);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    if (mobile) { await page.locator('.sidebar-toggle').waitFor({timeout:90000}); await page.locator('.sidebar-toggle').click(); }
    await page.getByText(planet, { exact: false }).first().waitFor({ timeout: 90000 });
    await page.getByText(planet, { exact: false }).first().click();
    if (mobile) { await page.waitForTimeout(300); if(await page.locator('.sidebar-menu').getAttribute('data-mobile-open') !== 'true') await page.locator('.sidebar-toggle').click(); }
    await page.getByText('Village', { exact: true }).first().click();
    await page.locator('.village-canvas').waitFor({ timeout: 60000 });
    await page.getByRole('button', { name: /^Join nearby voice in / }).click();
    await page.locator('.village-call-surface').waitFor();
    console.log(`Joined village; call requested for participant ${participants.length}`);
    return page;
}

async function waitRemote(page, property, value) {
    await page.waitForFunction(({ property, value }) => window.__qaMedia.getParticipantsSnapshot().participants
        .some(participant => !participant.isSelf && participant[property] === value), { property, value });
}
const controls = page => page.locator('.village-call-surface .call-controls').first();

try {
    const owner = await join(process.env.VILLAGE_QA_EMAIL, process.env.VILLAGE_QA_PASSWORD);
    const mobile = process.env.VILLAGE_QA_MEDIA_MOBILE === '1';
    const guest = await join(process.env.VILLAGE_QA_GUEST_EMAIL, process.env.VILLAGE_QA_GUEST_PASSWORD, mobile);
    for (const page of [owner, guest]) {
        await page.locator('.village-call-surface .call-status-badge').getByText('Connected', { exact: true }).waitFor({ state: 'attached', timeout: 60000 });
        await page.evaluate(async () => { window.__qaMedia = await import('/_content/Valour.Client/js/livekit.interop.js'); });
        await page.waitForFunction(() => window.__qaMedia.getSelfState().audioEnabled);
        await waitRemote(page, 'hasAudioTrack', true);
        await page.waitForFunction(async () => (await window.__qaInbound('audio')).some(stat => stat.bytes > 1000 && stat.packets > 10));
    }
    evidence.audio = await Promise.all([owner, guest].map(page => page.evaluate(() => window.__qaInbound('audio'))));
    record('Two accounts exchange real synthetic microphone audio through local LiveKit');

    await controls(owner).getByTitle('Mute', { exact: true }).click();
    await controls(owner).getByTitle('Unmute', { exact: true }).waitFor();
    await waitRemote(guest, 'audioEnabled', false);
    await controls(owner).getByTitle('Unmute', { exact: true }).click();
    await waitRemote(guest, 'audioEnabled', true);
    record('Mute and unmute propagate to the remote call participant');
    if(mobile) {
        await controls(guest).getByTitle('Mute',{exact:true}).tap();
        await waitRemote(owner,'audioEnabled',false);
        await controls(guest).getByTitle('Unmute',{exact:true}).tap();
        await waitRemote(owner,'audioEnabled',true);
        record('Touch mute and unmute update the other participant');
    }

    await guest.waitForFunction(() => [...document.querySelectorAll('.village-call-surface audio')]
        .some(audio => audio.srcObject?.getAudioTracks().length && audio.muted));
    await guest.locator('.village-icon-toggle').filter({ hasText: 'Spatial sound' }).click();
    await guest.waitForFunction(() => [...document.querySelectorAll('.village-call-surface audio')]
        .some(audio => audio.srcObject?.getAudioTracks().length && !audio.muted));
    await guest.locator('.village-icon-toggle').filter({ hasText: 'Spatial sound' }).click();
    await guest.waitForFunction(() => [...document.querySelectorAll('.village-call-surface audio')]
        .some(audio => audio.srcObject?.getAudioTracks().length && audio.muted));
    record('Spatial sound routes remote audio and switches back to ordinary playback');

    await controls(owner).getByTitle('Enable Camera', { exact: true }).click();
    await waitRemote(guest, 'hasVideoTrack', true);
    await guest.waitForFunction(async () => (await window.__qaInbound('video')).some(stat => stat.frames > 10));
    await guest.waitForFunction(() => [...document.querySelectorAll('.village-call-surface video')]
        .some(video => video.videoWidth > 0 && video.readyState >= 2));
    evidence.camera = await guest.evaluate(() => window.__qaInbound('video'));
    await guest.screenshot({ path: `${output}/media-camera.png` });
    await controls(owner).getByTitle('Disable Camera', { exact: true }).click();
    await waitRemote(guest, 'videoEnabled', false);
    record('Synthetic camera frames arrive, render remotely, and stop when disabled');

    await controls(owner).getByTitle('Share Screen', { exact: true }).click();
    await waitRemote(guest, 'hasScreenShareTrack', true);
    await guest.waitForFunction(() => {
        const remote = window.__qaMedia.getParticipantsSnapshot().participants.find(participant => !participant.isSelf && participant.hasScreenShareTrack);
        return [...document.querySelectorAll('.village-call-surface video')].some(video =>
            video.srcObject?.getVideoTracks().some(track => track.id === remote?.screenShareTrackId) &&
            video.videoWidth > 0 && video.readyState >= 2);
    });
    evidence.screen = await guest.evaluate(() => window.__qaInbound('video'));
    if (mobile) {
        evidence.responsive = [];
        let handheld = false;
        for (const [width,height] of [[375,667],[390,844],[568,320],[844,390],[768,1024]]) {
            await guest.setViewportSize({width,height});
            for (const mode of [false,true]) {
                if (mode !== handheld) {
                    await guest.getByRole('button',{name:mode?'Switch to handheld controls':'Switch to floating joystick controls',exact:true}).click();
                    handheld=mode;
                }
                for (const collapsed of [false,true]) {
                    if (collapsed) await guest.getByRole('button',{name:'Minimize meeting',exact:true}).click();
                    await guest.waitForTimeout(200);
                    const label=`media-${width}x${height}-${mode?'handheld':'floating'}-${collapsed?'collapsed':'expanded'}`;
                    const data=await guest.evaluate(()=>{
                        const boxes={};
                        for(const selector of ['.village-window','.village-topbar','.village-call-surface','.village-chat-composer','.village-handheld']) {
                            const element=document.querySelector(selector);
                            if(!element?.checkVisibility())continue;
                            const r=element.getBoundingClientRect(); boxes[selector]={x:r.x,y:r.y,width:r.width,height:r.height};
                        }
                        const problems=[], root=boxes['.village-window'];
                        for(const [selector,r]of Object.entries(boxes)) {
                            if(r.x<root.x-1 || r.x+r.width>root.x+root.width+1 || r.y<root.y-1 || r.y+r.height>root.y+root.height+1) problems.push(`${selector} leaves the pane`);
                        }
                        const entries=Object.entries(boxes).filter(([selector])=>selector!=='.village-window');
                        for(let i=0;i<entries.length;i++)for(let j=i+1;j<entries.length;j++) {
                            const [a,ar]=entries[i], [b,br]=entries[j];
                            if(Math.min(ar.x+ar.width,br.x+br.width)-Math.max(ar.x,br.x)>2 && Math.min(ar.y+ar.height,br.y+br.height)-Math.max(ar.y,br.y)>2) problems.push(`${a} overlaps ${b}`);
                        }
                        return {boxes,problems};
                    });
                    evidence.responsive.push({label,...data});
                    await guest.screenshot({path:`${output}/${label}.png`});
                    if(data.problems.length)console.error(label, data.problems);
                    await guest.getByRole('button',{name:collapsed?'Expand meeting':'Minimize meeting',exact:true}).click({trial:true});
                    if(mode) for(const name of ['Move up','Move down','Move left','Move right','Interact with nearby place','Back or leave building','Focus nearby chat','Open or close Places']) await guest.getByRole('button',{name,exact:true}).click({trial:true});
                    if(collapsed)await guest.getByRole('button',{name:'Expand meeting',exact:true}).click();
                }
            }
        }
        await guest.getByRole('button',{name:'Switch to floating joystick controls',exact:true}).click();
        await guest.setViewportSize({width:390,height:844});
        await guest.getByRole('button',{name:'Browse village places',exact:true}).click();
        await guest.locator('.place-list-item').filter({has:guest.getByText('Town Hall',{exact:true})}).click();
        await guest.getByRole('button',{name:'Enter',exact:true}).click({trial:true});
        await guest.getByRole('button',{name:'Close place details',exact:true}).click();
        await guest.locator('.village-call-surface').waitFor();
        await guest.waitForFunction(() => window.__qaMedia.getParticipantsSnapshot().participants.some(participant=>!participant.isSelf && participant.hasAudioTrack));
        record('Mobile place details remain usable during a call and closing them restores the meeting');
    }

    await guest.locator('.video-tile.screen-share').getByTitle('Focus participant', { exact: true }).click();
    await guest.locator('.video-tile.screen-share.focus-main').waitFor();
    await guest.locator('.video-tile.screen-share.focus-main').getByTitle('Full screen (95%)', { exact: true }).click();
    await guest.locator('.video-fullscreen-backdrop').waitFor();
    await guest.waitForFunction(() => {
        const video = document.querySelector('.video-fullscreen-backdrop video');
        return video?.videoWidth >= 640 && video.videoHeight >= 360 && video.readyState >= 2;
    });
    evidence.fullscreen = await guest.evaluate(() => window.__qaInbound('video'));
    await guest.screenshot({ path: `${output}/media-screen.png` });
    await guest.getByTitle('Exit Fullscreen', { exact: true }).click();
    await guest.locator('.video-fullscreen-backdrop').waitFor({ state: 'detached' });
    await guest.locator('.video-tile.screen-share.focus-main').getByTitle('Exit focused view', { exact: true }).click();
    await controls(owner).getByTitle('Stop Sharing Screen', { exact: true }).click();
    await waitRemote(guest, 'screenShareEnabled', false);
    record('Generated screen share renders in grid, focus and full screen, then stops cleanly');

    await owner.getByRole('button', { name: 'Browse village places' }).click();
    await owner.locator('.place-list-item').filter({ has: owner.getByText('Town Hall', { exact: true }) }).click();
    await owner.getByRole('button', { name: 'Enter', exact: true }).click();
    await owner.getByRole('button', { name: 'Leave building', exact: true }).waitFor();
    await owner.locator('.village-call-surface').waitFor({ state: 'detached' });
    await guest.waitForFunction(() => !window.__qaMedia.isInitialized() ||
        window.__qaMedia.getParticipantsSnapshot().participants.every(participant => participant.isSelf));
    await owner.waitForFunction(() => window.__qaCaptureTracks.every(track => track.readyState === 'ended') &&
        window.__qaPeerConnections.every(pc => pc.connectionState === 'closed'));
    record('Entering a non-call interior leaves the outdoor call and releases capture tracks');

    await guest.locator('.village-call-surface .call-status-badge').getByText('Waiting', { exact: true }).waitFor({ state: 'attached' });
    await guest.locator('.village-call-actions').getByRole('button', { name: 'Leave nearby voice', exact: true }).click();
    await guest.locator('.village-call-surface').waitFor({ state: 'detached' });
    await guest.waitForFunction(() => window.__qaCaptureTracks.every(track => track.readyState === 'ended') &&
        window.__qaPeerConnections.every(pc => pc.connectionState === 'closed'));
    record('The last participant returns to waiting and can leave with all media resources released');
    if(mobile) { assert.deepEqual(evidence.responsive.flatMap(x=>x.problems.map(problem=>`${x.label}: ${problem}`)),[]); record('Expanded and minimized calls fit five phone/tablet sizes with both control modes'); }
    assert.deepEqual(errors, []);
} catch (error) {
    for (const [index, participant] of participants.entries()) {
        await participant.page.screenshot({ path: `${output}/media-failure-${index}.png` }).catch(() => {});
        const messages = await participant.page.locator('.call-error').allTextContents().catch(() => []);
        if (messages.length) console.error(`Participant ${index} call errors: ${messages.join('; ')}`);
    }
    throw error;
} finally {
    await writeFile(`${output}/media-results.json`, JSON.stringify({ checks, errors, evidence }, null, 2));
    await browser.close();
}
