let meeting = null;

const DEFAULT_AUDIO_CONSTRAINTS = {
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true
};

const SDK_POLL_INTERVAL_MS = 50;
const SDK_SCRIPT_PATH = "_content/Valour.Client/js/realtimekit.js";
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

function getRealtimeKitClient() {
    const scope = getGlobalScope();
    const sdk = scope?.RealtimeKitClient;

    if (!sdk) {
        throw new Error(`RealtimeKit SDK was not found. Ensure '${SDK_SCRIPT_PATH}' is loaded before initializing.`);
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
        // Ignore invalid base URI and fallback to the raw path.
    }

    return SDK_SCRIPT_PATH;
}

function isSdkScriptSource(source) {
    if (typeof source !== "string" || source.length === 0) {
        return false;
    }

    return source.includes(SDK_SCRIPT_PATH) || source.endsWith("/realtimekit.js");
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
    if (getGlobalScope()?.RealtimeKitClient || typeof document === "undefined" || sdkScriptLoadPromise) {
        return;
    }

    const existingScript = findSdkScriptTag();
    const script = existingScript ?? document.createElement("script");

    if (!existingScript) {
        script.src = resolveSdkScriptUrl();
        script.async = true;
    }

    sdkScriptLoadPromise = new Promise((resolve, reject) => {
        if (getGlobalScope()?.RealtimeKitClient) {
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
            reject(new Error(`Failed to load RealtimeKit SDK script from '${script.src || resolveSdkScriptUrl()}'.`));
        };

        script.addEventListener("load", onLoad);
        script.addEventListener("error", onError);

        if (!existingScript) {
            const parent = document.head ?? document.body ?? document.documentElement;
            if (!parent) {
                cleanup();
                reject(new Error("Cannot load RealtimeKit SDK because the document does not have a script host element."));
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
        if (scope?.RealtimeKitClient) {
            return scope.RealtimeKitClient;
        }

        if (sdkScriptLoadError) {
            throw sdkScriptLoadError;
        }

        await new Promise((resolve) => setTimeout(resolve, SDK_POLL_INTERVAL_MS));
    }

    const script = findSdkScriptTag();
    throw new Error(
        `Timed out waiting for RealtimeKit SDK after ${timeoutMs}ms. Expected script '${script?.src || resolveSdkScriptUrl()}'.`
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

function canUseMediaDevices() {
    return typeof navigator !== "undefined"
        && navigator.mediaDevices !== undefined
        && navigator.mediaDevices !== null;
}

function canRequestMicrophoneAccess() {
    return canUseMediaDevices()
        && typeof navigator.mediaDevices.getUserMedia === "function";
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

async function requestMicrophonePermissionInternal() {
    if (!canRequestMicrophoneAccess()) {
        return false;
    }

    let stream = null;

    try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        return true;
    } catch {
        return false;
    } finally {
        stopStreamTracks(stream);
    }
}

function getMeetingOrThrow() {
    if (!meeting) {
        throw new Error("RealtimeKit meeting is not initialized. Call init() first.");
    }

    return meeting;
}

async function applyDefaultAudioConstraints(activeMeeting) {
    try {
        const track = activeMeeting.self?.audioTrack;
        if (track && typeof track.applyConstraints === "function") {
            await track.applyConstraints(DEFAULT_AUDIO_CONSTRAINTS);
        }
    } catch {
        // Browser does not support applying these constraints — silently continue.
    }
}

function resolveTargetPath(root, path) {
    const segments = path.split('.');
    let current = root;

    for (let i = 0; i < segments.length - 1; i++) {
        current = current?.[segments[i]];
        if (!current) {
            throw new Error(`Path '${path}' is invalid. Missing segment '${segments[i]}'.`);
        }
    }

    const methodName = segments[segments.length - 1];
    const method = current?.[methodName];

    if (typeof method !== 'function') {
        throw new Error(`Path '${path}' does not resolve to a function.`);
    }

    return { target: current, method };
}

export function isInitialized() {
    return meeting !== null;
}

export async function init(options, sdkLoadTimeoutMs = 20000, initTimeoutMs = 15000) {
    const sdk = await waitForSdk(sdkLoadTimeoutMs);
    meeting = await withTimeout(
        sdk.init(options),
        initTimeoutMs,
        `Timed out initializing RealtimeKit after ${initTimeoutMs}ms.`
    );
}

export async function joinRoom(timeoutMs = 25000) {
    const activeMeeting = getMeetingOrThrow();
    await withTimeout(
        activeMeeting.joinRoom(),
        timeoutMs,
        `Timed out joining voice room after ${timeoutMs}ms.`
    );
}

export async function leaveRoom(endCall = false) {
    if (!meeting) {
        return;
    }

    try {
        await meeting.leaveRoom(endCall);
    } finally {
        meeting = null;
    }
}

export async function enableAudio() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.enableAudio();
    await applyDefaultAudioConstraints(activeMeeting);
}

export async function disableAudio() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.disableAudio();
}

export async function enableVideo() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.enableVideo();
}

export async function disableVideo() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.disableVideo();
}

export async function enableScreenShare() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.enableScreenShare();
}

export async function disableScreenShare() {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.disableScreenShare();
}

export async function setDevice(device) {
    const activeMeeting = getMeetingOrThrow();
    await activeMeeting.self.setDevice(device);

    if (device?.kind === "audioinput") {
        await applyDefaultAudioConstraints(activeMeeting);
    }
}

export async function getAllDevices() {
    const activeMeeting = getMeetingOrThrow();
    return await activeMeeting.self.getAllDevices();
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
            const label = hasLabel ? device.label : `Microphone ${unnamedMicIndex++}`;

            return {
                deviceId: device.deviceId,
                label
            };
        });
}

export async function getMicrophonePermissionState() {
    if (!canRequestMicrophoneAccess()) {
        return "unsupported";
    }

    if (!navigator.permissions || typeof navigator.permissions.query !== "function") {
        return "unknown";
    }

    try {
        const permission = await navigator.permissions.query({ name: "microphone" });
        return normalizePermissionState(permission?.state);
    } catch {
        return "unknown";
    }
}

export async function requestMicrophonePermission() {
    return await requestMicrophonePermissionInternal();
}

export function getSelfState() {
    const activeMeeting = getMeetingOrThrow();
    const self = activeMeeting.self;

    return {
        id: self?.id ?? null,
        name: self?.name ?? null,
        picture: self?.picture ?? null,
        audioEnabled: self?.audioEnabled ?? false,
        videoEnabled: self?.videoEnabled ?? false,
        screenShareEnabled: self?.screenShareEnabled ?? false
    };
}

function getJoinedParticipants(activeMeeting) {
    return activeMeeting?.participants?.joined?.toArray?.() ?? [];
}

function getParticipantById(activeMeeting, participantId) {
    if (!participantId) {
        return null;
    }

    if (activeMeeting?.self?.id === participantId) {
        return activeMeeting.self;
    }

    const joined = getJoinedParticipants(activeMeeting);
    return joined.find((participant) => participant?.id === participantId) ?? null;
}

function parseUserIdString(participant) {
    const customParticipantId = participant?.customParticipantId ?? participant?.clientSpecificId ?? null;
    if (typeof customParticipantId === "string" && customParticipantId.length > 0) {
        const [candidate] = customParticipantId.split(":", 1);
        if (/^[0-9]+$/.test(candidate)) {
            return candidate;
        }
    }

    const rawUserId = participant?.userId;
    if (typeof rawUserId === "string" && /^[0-9]+$/.test(rawUserId)) {
        return rawUserId;
    }

    if (typeof rawUserId === "number" && Number.isFinite(rawUserId)) {
        return String(Math.trunc(rawUserId));
    }

    return null;
}

function mapParticipantSnapshot(participant, isSelf = false) {
    if (!participant?.id) {
        return null;
    }

    return {
        peerId: participant.id,
        userId: parseUserIdString(participant),
        customParticipantId: participant.customParticipantId ?? participant.clientSpecificId ?? null,
        name: participant.name ?? participant.displayName ?? null,
        picture: participant.picture ?? null,
        audioEnabled: participant.audioEnabled ?? false,
        videoEnabled: participant.videoEnabled ?? false,
        screenShareEnabled: participant.screenShareEnabled ?? false,
        hasAudioTrack: !!participant.audioTrack,
        audioTrackId: participant.audioTrack?.id ?? null,
        isSelf
    };
}

function getAudioElement(elementId) {
    if (typeof document === "undefined") {
        return null;
    }

    const element = document.getElementById(elementId);
    return element instanceof HTMLAudioElement ? element : null;
}

function getElementAudioTrack(audioElement) {
    const stream = audioElement?.srcObject;
    if (!(stream instanceof MediaStream)) {
        return null;
    }

    const tracks = stream.getAudioTracks();
    return tracks.length > 0 ? tracks[0] : null;
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

export function getParticipantsSnapshot() {
    const activeMeeting = getMeetingOrThrow();
    const participantMap = new Map();

    const joinedParticipants = getJoinedParticipants(activeMeeting);
    for (const participant of joinedParticipants) {
        const mappedParticipant = mapParticipantSnapshot(participant, false);
        if (mappedParticipant?.peerId) {
            participantMap.set(mappedParticipant.peerId, mappedParticipant);
        }
    }

    const selfParticipant = mapParticipantSnapshot(activeMeeting.self, true);
    if (selfParticipant?.peerId) {
        participantMap.set(selfParticipant.peerId, selfParticipant);
    }

    return {
        activeSpeakerPeerId: activeMeeting?.participants?.lastActiveSpeaker ?? null,
        participants: Array.from(participantMap.values())
    };
}

export function syncParticipantAudio(elementId, participantId, volume = 1.0) {
    const activeMeeting = getMeetingOrThrow();
    const audioElement = getAudioElement(elementId);
    if (!audioElement) {
        return;
    }

    const participant = getParticipantById(activeMeeting, participantId);
    const audioTrack = participant?.audioTrack;
    const isSelfParticipant = participant?.id === activeMeeting?.self?.id;
    const shouldPlayAudio = !isSelfParticipant && !!participant?.audioEnabled && !!audioTrack;

    audioElement.autoplay = true;
    audioElement.playsInline = true;

    if (!shouldPlayAudio) {
        clearAudioElement(audioElement);
        return;
    }

    const existingTrack = getElementAudioTrack(audioElement);
    if (!existingTrack || existingTrack.id !== audioTrack.id) {
        audioElement.srcObject = new MediaStream([audioTrack]);
    }

    audioElement.volume = Math.max(0, Math.min(1, volume));

    const playResult = audioElement.play();
    if (playResult && typeof playResult.catch === "function") {
        playResult.catch(() => {
            // Autoplay restrictions are expected until a user interaction occurs.
        });
    }
}

export async function invoke(path, args) {
    const activeMeeting = getMeetingOrThrow();
    const { target, method } = resolveTargetPath(activeMeeting, path);
    const normalizedArgs = Array.isArray(args) ? args : [];

    return await method.apply(target, normalizedArgs);
}

export function reset() {
    meeting = null;
}

export function sdkLoaded() {
    try {
        return !!getRealtimeKitClient();
    } catch {
        return false;
    }
}
