import test from 'node:test';
import assert from 'node:assert/strict';

const elements = new Map();
globalThis.MediaStream = class {
    constructor(tracks) { this.tracks = [...tracks]; }
    getTracks() { return this.tracks; }
    getVideoTracks() { return this.tracks.filter(track => track.kind === 'video'); }
    removeTrack(track) { this.tracks = this.tracks.filter(item => item !== track); }
};
globalThis.HTMLVideoElement = class {
    isConnected = true;
    srcObject = null;
    play() { return Promise.resolve(); }
};
globalThis.document = { getElementById: id => elements.get(id) };
let activeRoom;
class VideoTrack {
    mediaStreamTrack = { id: 'remote-screen', kind: 'video', readyState: 'live' };
    elements = new Set();
    attach(element) { this.elements.add(element); element.srcObject = new MediaStream([this.mediaStreamTrack]); }
    detach(element) { this.elements.delete(element); element.srcObject = null; }
}
class Room {
    video = new VideoTrack();
    localParticipant = { sid: 'self' };
    participant = { sid: 'remote', isCameraEnabled: true, getTrackPublication: () => ({ track: this.video }) };
    remoteParticipants = new Map([['remote', this.participant]]);
    disconnected = false;
    constructor() { activeRoom = this; }
    on() {}
    async disconnect() { this.disconnected = true; }
}
globalThis.window = { LivekitClient: { Room, Track: { Source: { Camera: 'camera', ScreenShare: 'screen' } }, RoomEvent: {} } };
const media = await import('../../Client/wwwroot/js/livekit.interop.js');
async function setup() {
    elements.clear();
    await media.init({ baseURI: 'ws://localhost:7880', authToken: 'synthetic-test-token' });
    return activeRoom;
}
function add(id) {
    const element = new HTMLVideoElement();
    elements.set(id, element);
    return element;
}

test('removing an inline view preserves a full-screen subscription to the same video', async () => {
    const room = await setup(), inline = add('inline'), fullscreen = add('fullscreen');
    media.syncParticipantVideo('inline', 'remote');
    media.syncParticipantVideo('fullscreen', 'remote');
    assert.equal(room.video.elements.size, 2);
    inline.isConnected = false;
    media.syncParticipantVideo('fullscreen', 'remote');
    assert.equal(inline.srcObject, null);
    assert.deepEqual([...room.video.elements], [fullscreen]);
    assert.equal(fullscreen.srcObject.getVideoTracks()[0].readyState, 'live');
    await media.leaveRoom();
    assert.equal(fullscreen.srcObject, null);
    assert.equal(room.video.elements.size, 0);
    assert.equal(room.disconnected, true);
});

test('camera visibility and replaced DOM hosts update SDK observation without stopping the remote track', async () => {
    const room = await setup(), old = add('camera');
    media.syncParticipantVideo('camera', 'remote', false);
    room.participant.isCameraEnabled = false;
    media.syncParticipantVideo('camera', 'remote', false);
    assert.equal(room.video.elements.size, 0);
    assert.equal(old.srcObject, null);
    room.participant.isCameraEnabled = true;
    media.syncParticipantVideo('camera', 'remote', false);
    old.isConnected = false;
    const replacement = add('camera');
    media.syncParticipantVideo('camera', 'remote', false);
    assert.deepEqual([...room.video.elements], [replacement]);
    assert.equal(room.video.mediaStreamTrack.readyState, 'live');
    await media.leaveRoom();
});

test('an SDK detach failure cannot leave the call connected', async () => {
    const room = await setup(), element = add('screen');
    media.syncParticipantVideo('screen', 'remote');
    room.video.detach = () => { throw new Error('Detached renderer'); };
    await media.leaveRoom();
    assert.equal(element.srcObject, null);
    assert.equal(room.disconnected, true);
    assert.equal(media.isInitialized(), false);
});

test('participant snapshots expose a disconnected transport after an unexpected room loss', async () => {
    const room = await setup();
    room.state = 'connected';
    assert.equal(media.getParticipantsSnapshot().connectionState, 'connected');
    room.state = 'disconnected';
    assert.equal(media.getParticipantsSnapshot().connectionState, 'disconnected');
    await media.leaveRoom();
});
