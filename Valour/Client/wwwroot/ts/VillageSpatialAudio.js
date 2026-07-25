/**
 * Positional voice for villages.
 *
 * Each remote member's microphone stream is routed through its own panner so
 * that someone standing to your left is heard on your left, and someone across
 * the square is quiet. The listener stays at the origin and every source is
 * positioned relative to it, which avoids having to keep the listener's
 * orientation in sync with a top-down camera that never rotates.
 *
 * The plain <audio> element that the call layer already created stays alive but
 * muted: Chrome will stop delivering a remote WebRTC track that is not attached
 * to a media element, so removing it would silence everyone.
 */
/**
 * One tile is treated as one metre. The Web Audio distance model is tuned in
 * these units, so this is the only place tile-space meets audio-space.
 */
const UNITS_PER_TILE = 1;
export function createSpatialAudio(options = {}) {
    const sources = new Map();
    let context = null;
    let enabled = false;
    let listenerX = 0;
    let listenerY = 0;
    let refDistance = options.refDistance ?? 2;
    let maxDistance = options.maxDistance ?? 14;
    let rolloff = options.rolloff ?? 1.4;
    function getContext() {
        if (context) {
            return context;
        }
        const Ctor = window.AudioContext ?? window.webkitAudioContext;
        if (!Ctor) {
            return null;
        }
        context = new Ctor();
        return context;
    }
    function applyPosition(entry) {
        if (!entry.panner) {
            return;
        }
        // Relative to the listener at the origin. The game is top-down, so the
        // world's Y axis becomes the audio Z axis and audio Y stays flat.
        const dx = (entry.x - listenerX) * UNITS_PER_TILE;
        const dz = (entry.y - listenerY) * UNITS_PER_TILE;
        const panner = entry.panner;
        if (panner.positionX) {
            const now = context?.currentTime ?? 0;
            // Ramped rather than set, or stepping a tile produces an audible click.
            panner.positionX.setTargetAtTime(dx, now, 0.05);
            panner.positionY.setTargetAtTime(0, now, 0.05);
            panner.positionZ.setTargetAtTime(dz, now, 0.05);
        }
        else if (typeof panner.setPosition === "function") {
            panner.setPosition(dx, 0, dz);
        }
    }
    function buildGraph(entry) {
        const ctx = getContext();
        if (!ctx || !entry.stream || entry.source) {
            return;
        }
        // Keep a muted element attached so the browser keeps the remote track
        // flowing; the audible path is the Web Audio graph below.
        if (!entry.element) {
            const element = document.createElement("audio");
            element.autoplay = true;
            element.muted = true;
            element.playsInline = true;
            element.srcObject = entry.stream;
            element.style.display = "none";
            document.body.appendChild(element);
            entry.element = element;
            void element.play().catch(() => { });
        }
        entry.source = ctx.createMediaStreamSource(entry.stream);
        entry.panner = ctx.createPanner();
        entry.panner.panningModel = "HRTF";
        entry.panner.distanceModel = "inverse";
        entry.panner.refDistance = refDistance;
        entry.panner.maxDistance = maxDistance;
        entry.panner.rolloffFactor = rolloff;
        entry.gain = ctx.createGain();
        entry.gain.gain.value = enabled ? 1 : 0;
        entry.source.connect(entry.panner);
        entry.panner.connect(entry.gain);
        entry.gain.connect(ctx.destination);
        applyPosition(entry);
    }
    function teardownGraph(entry) {
        try {
            entry.source?.disconnect();
        }
        catch { /* already gone */ }
        try {
            entry.panner?.disconnect();
        }
        catch { /* already gone */ }
        try {
            entry.gain?.disconnect();
        }
        catch { /* already gone */ }
        entry.source = null;
        entry.panner = null;
        entry.gain = null;
        if (entry.element) {
            entry.element.srcObject = null;
            entry.element.remove();
            entry.element = null;
        }
    }
    return {
        get enabled() {
            return enabled;
        },
        setEnabled(next) {
            enabled = next;
            for (const entry of sources.values()) {
                if (!entry.gain || !context) {
                    continue;
                }
                // Ramped so toggling proximity chat does not pop.
                entry.gain.gain.setTargetAtTime(enabled ? 1 : 0, context.currentTime, 0.05);
            }
        },
        setListener(x, y) {
            listenerX = x;
            listenerY = y;
            for (const entry of sources.values()) {
                applyPosition(entry);
            }
        },
        upsert(userId, x, y, stream) {
            let entry = sources.get(userId);
            if (!entry) {
                entry = { userId, stream, element: null, source: null, panner: null, gain: null, x, y };
                sources.set(userId, entry);
            }
            else {
                entry.x = x;
                entry.y = y;
                // A renegotiated stream needs a fresh graph; MediaStreamAudioSourceNode
                // cannot be repointed at a different stream.
                if (stream && entry.stream !== stream) {
                    teardownGraph(entry);
                    entry.stream = stream;
                }
            }
            if (entry.stream && !entry.source) {
                buildGraph(entry);
            }
            else {
                applyPosition(entry);
            }
        },
        remove(userId) {
            const entry = sources.get(userId);
            if (!entry) {
                return;
            }
            teardownGraph(entry);
            sources.delete(userId);
        },
        setOptions(next) {
            refDistance = next.refDistance ?? refDistance;
            maxDistance = next.maxDistance ?? maxDistance;
            rolloff = next.rolloff ?? rolloff;
            for (const entry of sources.values()) {
                if (!entry.panner) {
                    continue;
                }
                entry.panner.refDistance = refDistance;
                entry.panner.maxDistance = maxDistance;
                entry.panner.rolloffFactor = rolloff;
            }
        },
        /**
         * Browsers start an AudioContext suspended until a gesture. The village
         * calls this from the first click or key press.
         */
        async resume() {
            const ctx = getContext();
            if (ctx && ctx.state === "suspended") {
                await ctx.resume();
            }
            for (const entry of sources.values()) {
                if (entry.element?.paused) {
                    void entry.element.play().catch(() => { });
                }
            }
        },
        dispose() {
            for (const entry of sources.values()) {
                teardownGraph(entry);
            }
            sources.clear();
            // The context is closed rather than left suspended so a village that
            // is opened and closed repeatedly does not leak audio contexts, which
            // browsers cap per page.
            if (context) {
                void context.close().catch(() => { });
                context = null;
            }
        },
    };
}
//# sourceMappingURL=VillageSpatialAudio.js.map