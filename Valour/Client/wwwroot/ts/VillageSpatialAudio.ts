/**
 * Positional voice for villages.
 *
 * Each remote member's microphone stream is routed through its own panner so
 * that someone standing to your left is heard on your left, and someone across
 * the square is quiet. The listener stays at the origin and every source is
 * positioned relative to it, which avoids having to keep the listener's
 * orientation in sync with a top-down camera that never rotates.
 *
 * The plain <audio> element that the call layer already created stays attached
 * but is muted while this graph is the audible route. The caller only mutes it
 * after upsert confirms Web Audio can actually route the stream.
 */

export type SpatialSource = {
    userId: string;
    stream: MediaStream | null;
    source: MediaStreamAudioSourceNode | null;
    panner: PannerNode | null;
    distanceGain: GainNode | null;
    outputGain: GainNode | null;
    volume: number;
    x: number;
    y: number;
};

export type SpatialAudioOptions = {
    /**
     * Tiles at which a voice is still at full volume.
     */
    refDistance?: number;

    /**
     * Tiles beyond which a voice is inaudible.
     */
    maxDistance?: number;
};

export type SpatialAudioRuntime = {
    readonly enabled: boolean;
    setEnabled(enabled: boolean): void;
    setListener(x: number, y: number): void;
    upsert(userId: string, x: number, y: number, stream: MediaStream | null, volume?: number): boolean;
    remove(userId: string): void;
    setOptions(options: SpatialAudioOptions): void;
    resume(): Promise<void>;
    dispose(): void;
};

/**
 * One tile is treated as one metre. The Web Audio distance model is tuned in
 * these units, so this is the only place tile-space meets audio-space.
 */
const UNITS_PER_TILE = 1;

export function calculateDistanceGain(
    distance: number,
    fullVolumeDistance = 2,
    maxDistance = 16
): number {
    if (!Number.isFinite(distance)) {
        return 0;
    }

    const near = Number.isFinite(fullVolumeDistance) ? Math.max(0, fullVolumeDistance) : 2;
    const requestedFar = Number.isFinite(maxDistance) ? maxDistance : 16;
    const far = Math.max(near + 0.001, requestedFar);
    if (distance <= near) {
        return 1;
    }
    if (distance >= far) {
        return 0;
    }

    // Reversed smoothstep: natural through the middle of the range, with no
    // discontinuity or audible cliff at either end.
    const t = (distance - near) / (far - near);
    return 1 - (t * t * (3 - 2 * t));
}

export function createSpatialAudio(options: SpatialAudioOptions = {}): SpatialAudioRuntime {
    const sources = new Map<string, SpatialSource>();

    let context: AudioContext | null = null;
    let enabled = false;
    let listenerX = 0;
    let listenerY = 0;
    let refDistance = options.refDistance ?? 2;
    let maxDistance = options.maxDistance ?? 16;

    function getContext(): AudioContext | null {
        if (context) {
            return context;
        }

        const Ctor = window.AudioContext ?? (window as any).webkitAudioContext;
        if (!Ctor) {
            return null;
        }

        try {
            context = new Ctor();
        } catch {
            // Web Audio may exist but still be unavailable (device/browser
            // policy or exhausted context limit). The caller will keep its
            // ordinary audio element audible when upsert returns false.
            return null;
        }
        return context;
    }

    function applyPosition(entry: SpatialSource) {
        if (!entry.panner) {
            return;
        }

        // Relative to the listener at the origin. The game is top-down, so the
        // world's Y axis becomes the audio Z axis and audio Y stays flat.
        const dx = (entry.x - listenerX) * UNITS_PER_TILE;
        const dz = (entry.y - listenerY) * UNITS_PER_TILE;
        const distanceGain = calculateDistanceGain(Math.hypot(dx, dz), refDistance, maxDistance);

        const panner = entry.panner;
        if (panner.positionX) {
            const now = context?.currentTime ?? 0;
            // Ramped rather than set, or stepping a tile produces an audible click.
            panner.positionX.setTargetAtTime(dx, now, 0.05);
            panner.positionY.setTargetAtTime(0, now, 0.05);
            panner.positionZ.setTargetAtTime(dz, now, 0.05);
        } else if (typeof (panner as any).setPosition === "function") {
            (panner as any).setPosition(dx, 0, dz);
        }

        if (entry.distanceGain && context) {
            entry.distanceGain.gain.setTargetAtTime(distanceGain, context.currentTime, 0.05);
        }
    }

    function buildGraph(entry: SpatialSource): boolean {
        const ctx = getContext();
        if (!ctx || !entry.stream || entry.source) {
            return !!entry.source;
        }

        try {
            entry.source = ctx.createMediaStreamSource(entry.stream);

            entry.panner = ctx.createPanner();
            entry.panner.panningModel = "HRTF";
            // Distance is handled by our smooth, testable gain curve below. The
            // panner is responsible only for HRTF directionality.
            entry.panner.distanceModel = "inverse";
            entry.panner.refDistance = 1;
            entry.panner.maxDistance = 10000;
            entry.panner.rolloffFactor = 0;

            entry.distanceGain = ctx.createGain();
            entry.outputGain = ctx.createGain();
            entry.outputGain.gain.value = enabled ? entry.volume : 0;

            entry.source.connect(entry.panner);
            entry.panner.connect(entry.distanceGain);
            entry.distanceGain.connect(entry.outputGain);
            entry.outputGain.connect(ctx.destination);

            applyPosition(entry);
            return true;
        } catch {
            teardownGraph(entry);
            return false;
        }
    }

    function teardownGraph(entry: SpatialSource) {
        try { entry.source?.disconnect(); } catch { /* already gone */ }
        try { entry.panner?.disconnect(); } catch { /* already gone */ }
        try {
            entry.distanceGain?.disconnect();
            entry.outputGain?.disconnect();
        } catch { /* already gone */ }

        entry.source = null;
        entry.panner = null;
        entry.distanceGain = null;
        entry.outputGain = null;
    }

    return {
        get enabled() {
            return enabled;
        },

        setEnabled(next: boolean) {
            enabled = next;

            for (const entry of sources.values()) {
                if (!entry.outputGain || !context) {
                    continue;
                }

                // Ramped so toggling proximity chat does not pop.
                entry.outputGain.gain.setTargetAtTime(
                    enabled ? entry.volume : 0,
                    context.currentTime,
                    0.05);
            }
        },

        setListener(x: number, y: number) {
            listenerX = x;
            listenerY = y;

            for (const entry of sources.values()) {
                applyPosition(entry);
            }
        },

        upsert(userId: string, x: number, y: number, stream: MediaStream | null, volume = 1) {
            let entry = sources.get(userId);

            if (!entry) {
                entry = {
                    userId,
                    stream,
                    source: null,
                    panner: null,
                    distanceGain: null,
                    outputGain: null,
                    volume: normalizeVolume(volume),
                    x,
                    y
                };
                sources.set(userId, entry);
            } else {
                entry.x = x;
                entry.y = y;
                entry.volume = normalizeVolume(volume);

                // A renegotiated stream needs a fresh graph; MediaStreamAudioSourceNode
                // cannot be repointed at a different stream.
                if (stream && entry.stream !== stream) {
                    teardownGraph(entry);
                    entry.stream = stream;
                }
            }

            if (entry.stream && !entry.source) {
                buildGraph(entry);
            } else {
                applyPosition(entry);
            }

            if (entry.outputGain && context) {
                entry.outputGain.gain.setTargetAtTime(
                    enabled ? entry.volume : 0,
                    context.currentTime,
                    0.05);
            }

            return !!entry.source;
        },

        remove(userId: string) {
            const entry = sources.get(userId);
            if (!entry) {
                return;
            }

            teardownGraph(entry);
            sources.delete(userId);
        },

        setOptions(next: SpatialAudioOptions) {
            refDistance = next.refDistance ?? refDistance;
            maxDistance = next.maxDistance ?? maxDistance;

            for (const entry of sources.values()) {
                applyPosition(entry);
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
                void context.close().catch(() => { /* already closing */ });
                context = null;
            }
        },
    };
}

function normalizeVolume(volume: number): number {
    return Number.isFinite(volume) ? Math.max(0, Math.min(1, volume)) : 1;
}
