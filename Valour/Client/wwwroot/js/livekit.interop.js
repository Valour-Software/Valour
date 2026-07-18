// LiveKit interop module. Implements the SAME exported surface as
// RealtimeKitComponent.razor.js (init/join/leave, audio/video/screenshare,
// device + permission helpers, getSelfState/getParticipantsSnapshot, and the
// syncParticipant* DOM binders) so the C# wrapper (RealtimeKitComponent) and
// GlobalCallSessionService drive either backend unchanged. Only the init
// options differ: LiveKit reads options.authToken (access token) and
// options.baseURI (the SFU wss URL).
//
// The snapshot/self shapes here are byte-for-byte the same JSON the RealtimeKit
// module emits, so the existing C# models (RealtimeKitSelfState,
// RealtimeKitParticipantsSnapshot/ParticipantState) deserialize with no changes.

let room = null;
let pendingConnect = null;
let lastActiveSpeakerSid = null;

const DEFAULT_AUDIO_CONSTRAINTS = {
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true
};

const SDK_POLL_INTERVAL_MS = 50;
const SDK_SCRIPT_PATH = "_content/Valour.Client/js/livekit-client.umd.js";
let sdkScriptLoadPromise = null;
let sdkScriptLoadError = null;

function getGlobalScope() {
    if (typeof window !== 'undefined') {
        return window;
    }

    if (typeof globalThis !== 'undefined') {
        return globalThis;
    }

    return null;
}

function getLiveKitClient() {
    const scope = getGlobalScope();
    const sdk = scope?.LivekitClient ?? scope?.LiveKitClient;

    if (!sdk) {
        throw new Error(`LiveKit SDK was not found. Ensure '${SDK_SCRIPT_PATH}' is loaded before initializing.`);
    }

    return sdk;
}

function resolveSdkScriptUrl() {
    try {
        if (typeof document !== "undefined" && typeof document.baseURI === "string") {
            return new URL(SDK_SCRIPT_PATH, document.baseURI).toString();
        }

        if (typeof location !== "undefined" && typeof location.href === "string") {
            return new URL(SDK_SCRIPT_PATH, location.href).toString();
        }
    } catch {
        // Ignore invalid base URI and fall back to the raw path.
    }

    return SDK_SCRIPT_PATH;
}

function isSdkScriptSource(source) {
    if (typeof source !== "string" || source.length === 0) {
        return false;
    }

    return source.includes(SDK_SCRIPT_PATH) || source.endsWith("/livekit-client.umd.js");
}

function findSdkScriptTag() {
    if (typeof document === "undefined") {
        return null;
    }

    const scripts = document.querySelectorAll("script[src]");
    for (const script of scripts) {
        if (isSdkScriptSource(script.getAttribute("src")) || isSdkScriptSource(script.src)) {
            return script;
        }
    }

    return null;
}

function startSdkScriptLoad() {
    if (getGlobalScope()?.LivekitClient || typeof document === "undefined" || sdkScriptLoadPromise) {
        return;
    }

    const existingScript = findSdkScriptTag();
    const script = existingScript ?? document.createElement("script");

    if (!existingScript) {
        script.src = resolveSdkScriptUrl();
        script.async = true;
    }

    sdkScriptLoadPromise = new Promise((resolve, reject) => {
        if (getGlobalScope()?.LivekitClient) {
            resolve();
            return;
        }

        const cleanup = () => {
            script.removeEventListener("load", onLoad);
            script.removeEventListener("error", onError);
        };

        const onLoad = () => {
            cleanup();
            resolve();
        };

        const onError = () => {
            cleanup();
            reject(new Error(`Failed to load LiveKit SDK script from '${script.src || resolveSdkScriptUrl()}'.`));
        };

        script.addEventListener("load", onLoad);
        script.addEventListener("error", onError);

        if (!existingScript) {
            const parent = document.head ?? document.body ?? document.documentElement;
            if (!parent) {
                cleanup();
                reject(new Error("Cannot load LiveKit SDK because the document does not have a script host element."));
                return;
            }

            parent.appendChild(script);
        }
    }).catch((error) => {
        sdkScriptLoadError = error;
        throw error;
    });
}

async function waitForSdk(timeoutMs = 20000) {
    const startedAt = Date.now();
    startSdkScriptLoad();

    while (Date.now() - startedAt < timeoutMs) {
        const scope = getGlobalScope();
        if (scope?.LivekitClient) {
            return scope.LivekitClient;
        }

        if (sdkScriptLoadError) {
            throw sdkScriptLoadError;
        }

        await new Promise((resolve) => setTimeout(resolve, SDK_POLL_INTERVAL_MS));
    }

    const script = findSdkScriptTag();
    throw new Error(
        `Timed out waiting for LiveKit SDK after ${timeoutMs}ms. Expected script '${script?.src || resolveSdkScriptUrl()}'.`
    );
}

async function withTimeout(promise, timeoutMs, timeoutMessage) {
    if (timeoutMs <= 0) {
        return await promise;
    }

    let timeoutHandle = null;

    try {
        return await Promise.race([
            promise,
            new Promise((_, reject) => {
                timeoutHandle = setTimeout(
                    () => reject(new Error(timeoutMessage)),
                    timeoutMs
                );
            })
        ]);
    } finally {
        if (timeoutHandle !== null) {
            clearTimeout(timeoutHandle);
        }
    }
}

// ---- Provider-neutral navigator helpers (identical to the RealtimeKit module) ----

function canUseMediaDevices() {
    return typeof navigator !== "undefined"
        && navigator.mediaDevices !== undefined
        && navigator.mediaDevices !== null;
}

function canRequestMicrophoneAccess() {
    return canUseMediaDevices() && typeof navigator.mediaDevices.getUserMedia === "function";
}

function canRequestCameraAccess() {
    return canRequestMicrophoneAccess();
}

function canRequestScreenShareAccess() {
    return canUseMediaDevices() && typeof navigator.mediaDevices.getDisplayMedia === "function";
}

function isAndroidDeviceInternal() {
    if (typeof navigator === "undefined") {
        return false;
    }

    return /android/i.test(navigator.userAgent ?? "");
}

function normalizePermissionState(state) {
    if (state === "granted" || state === "denied" || state === "prompt") {
        return state;
    }

    return "unknown";
}

function stopStreamTracks(stream) {
    if (!stream?.getTracks) {
        return;
    }

    for (const track of stream.getTracks()) {
        track.stop();
    }
}

async function requestMediaPermissionInternal(canRequestAccess, constraints) {
    if (!canRequestAccess) {
        return false;
    }

    let stream = null;
    try {
        stream = await navigator.mediaDevices.getUserMedia(constraints);
        return true;
    } catch (error) {
        try {
            const reason = error?.name ? `${error.name}: ${error.message ?? ""}` : String(error);
            console.warn("Failed to request media permission.", { constraints, reason });
        } catch {
            // Ignore console failures.
        }
        return false;
    } finally {
        stopStreamTracks(stream);
    }
}

async function getPermissionStateAsync(name) {
    if (!navigator.permissions || typeof navigator.permissions.query !== "function") {
        return "unknown";
    }

    try {
        const permission = await navigator.permissions.query({ name });
        return normalizePermissionState(permission?.state);
    } catch {
        return "unknown";
    }
}

// ---- LiveKit track / participant helpers ----

function trackSource(kind) {
    const LK = getLiveKitClient();
    return LK.Track.Source[kind];
}

function getPublication(participant, sourceKind) {
    try {
        return participant?.getTrackPublication?.(trackSource(sourceKind)) ?? null;
    } catch {
        return null;
    }
}

function mediaStreamTrackOf(publication) {
    const track = publication?.track ?? null;
    return track?.mediaStreamTrack ?? null;
}

function getMicMediaStreamTrack(participant) {
    return mediaStreamTrackOf(getPublication(participant, "Microphone"));
}

function getCameraMediaStreamTrack(participant) {
    return mediaStreamTrackOf(getPublication(participant, "Camera"));
}

function getScreenShareMediaStreamTrack(participant) {
    return mediaStreamTrackOf(getPublication(participant, "ScreenShare"));
}

function getScreenShareAudioMediaStreamTrack(participant) {
    return mediaStreamTrackOf(getPublication(participant, "ScreenShareAudio"));
}

function parseUserIdString(participant) {
    const identity = participant?.identity ?? null;
    if (typeof identity === "string" && identity.length > 0) {
        const [candidate] = identity.split(":", 1);
        if (/^[0-9]+$/.test(candidate)) {
            return candidate;
        }
    }

    return null;
}

function getRoomOrThrow() {
    if (!room) {
        throw new Error("LiveKit room is not initialized. Call init() first.");
    }

    return room;
}

function getLocalParticipant() {
    return room?.localParticipant ?? null;
}

function getRemoteParticipants() {
    const remotes = room?.remoteParticipants;
    if (!remotes) {
        return [];
    }

    return typeof remotes.values === "function" ? Array.from(remotes.values()) : [];
}

function getParticipantBySid(participantId) {
    if (!participantId) {
        return null;
    }

    const local = getLocalParticipant();
    if (local?.sid === participantId) {
        return local;
    }

    return getRemoteParticipants().find((participant) => participant?.sid === participantId) ?? null;
}

function mapParticipantSnapshot(participant, isSelf = false) {
    if (!participant?.sid) {
        return null;
    }

    const micTrack = getMicMediaStreamTrack(participant);
    const videoTrack = getCameraMediaStreamTrack(participant);
    const screenShareTrack = getScreenShareMediaStreamTrack(participant);
    const screenShareAudioTrack = getScreenShareAudioMediaStreamTrack(participant);

    return {
        peerId: participant.sid,
        userId: parseUserIdString(participant),
        customParticipantId: participant.identity ?? null,
        name: participant.name || participant.identity || null,
        picture: null,
        audioEnabled: !!participant.isMicrophoneEnabled,
        videoEnabled: !!participant.isCameraEnabled,
        screenShareEnabled: !!participant.isScreenShareEnabled,
        hasAudioTrack: !!micTrack,
        audioTrackId: micTrack?.id ?? null,
        hasVideoTrack: !!videoTrack,
        videoTrackId: videoTrack?.id ?? null,
        hasScreenShareTrack: !!screenShareTrack,
        screenShareTrackId: screenShareTrack?.id ?? null,
        hasScreenShareAudioTrack: !!screenShareAudioTrack,
        screenShareAudioTrackId: screenShareAudioTrack?.id ?? null,
        isSelf
    };
}

async function teardownRoom(activeRoom) {
    if (!activeRoom) {
        return;
    }

    try {
        await withTimeout(activeRoom.disconnect(true), 2000, "Timed out disconnecting from LiveKit during teardown.");
    } catch {
        // Best-effort teardown.
    }
}

// ---- Exported interop surface (matches RealtimeKitComponent.razor.js) ----

export function isInitialized() {
    return room !== null;
}

export async function init(options, sdkLoadTimeoutMs = 20000, initTimeoutMs = 15000) {
    const LK = await waitForSdk(sdkLoadTimeoutMs);

    const serverUrl = options?.baseURI ?? options?.serverUrl ?? null;
    const token = options?.authToken ?? options?.token ?? null;
    if (!serverUrl || !token) {
        throw new Error("LiveKit init requires a server URL (baseURI) and an access token (authToken).");
    }

    room = new LK.Room({
        adaptiveStream: true,
        dynacast: true,
        audioCaptureDefaults: DEFAULT_AUDIO_CONSTRAINTS
    });

    lastActiveSpeakerSid = null;
    room.on(LK.RoomEvent.ActiveSpeakersChanged, (speakers) => {
        lastActiveSpeakerSid = speakers && speakers.length > 0 ? (speakers[0]?.sid ?? null) : null;
    });

    // Warm the connection (TLS + ICE) without joining, mirroring RealtimeKit's
    // init/join split. Best-effort — connect() below is the real gate.
    try {
        if (typeof room.prepareConnection === "function") {
            await withTimeout(room.prepareConnection(serverUrl, token), initTimeoutMs, "Timed out preparing LiveKit connection.");
        }
    } catch {
        // Ignore — prepareConnection is an optimization only.
    }

    pendingConnect = { serverUrl, token };
}

export async function joinRoom(timeoutMs = 25000) {
    const activeRoom = getRoomOrThrow();
    if (!pendingConnect) {
        throw new Error("LiveKit connection details are missing. Call init() first.");
    }

    await withTimeout(
        activeRoom.connect(pendingConnect.serverUrl, pendingConnect.token),
        timeoutMs,
        `Timed out joining voice room after ${timeoutMs}ms.`
    );
}

export async function leaveRoom(endCall = false) {
    if (!room) {
        return;
    }

    const activeRoom = room;
    room = null;
    pendingConnect = null;

    try {
        await teardownRoom(activeRoom);
    } catch {
        // Best-effort.
    }
}

export async function enableAudio() {
    const local = getRoomOrThrow().localParticipant;
    await local.setMicrophoneEnabled(true);
}

export async function disableAudio() {
    const local = getRoomOrThrow().localParticipant;
    await local.setMicrophoneEnabled(false);
}

export async function enableVideo() {
    const local = getRoomOrThrow().localParticipant;
    await local.setCameraEnabled(true);
}

export async function disableVideo() {
    const local = getRoomOrThrow().localParticipant;
    await local.setCameraEnabled(false);
}

export async function enableScreenShare() {
    const local = getRoomOrThrow().localParticipant;
    await local.setScreenShareEnabled(true, { audio: true });
}

export async function disableScreenShare() {
    const local = getRoomOrThrow().localParticipant;
    await local.setScreenShareEnabled(false);
}

export async function setDevice(device) {
    const activeRoom = getRoomOrThrow();
    if (!device?.deviceId || !device?.kind) {
        return;
    }

    // RealtimeKit device kinds ("audioinput"/"videoinput"/"audiooutput") are also
    // valid LiveKit MediaDeviceKind values.
    await activeRoom.switchActiveDevice(device.kind, device.deviceId);
}

export async function getAllDevices() {
    if (!canUseMediaDevices() || typeof navigator.mediaDevices.enumerateDevices !== "function") {
        return [];
    }

    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices.map((device) => ({
        deviceId: device.deviceId,
        kind: device.kind,
        label: device.label,
        groupId: device.groupId
    }));
}

export async function getAudioInputDevices() {
    if (!canUseMediaDevices() || typeof navigator.mediaDevices.enumerateDevices !== "function") {
        return [];
    }

    let unnamedMicIndex = 1;
    const devices = await navigator.mediaDevices.enumerateDevices();

    return devices
        .filter((device) => device.kind === "audioinput")
        .map((device) => {
            const hasLabel = typeof device.label === "string" && device.label.trim().length > 0;
            return {
                deviceId: device.deviceId,
                label: hasLabel ? device.label : `Microphone ${unnamedMicIndex++}`
            };
        });
}

export async function getVideoInputDevices() {
    if (!canUseMediaDevices() || typeof navigator.mediaDevices.enumerateDevices !== "function") {
        return [];
    }

    let unnamedCameraIndex = 1;
    const devices = await navigator.mediaDevices.enumerateDevices();

    return devices
        .filter((device) => device.kind === "videoinput")
        .map((device) => {
            const hasLabel = typeof device.label === "string" && device.label.trim().length > 0;
            return {
                deviceId: device.deviceId,
                label: hasLabel ? device.label : `Camera ${unnamedCameraIndex++}`
            };
        });
}

export async function getMicrophonePermissionState() {
    if (!canRequestMicrophoneAccess()) {
        return "unsupported";
    }

    return await getPermissionStateAsync("microphone");
}

export async function getCameraPermissionState() {
    if (!canRequestCameraAccess()) {
        return "unsupported";
    }

    return await getPermissionStateAsync("camera");
}

export async function requestMicrophonePermission() {
    return await requestMediaPermissionInternal(canRequestMicrophoneAccess(), { audio: true });
}

export async function requestCameraPermission() {
    return await requestMediaPermissionInternal(canRequestCameraAccess(), { video: true });
}

export async function requestPlatformVideoPermission() {
    if (isAndroidDeviceInternal()) {
        return await requestMediaPermissionInternal(canRequestCameraAccess(), { audio: true, video: true });
    }

    return await requestCameraPermission();
}

export function isScreenShareSupported() {
    return canRequestScreenShareAccess();
}

export function isAndroidDevice() {
    return isAndroidDeviceInternal();
}

export function getSelfState() {
    const local = getRoomOrThrow().localParticipant;

    return {
        id: local?.sid ?? null,
        name: local?.name ?? local?.identity ?? null,
        picture: null,
        audioEnabled: !!local?.isMicrophoneEnabled,
        videoEnabled: !!local?.isCameraEnabled,
        screenShareEnabled: !!local?.isScreenShareEnabled
    };
}

export function getParticipantsSnapshot() {
    getRoomOrThrow();
    const participantMap = new Map();

    for (const participant of getRemoteParticipants()) {
        const mapped = mapParticipantSnapshot(participant, false);
        if (mapped?.peerId) {
            participantMap.set(mapped.peerId, mapped);
        }
    }

    const self = mapParticipantSnapshot(getLocalParticipant(), true);
    if (self?.peerId) {
        participantMap.set(self.peerId, self);
    }

    return {
        activeSpeakerPeerId: lastActiveSpeakerSid,
        participants: Array.from(participantMap.values())
    };
}

// ---- DOM binders (identical logic to the RealtimeKit module) ----

function getAudioElement(elementId) {
    if (typeof document === "undefined") {
        return null;
    }

    const element = document.getElementById(elementId);
    return element instanceof HTMLAudioElement ? element : null;
}

function getVideoElement(elementId) {
    if (typeof document === "undefined") {
        return null;
    }

    const element = document.getElementById(elementId);
    return element instanceof HTMLVideoElement ? element : null;
}

function getElementAudioTrackIds(audioElement) {
    const stream = audioElement?.srcObject;
    if (!(stream instanceof MediaStream)) {
        return [];
    }

    return stream.getAudioTracks().map((track) => track.id).sort();
}

function clearAudioElement(audioElement) {
    if (!(audioElement?.srcObject instanceof MediaStream)) {
        audioElement.srcObject = null;
        return;
    }

    const currentStream = audioElement.srcObject;
    for (const track of currentStream.getTracks()) {
        currentStream.removeTrack(track);
    }

    audioElement.srcObject = null;
}

export function syncParticipantAudio(elementId, participantId, volume = 1.0) {
    getRoomOrThrow();
    const audioElement = getAudioElement(elementId);
    if (!audioElement) {
        return;
    }

    const participant = getParticipantBySid(participantId);
    const micAudioTrack = getMicMediaStreamTrack(participant);
    const screenShareAudioTrack = getScreenShareAudioMediaStreamTrack(participant);
    const isSelfParticipant = participant?.sid === getLocalParticipant()?.sid;
    const shouldPlayMicAudio = !!participant?.isMicrophoneEnabled && !!micAudioTrack;
    const shouldPlayScreenShareAudio = !!participant?.isScreenShareEnabled && !!screenShareAudioTrack;
    const shouldPlayAudio = !isSelfParticipant && (shouldPlayMicAudio || shouldPlayScreenShareAudio);

    audioElement.autoplay = true;
    audioElement.playsInline = true;

    if (!shouldPlayAudio) {
        clearAudioElement(audioElement);
        return;
    }

    const desiredTracks = [];
    if (shouldPlayMicAudio) {
        desiredTracks.push(micAudioTrack);
    }

    if (shouldPlayScreenShareAudio) {
        desiredTracks.push(screenShareAudioTrack);
    }

    const desiredTrackIds = desiredTracks.map((track) => track.id).sort();
    const existingTrackIds = getElementAudioTrackIds(audioElement);
    const hasSameAudioTracks = desiredTrackIds.length === existingTrackIds.length
        && desiredTrackIds.every((trackId, index) => trackId === existingTrackIds[index]);

    if (!hasSameAudioTracks) {
        audioElement.srcObject = new MediaStream(desiredTracks);
    }

    audioElement.volume = Math.max(0, Math.min(1, volume));

    const playResult = audioElement.play();
    if (playResult && typeof playResult.catch === "function") {
        playResult.catch(() => {
            // Autoplay restrictions are expected until a user interaction occurs.
        });
    }
}

function getElementVideoTrack(videoElement) {
    const stream = videoElement?.srcObject;
    if (!(stream instanceof MediaStream)) {
        return null;
    }

    const tracks = stream.getVideoTracks();
    return tracks.length > 0 ? tracks[0] : null;
}

function clearVideoElement(videoElement) {
    if (!(videoElement?.srcObject instanceof MediaStream)) {
        videoElement.srcObject = null;
        return;
    }

    const currentStream = videoElement.srcObject;
    for (const track of currentStream.getTracks()) {
        currentStream.removeTrack(track);
    }

    videoElement.srcObject = null;
}

export function syncParticipantVideo(elementId, participantId, preferScreenShare = true) {
    getRoomOrThrow();
    const videoElement = getVideoElement(elementId);
    if (!videoElement) {
        return;
    }

    const participant = getParticipantBySid(participantId);
    const videoTrack = getCameraMediaStreamTrack(participant);
    const screenShareTrack = getScreenShareMediaStreamTrack(participant);
    const selectedTrack = preferScreenShare ? screenShareTrack : videoTrack;
    const shouldRenderTrack = preferScreenShare
        ? !!screenShareTrack
        : !!videoTrack && !!participant?.isCameraEnabled;

    videoElement.autoplay = true;
    videoElement.playsInline = true;
    videoElement.muted = true;

    if (!shouldRenderTrack) {
        clearVideoElement(videoElement);
        return;
    }

    const existingTrack = getElementVideoTrack(videoElement);
    if (!existingTrack || existingTrack.id !== selectedTrack.id) {
        videoElement.srcObject = new MediaStream([selectedTrack]);
    }

    const playResult = videoElement.play();
    if (playResult && typeof playResult.catch === "function") {
        playResult.catch(() => {
            // Autoplay restrictions are expected until a user interaction occurs.
        });
    }
}

export async function invoke(path, args) {
    const activeRoom = getRoomOrThrow();
    const segments = path.split('.');
    let current = activeRoom;

    for (let i = 0; i < segments.length - 1; i++) {
        current = current?.[segments[i]];
        if (!current) {
            throw new Error(`Path '${path}' is invalid. Missing segment '${segments[i]}'.`);
        }
    }

    const method = current?.[segments[segments.length - 1]];
    if (typeof method !== 'function') {
        throw new Error(`Path '${path}' does not resolve to a function.`);
    }

    return await method.apply(current, Array.isArray(args) ? args : []);
}

export function reset() {
    if (!room) {
        return;
    }

    const activeRoom = room;
    room = null;
    pendingConnect = null;
    void teardownRoom(activeRoom);
}

export function sdkLoaded() {
    try {
        return !!getLiveKitClient();
    } catch {
        return false;
    }
}
