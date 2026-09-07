import {
    clamp,
    isTextInput,
    loadTexture as loadCachedTexture,
    normalizeTileDefinitions,
    normalizeTerrainDefinitions,
    buildTerrainIndex,
    resolveTerrainCell,
    createDefinitionMap,
    getCallAudioElementId,
    getBottomAnchoredSpriteBounds,
    getBottomAnchoredCollisionCells,
    getBottomAnchoredStateCells,
    COLLISION_STATE_DOOR,
    getVillageRenderScale,
    adjustVillageZoom,
    getPlayerCenteredCamera
} from "../../../ts/VillageTileRendering.js";
import { createSpatialAudio } from "../../../ts/VillageSpatialAudio.js";
import {
    enqueueVillageBubble,
    getVillageBubbleAlpha,
    VILLAGE_BUBBLE_HOLD_MS,
    VILLAGE_BUBBLE_FADE_MS
} from "../../../ts/VillageChatBubbles.js";
import {
    normalizeMovementKey,
    distanceToBuildingInteraction
} from "../../../ts/VillageHandheldControls.js";
import { parseWallDefinitionKey, getWallNeighborMask, resolveWallFrame, makeWallDefinitionKey, wallMaskForFrame, buildRectangleCells, renderRoomWall, ROOM_WALL_SIZE, ROOM_WALL_RISE } from "../../../ts/VillageWallRendering.js";
import { isCanonicalTilesetManifest } from "../../../ts/VillageTilesetPacking.js";
export { resolveVillagePreviewImages } from "../../../ts/VillageAtlasProtection.js";

// Pixels of drag before a touch counts as steering rather than a tap.
const TOUCH_DEADZONE = 18;

export function init(canvasId, dotNetRef, scene, isMobile) {
    const canvas = document.getElementById(canvasId);
    const ctx = canvas.getContext("2d");
    ctx.imageSmoothingEnabled = false;
    const localAppearance = scene.characters.find((character) => character.isLocalPlayer) ?? {
        name: "",
        avatarUrl: "",
        accentColor: "#4780d9"
    };

    const state = {
        canvas,
        ctx,
        dotNetRef,
        scene,
        localAppearance,
        currentMapId: scene.startingMapId,
        transitioning: false,
        moveReportPromise: Promise.resolve(),
        selectedBuildingId: null,
        selectedPlotId: null,
        keys: new Set(),
        lastDirectionKey: null,
        repeatDelayMs: 160,
        moveAccumulatorMs: 0,
        stepDurationMs: 130,
        animationFrame: 0,
        destroyed: false,
        lastTimestamp: 0,
        zoom: 0.65,
        zoomByMap: new Map(),
        // The wheel steers this and the frame loop eases zoom toward it, so
        // scrolling glides instead of snapping between fixed steps.
        targetZoom: 0.65,
        lastReportedZoomPercent: 65,
        lastZoomReportAt: 0,
        currentScale: 2,
        cameraX: 0,
        cameraY: 0,
        renderCameraX: 0,
        renderCameraY: 0,
        viewportWidth: 0,
        viewportHeight: 0,
        devicePixelRatio: 1,
        localPlayerByMap: new Map(),
        collisionByMap: new Map(),
        remotes: new Map(),
        bubbles: new Map(),
        // Tileset key -> { definitions: Map<key, def>, imageUrl, tileSize }
        tilesets: new Map(),
        wallSets: new Map(),
        touch: { active: false, pointerId: null, originX: 0, originY: 0, dx: 0, dy: 0, moved: false, direction: null },
        touchControlMode: "floating",
        handheldDirection: null,
        lastHandheldPointerAt: 0,
        // Live non-mouse pointers by id; two at once turns the gesture into a
        // pinch that steers the eased zoom target.
        pointers: new Map(),
        pinch: { active: false, startDistance: 1, startZoom: 1 },
        build: {
            enabled: false,
            tool: "Furnish",
            definition: null,
            brush: null,
            wallSet: null,
            selectionKey: "",
            hoverX: null,
            hoverY: null,
            pointerId: null,
            lastDragTile: null,
            stroke: null,
            optimisticRollback: null,
            nextOptimisticId: 1,
            lastSubmitKey: null,
            lastSubmitAt: 0,
            submitting: false,
        },
        admin: {
            enabled: false,
            drawing: false,
            pointerId: null,
            start: null,
            current: null,
        },
        // Whether to draw the resting joystick affordance. The stick itself
        // works from any touch; the ghost exists so touch players can SEE that
        // it exists. Any real touch also flips this on, which covers touch
        // hardware the user-agent sniff misses.
        showTouchControls: !!isMobile || (navigator.maxTouchPoints ?? 0) > 0,
        spatialAudio: createSpatialAudio(),
        spatialAudioEnabled: true,
        voicePeers: new Map(),
        voiceElements: new Map(),
        textureCache: new Map(),
        // Offscreen composite of the current map's static art (base tiles +
        // ground layer). Rebuilt only when the map, scale, or a late-loading
        // texture changes; blitted once per frame instead of re-issuing
        // thousands of per-tile draws.
        staticLayer: null
    };

    for (const character of scene.characters) {
        if (character.isLocalPlayer) {
            state.localPlayerByMap.set(character.mapId, createPlayerState(character.x, character.y));
        }
    }

    const runtime = {
        // The buttons still step through the familiar 25% levels; the easing
        // in the frame loop makes the step glide rather than snap.
        zoomIn() {
            setTargetZoom(state, adjustVillageZoom(state.targetZoom, 1));
        },
        zoomOut() {
            setTargetZoom(state, adjustVillageZoom(state.targetZoom, -1));
        },
        resetZoom() {
            setTargetZoom(state, 1);
        },
        setTouchControlMode(mode) {
            state.touchControlMode = mode === "handheld" ? "handheld" : "floating";
            state.handheldDirection = null;
            state.touch.active = false;
            state.touch.direction = null;
            state.moveAccumulatorMs = 0;
            state.showTouchControls = state.touchControlMode === "floating" &&
                (!!isMobile || (navigator.maxTouchPoints ?? 0) > 0);
            draw(state);
        },
        setHandheldDirection(direction, pressed) {
            if (state.destroyed || state.admin.enabled || state.touchControlMode !== "handheld") {
                return;
            }

            const normalized = normalizeMovementKey(direction);
            if (!normalized) {
                return;
            }

            unlockAudio(state);
            state.lastHandheldPointerAt = performance.now();
            if (!pressed) {
                if (state.handheldDirection === normalized) {
                    state.handheldDirection = null;
                    state.moveAccumulatorMs = 0;
                }
                return;
            }

            const isNewPress = state.handheldDirection !== normalized;
            state.handheldDirection = normalized;
            state.lastDirectionKey = normalized;
            if (isNewPress) {
                state.moveAccumulatorMs = 0;
                queueMovement(state, normalized);
            }
        },
        nudgeHandheldDirection(direction) {
            if (state.destroyed || state.admin.enabled || state.touchControlMode !== "handheld") {
                return;
            }

            // Touch browsers synthesize click after pointerup. The pointerdown
            // already moved, so only keyboard/screen-reader clicks should nudge.
            if (performance.now() - state.lastHandheldPointerAt < 500) {
                return;
            }

            const normalized = normalizeMovementKey(direction);
            if (normalized) {
                state.lastDirectionKey = normalized;
                queueMovement(state, normalized);
            }
        },
        async interact() {
            if (state.destroyed || state.admin.enabled || state.build.enabled) {
                return;
            }

            const map = getCurrentMap(state);
            const player = ensureLocalPlayerPosition(state, state.currentMapId);
            if (!map || !player) {
                return;
            }

            const nearby = (map.buildings ?? [])
                .map(building => ({ building, distance: distanceToBuildingInteraction(building, player) }))
                .filter(item => item.distance <= 1)
                .sort((a, b) => a.distance - b.distance)[0]?.building;

            if (!nearby) {
                return;
            }

            state.selectedBuildingId = nearby.id;
            state.selectedPlotId = null;
            await notifySelection(state);

            if (nearby.interiorMapId) {
                await runtime.setMap(nearby.interiorMapId);
                return;
            }

            draw(state);
        },
        setBuildMode(config) {
            state.build.enabled = config?.enabled === true;
            if (!state.build.enabled) state.build.cameraOffset = { x: 0, y: 0 };
            state.build.pan = null;
            state.build.panKey = false;
            state.build.tool = config?.tool ?? "Furnish";
            state.build.definition = config?.definition ?? null;
            state.build.brush = config?.brush ?? null;
            state.build.wallSet = config?.wallSet ?? null;
            state.build.movingObject = null;
            state.build.selectionKey = config?.selectionKey ?? "";
            state.build.pointerId = null;
            state.build.lastDragTile = null;
            state.build.stroke = null;
            state.keys.clear();
            state.handheldDirection = null;
            state.touch.direction = null;
            state.lastDirectionKey = null;
            state.moveAccumulatorMs = 0;
            if (!state.build.enabled) {
                restoreOptimisticTerrain(state);
                state.build.hoverX = null;
                state.build.hoverY = null;
            }
            canvas.classList.toggle("build-mode", state.build.enabled);
            draw(state);
        },
        setAdminMode(enabled) {
            state.admin.enabled = enabled === true;
            state.admin.drawing = false;
            state.admin.pointerId = null;
            state.admin.start = null;
            state.admin.current = null;
            state.keys.clear();
            state.handheldDirection = null;
            state.touch.direction = null;
            state.lastDirectionKey = null;
            state.moveAccumulatorMs = 0;
            canvas.classList.toggle("admin-mode", state.admin.enabled);
            canvas.classList.remove("admin-drawing");
            draw(state);
        },
        setAdminDrawing(enabled) {
            state.admin.drawing = state.admin.enabled && enabled === true;
            state.admin.pointerId = null;
            state.admin.start = null;
            state.admin.current = null;
            canvas.classList.toggle("admin-drawing", state.admin.drawing);
            draw(state);
        },
        selectAdminPlot(plotId) {
            state.selectedBuildingId = null;
            state.selectedPlotId = plotId == null ? null :
                getCurrentMap(state)?.plots?.find(x => String(x.id) === String(plotId))?.id ?? null;
            draw(state);
        },
        setSelection(buildingId, plotId) {
            const map = getCurrentMap(state);
            state.selectedBuildingId = buildingId == null ? null :
                map?.buildings?.find(x => String(x.id) === String(buildingId))?.id ?? null;
            state.selectedPlotId = plotId == null ? null :
                map?.plots?.find(x => String(x.id) === String(plotId))?.id ?? null;
            draw(state);
        },
        applyBuildResult(result) {
            restoreOptimisticTerrain(state);
            const map = getCurrentMap(state);
            if (!map || !result) {
                return;
            }

            const removed = new Set((result.removedObjectIds ?? []).map(String));
            map.groundTiles = (map.groundTiles ?? []).filter(item => !removed.has(String(item.id)));
            map.decorations = (map.decorations ?? []).filter(item => !removed.has(String(item.id)));
            const changed = [...(result.decorations ?? [])];
            if (result.decoration && !changed.some(item => String(item.id) === String(result.decoration.id))) {
                changed.push(result.decoration);
            }
            if (state.build.movingObject && changed.some(item => String(item.id) === String(state.build.movingObject.id))) {
                state.build.movingObject = null;
                state.build.definition = null;
            }
            for (const decoration of changed) {
                map.groundTiles = map.groundTiles.filter(item => String(item.id) !== String(decoration.id));
                map.decorations = map.decorations.filter(item => String(item.id) !== String(decoration.id));
                const target = decoration.zIndex < 0 ? map.groundTiles : map.decorations;
                target.push(decoration);
            }

            state.collisionByMap.delete(map.id);
            state.staticLayer = null;
            draw(state);
        },
        rollbackBuildPreview() {
            restoreOptimisticTerrain(state);
            draw(state);
        },
        async replaceScene(nextScene) {
            if (!nextScene) {
                return;
            }

            restoreOptimisticTerrain(state);
            const previousMapId = state.currentMapId;
            state.scene = nextScene;
            state.localAppearance = nextScene.characters?.find((character) => character.isLocalPlayer)
                ?? state.localAppearance;
            state.collisionByMap.clear();
            state.staticLayer = null;
            state.currentMapId = nextScene.maps?.some((map) => map.id === previousMapId)
                ? previousMapId
                : nextScene.startingMapId;
            primeMapTextures(state);
            await loadTilesetsForScene(state);
            draw(state);
        },
        async setMap(mapId) {
            const current = getCurrentMap(state);
            const exit = current?.portals?.find(portal => portal.targetMapId === mapId);
            await transitionToMap(state, mapId, exit ? {x: exit.targetX, y: exit.targetY} : null);
        },
        /**
         * Replaces the set of remote players. Positions arrive as target tiles;
         * each remote eases toward its target here rather than snapping, so a
         * ~130ms network cadence still reads as smooth walking.
         */
        setPresences(presences) {
            const seen = new Set();

            for (const p of presences ?? []) {
                seen.add(p.userId);
                const existing = state.remotes.get(p.userId);

                if (!existing) {
                    state.remotes.set(p.userId, {
                        userId: p.userId,
                        name: p.name,
                        avatarUrl: p.avatarUrl,
                        buildingId: p.buildingId ?? null,
                        tileX: p.x,
                        tileY: p.y,
                        renderX: p.x,
                        renderY: p.y,
                        facing: p.facing ?? 0
                    });

                    if (p.avatarUrl) {
                        loadTexture(state, p.avatarUrl);
                    }

                    continue;
                }

                // Identity only arrives on join, so never overwrite it with the
                // blanks that a movement-derived record carries.
                if (p.name) existing.name = p.name;
                if (p.avatarUrl && existing.avatarUrl !== p.avatarUrl) {
                    existing.avatarUrl = p.avatarUrl;
                    loadTexture(state, p.avatarUrl);
                }

                existing.tileX = p.x;
                existing.tileY = p.y;
                existing.facing = p.facing ?? existing.facing;
                existing.buildingId = p.buildingId ?? null;
            }

            for (const userId of [...state.remotes.keys()]) {
                if (!seen.has(userId)) {
                    state.remotes.delete(userId);
                }
            }

            draw(state);
        },
        /**
         * Turns proximity voice on or off. The graph stays built either way so
         * toggling is instant and does not renegotiate anything.
         */
        setSpatialAudioEnabled(enabled) {
            state.spatialAudioEnabled = !!enabled;
            state.spatialAudio.setEnabled(state.spatialAudioEnabled);

            // Enabling is completed per peer only after a graph exists. Muting
            // optimistically here would silence browsers without Web Audio or
            // peers whose media stream has not arrived yet.
            if (!state.spatialAudioEnabled) {
                for (const element of state.voiceElements.values()) {
                    element.muted = false;
                }
            }
        },
        async resumeSpatialAudio() {
            await state.spatialAudio.resume();
        },
        /**
         * Hands the runtime the live voice peers. Called by the call layer, not
         * by presence: someone can be standing in the village without being in
         * voice, and vice versa.
         */
        setVoicePeers(peers) {
            const seen = new Set();

            for (const peer of peers ?? []) {
                const userId = String(peer.userId);
                seen.add(userId);
                state.voicePeers.set(userId, {
                    userId: peer.userId,
                    peerId: peer.peerId ?? ""
                });
            }

            for (const userId of [...state.voicePeers.keys()]) {
                if (!seen.has(userId)) {
                    const element = state.voiceElements.get(userId);
                    if (element) {
                        element.muted = false;
                    }

                    state.voicePeers.delete(userId);
                    state.voiceElements.delete(userId);
                    state.spatialAudio.remove(userId);
                }
            }
        },
        /** Shows a short, bounded stack of recent chat above a member. */
        pushBubble(userId, text, messageId) {
            if (!text) {
                return;
            }

            enqueueVillageBubble(state.bubbles, userId, text, performance.now(), messageId);

            draw(state);
        },
        dispose() {
            state.destroyed = true;
            for (const element of state.voiceElements.values()) {
                element.muted = false;
            }
            state.spatialAudio.dispose();
            window.removeEventListener("resize", state.onResize);
            window.removeEventListener("keydown", state.onKeyDown);
            window.removeEventListener("keyup", state.onKeyUp);
            window.removeEventListener("blur", state.onBlur);
            canvas.removeEventListener("click", state.onClick);
            canvas.removeEventListener("pointerdown", state.onPointerDown);
            canvas.removeEventListener("pointermove", state.onPointerMove);
            canvas.removeEventListener("pointerup", state.onPointerUp);
            canvas.removeEventListener("pointercancel", state.onPointerUp);
            canvas.removeEventListener("wheel", state.onWheel);
            state.resizeObserver?.disconnect();
            state.resizeObserver = null;
            if (state.animationFrame) {
                cancelAnimationFrame(state.animationFrame);
                state.animationFrame = 0;
            }
            state.keys.clear();
        }
    };

    state.spatialAudio.setEnabled(state.spatialAudioEnabled);

    state.onResize = () => {
        resizeCanvas(state);
        updateCamera(state);
        draw(state);
    };

    state.onKeyDown = (event) => {
        if (event.code === "Space" && state.build.enabled && acceptsInput(state, event)) {
            event.preventDefault(); state.build.panKey = true; return;
        }
        if (event.key === "Escape" && state.build.enabled && acceptsInput(state, event)) {
            state.build.movingObject = null;
            if (state.build.tool === "Move") state.build.definition = null;
            draw(state);
            return;
        }
        const normalizedKey = normalizeMovementKey(event.key);
        if (!normalizedKey || state.admin.enabled || state.build.enabled || !acceptsInput(state, event)) {
            return;
        }

        event.preventDefault();
        unlockAudio(state);
        const isNewPress = !state.keys.has(normalizedKey);
        state.keys.add(normalizedKey);
        state.lastDirectionKey = normalizedKey;

        if (isNewPress) {
            state.moveAccumulatorMs = 0;
            queueMovement(state, normalizedKey);
        }
    };

    // Deliberately not gated on acceptsInput: a key pressed over the canvas and
    // released after focus moved away must still clear, or the player walks forever.
    state.onKeyUp = (event) => {
        if (event.code === "Space") state.build.panKey = false;
        const normalizedKey = normalizeMovementKey(event.key);
        if (!normalizedKey) {
            return;
        }

        state.keys.delete(normalizedKey);
        if (state.lastDirectionKey === normalizedKey) {
            state.lastDirectionKey = getActiveDirectionKey(state);
        }
        if (state.keys.size === 0) {
            state.moveAccumulatorMs = 0;
        }
    };

    state.onClick = async (event) => {
        const handled = state.build.handledPointerUp;
        if (isHandledPointerClick(event, handled, performance.now())) return;
        unlockAudio(state);
        const map = getCurrentMap(state);
        if (!map) {
            return;
        }

        const rect = canvas.getBoundingClientRect();
        const px = tilePixelSize(state);
        // Must match the rounded camera the frame was drawn with, or the hit-test
        // skews by up to a pixel against what the user actually sees.
        const worldX = event.clientX - rect.left + state.renderCameraX;
        const worldY = event.clientY - rect.top + state.renderCameraY;
        const tileX = Math.floor(worldX / px);
        const tileY = Math.floor(worldY / px);

        if (state.admin.enabled) {
            return;
        }

        if (state.build.enabled) {
            if (!isStrokeBuildTool(state.build.tool)) {
                await submitBuildSelection(state, map, tileX, tileY);
            }
            return;
        }

        const building = map.buildings.find((item) =>
            buildingContainsTile(state, map, item, tileX, tileY));

        state.selectedBuildingId = building ? building.id : null;
        const plot = building ? null : (map.plots ?? []).find((item) =>
            tileX >= item.x &&
            tileX < item.x + item.width &&
            tileY >= item.y &&
            tileY < item.y + item.height);
        state.selectedPlotId = plot ? plot.id : null;
        await notifySelection(state);
        draw(state);
    };

    // Touch movement: the joystick springs up wherever the finger lands rather
    // than living in a fixed corner, so it works in either hand and never
    // covers something the player was trying to look at.
    state.onPointerDown = (event) => {
        if (state.admin.enabled) {
            if (!state.admin.drawing || state.admin.pointerId !== null) return;
            const map = getCurrentMap(state);
            if (!map) return;
            const tile = pointerTile(state, event, map);
            state.admin.pointerId = event.pointerId;
            state.admin.start = tile;
            state.admin.current = tile;
            try { canvas.setPointerCapture?.(event.pointerId); } catch { }
            draw(state);
            return;
        }

        if (state.build.enabled && (state.build.tool === "Pan" || state.build.panKey || event.button === 1)) {
            state.build.pan = { pointerId: event.pointerId, x: event.clientX, y: event.clientY };
            state.build.cameraOffset ??= { x: 0, y: 0 };
            try { canvas.setPointerCapture?.(event.pointerId); } catch { }
            return;
        }
        if (state.build.enabled) {
            if (event.button && event.button !== 0) return;
            if (state.build.submitting || state.build.pointerId !== null) {
                return;
            }
            unlockAudio(state);
            updateBuildHover(state, event);
            state.build.pointerId = event.pointerId;
            state.build.lastDragTile = state.build.hoverX === null || state.build.hoverY === null
                ? null
                : { x: state.build.hoverX, y: state.build.hoverY };
            try {
                canvas.setPointerCapture?.(event.pointerId);
            } catch { }
            if (isStrokeBuildTool(state.build.tool) && state.build.lastDragTile) {
                const map = getCurrentMap(state);
                if (map) {
                    state.build.stroke = {
                        selectionKey: state.build.selectionKey,
                        cells: new Map(),
                        optimisticToken: null,
                        area: event.shiftKey === true,
                        origin: { ...state.build.lastDragTile },
                    };
                    addBuildStrokeCell(state, map, state.build.lastDragTile.x, state.build.lastDragTile.y);
                    if (state.build.tool === "Paint") {
                        state.build.stroke.optimisticToken = applyOptimisticTerrainStroke(state, state.build.stroke);
                    }
                }
            }
            return;
        }

        if (event.pointerType === "mouse") {
            return;
        }

        // In handheld mode the HTML D-pad owns movement. Canvas touches remain
        // ordinary taps, so inspecting a place still works without a ghost
        // joystick appearing beneath the player's finger.
        if (state.touchControlMode === "handheld") {
            unlockAudio(state);
            return;
        }

        unlockAudio(state);
        state.showTouchControls = true;
        state.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });

        try {
            canvas.setPointerCapture?.(event.pointerId);
        } catch {
            // Synthetic pointers cannot be captured; steering still works.
        }

        // A second finger turns the gesture into a pinch: steering stops, and
        // the release must not read as an inspect tap.
        if (state.pointers.size === 2) {
            const [a, b] = [...state.pointers.values()];
            state.pinch.active = true;
            state.pinch.startDistance = Math.max(1, Math.hypot(a.x - b.x, a.y - b.y));
            state.pinch.startZoom = state.targetZoom;
            state.touch.active = false;
            state.touch.direction = null;
            state.touch.moved = true;
            state.moveAccumulatorMs = 0;
            return;
        }

        if (state.pinch.active) {
            return;
        }

        state.touch.active = true;
        state.touch.pointerId = event.pointerId;
        state.touch.originX = event.clientX;
        state.touch.originY = event.clientY;
        state.touch.dx = 0;
        state.touch.dy = 0;
        state.touch.moved = false;
        state.touch.direction = null;

        // A touch that lands on the resting joystick anchors the stick there,
        // so it behaves like a classic fixed pad; anywhere else the stick
        // still springs up under the finger.
        const rect = canvas.getBoundingClientRect();
        const anchor = getTouchStickAnchor(state);
        const anchorX = rect.left + anchor.x;
        const anchorY = rect.top + anchor.y;
        if (Math.hypot(event.clientX - anchorX, event.clientY - anchorY) <= 58) {
            state.touch.originX = anchorX;
            state.touch.originY = anchorY;
        }
    };

    state.onPointerMove = (event) => {
        if (state.build.pan?.pointerId === event.pointerId) {
            event.preventDefault();
            const px = tilePixelSize(state);
            state.build.cameraOffset.x += (state.build.pan.x - event.clientX) / px;
            state.build.cameraOffset.y += (state.build.pan.y - event.clientY) / px;
            state.build.pan.x = event.clientX; state.build.pan.y = event.clientY;
            updateCamera(state); draw(state); return;
        }
        if (state.admin.enabled) {
            if (state.admin.drawing && event.pointerId === state.admin.pointerId) {
                const map = getCurrentMap(state);
                if (map) state.admin.current = pointerTile(state, event, map);
                event.preventDefault();
                draw(state);
            }
            return;
        }

        if (state.build.enabled) {
            updateBuildHover(state, event);
            if (isStrokeBuildTool(state.build.tool) &&
                event.pointerId === state.build.pointerId &&
                state.build.hoverX !== null &&
                state.build.hoverY !== null) {
                const map = getCurrentMap(state);
                if (map && state.build.stroke) {
                    const next = { x: state.build.hoverX, y: state.build.hoverY };
                    if (state.build.stroke.area) {
                        if (state.build.lastDragTile?.x !== next.x ||
                            state.build.lastDragTile?.y !== next.y) {
                            replaceBuildStrokeArea(state, map, state.build.stroke.origin, next);
                            if (state.build.tool === "Paint") {
                                restoreOptimisticTerrain(state);
                                state.build.stroke.optimisticToken = applyOptimisticTerrainStroke(
                                    state,
                                    state.build.stroke);
                            }
                        }
                    } else {
                        addBuildStrokeLine(state, map, state.build.lastDragTile, next);
                        if (state.build.tool === "Paint") {
                            state.build.stroke.optimisticToken = applyOptimisticTerrainStroke(
                                state,
                                state.build.stroke);
                        }
                    }
                    state.build.lastDragTile = next;
                }
            }
            event.preventDefault();
            return;
        }

        const tracked = state.pointers.get(event.pointerId);
        if (tracked) {
            tracked.x = event.clientX;
            tracked.y = event.clientY;
        }

        // Two fingers steer the zoom target by their distance ratio; the frame
        // loop's easing turns that into the same glide the wheel gets.
        if (state.pinch.active) {
            if (!tracked || state.pointers.size < 2) {
                return;
            }

            event.preventDefault();
            const [a, b] = [...state.pointers.values()];
            const distance = Math.max(1, Math.hypot(a.x - b.x, a.y - b.y));
            setTargetZoom(state, state.pinch.startZoom * (distance / state.pinch.startDistance));
            return;
        }

        if (!state.touch.active || event.pointerId !== state.touch.pointerId) {
            return;
        }

        // Only claim the gesture once it is clearly a drag, so a tap still
        // reaches the building hit-test.
        event.preventDefault();

        state.touch.dx = event.clientX - state.touch.originX;
        state.touch.dy = event.clientY - state.touch.originY;

        const distance = Math.hypot(state.touch.dx, state.touch.dy);
        if (distance < TOUCH_DEADZONE) {
            state.touch.direction = null;
            return;
        }

        state.touch.moved = true;
        const next = Math.abs(state.touch.dx) > Math.abs(state.touch.dy)
            ? (state.touch.dx > 0 ? "right" : "left")
            : (state.touch.dy > 0 ? "down" : "up");

        if (next !== state.touch.direction) {
            state.touch.direction = next;
            state.moveAccumulatorMs = 0;
            queueMovement(state, next);
        }
    };

    state.onPointerUp = (event) => {
        if (state.build.pan?.pointerId === event.pointerId) {
            state.build.pan = null;
            state.build.handledPointerUp = { x: event.clientX, y: event.clientY, at: performance.now() };
            try { canvas.releasePointerCapture?.(event.pointerId); } catch { }
            return;
        }
        if (state.admin.enabled) {
            if (event.pointerId === state.admin.pointerId) {
                const start = state.admin.start;
                const end = state.admin.current ?? start;
                state.admin.pointerId = null;
                state.admin.start = null;
                state.admin.current = null;
                state.admin.drawing = false;
                canvas.classList.remove("admin-drawing");
                try { canvas.releasePointerCapture?.(event.pointerId); } catch { }
                if (event.type !== "pointercancel" && start && end) {
                    const x = Math.min(start.x, end.x);
                    const y = Math.min(start.y, end.y);
                    void invokeDotNet(state, "OnAdminParcelDrawn", x, y,
                        Math.abs(end.x - start.x) + 1,
                        Math.abs(end.y - start.y) + 1);
                }
                draw(state);
            }
            return;
        }

        if (state.build.enabled) {
            if (event.pointerId === state.build.pointerId) {
                const stroke = state.build.stroke;
                state.build.pointerId = null;
                state.build.lastDragTile = null;
                state.build.stroke = null;
                try {
                    canvas.releasePointerCapture?.(event.pointerId);
                } catch { }
                if (stroke) {
                    if (event.type === "pointercancel") {
                        restoreOptimisticTerrain(state);
                        draw(state);
                    } else {
                        void submitBuildStroke(state, stroke);
                    }
                } else if (event.type !== "pointercancel" && !isStrokeBuildTool(state.build.tool)) {
                    void state.onClick(event);
                    state.build.handledPointerUp = { x: event.clientX, y: event.clientY, at: performance.now() };
                }
                if (event.pointerType === "touch") {
                    state.build.hoverX = null;
                    state.build.hoverY = null;
                    draw(state);
                }
            }
            return;
        }

        state.pointers.delete(event.pointerId);

        try {
            canvas.releasePointerCapture?.(event.pointerId);
        } catch {
            // Never captured; nothing to release.
        }

        if (state.pinch.active) {
            // The pinch ends when a finger lifts. The remaining finger's origin
            // is stale, so it does not resume steering - a fresh touch does.
            if (state.pointers.size < 2) {
                state.pinch.active = false;
            }

            return;
        }

        if (!state.touch.active || event.pointerId !== state.touch.pointerId) {
            return;
        }

        const wasTap = !state.touch.moved;

        state.touch.active = false;
        state.touch.pointerId = null;
        state.touch.direction = null;
        state.moveAccumulatorMs = 0;

        // A tap with no drag behaves like a click: inspect whatever is under it.
        if (wasTap) {
            void state.onClick(event);
        }
    };

    state.onBlur = () => {
        state.build.pan = null;
        state.build.panKey = false;
        restoreOptimisticTerrain(state);
        state.keys.clear();
        state.handheldDirection = null;
        state.lastDirectionKey = null;
        state.moveAccumulatorMs = 0;
        state.touch.active = false;
        state.touch.direction = null;
        state.pointers.clear();
        state.pinch.active = false;
        state.build.pointerId = null;
        state.build.lastDragTile = null;
        state.build.stroke = null;
        state.admin.pointerId = null;
        state.admin.start = null;
        state.admin.current = null;
        state.admin.drawing = false;
        canvas.classList.remove("admin-drawing");
    };

    state.onWheel = (event) => {
        event.preventDefault();
        unlockAudio(state);

        // Continuous rather than stepped: every wheel tick nudges the target
        // multiplicatively (so zooming feels uniform at any level) and the
        // frame loop eases toward it. Line-mode deltas (classic mouse wheels)
        // are scaled up to roughly match pixel-mode trackpads.
        const pixels = event.deltaMode === 1 ? event.deltaY * 33 : event.deltaY;
        setTargetZoom(state, state.targetZoom * Math.exp(-pixels * 0.0012));
    };

    window.addEventListener("resize", state.onResize);
    window.addEventListener("keydown", state.onKeyDown);
    window.addEventListener("keyup", state.onKeyUp);
    window.addEventListener("blur", state.onBlur);
    canvas.addEventListener("click", state.onClick);
    canvas.addEventListener("pointerdown", state.onPointerDown, { passive: true });
    canvas.addEventListener("pointermove", state.onPointerMove, { passive: false });
    canvas.addEventListener("pointerup", state.onPointerUp);
    canvas.addEventListener("pointercancel", state.onPointerUp);
    canvas.addEventListener("wheel", state.onWheel, { passive: false });

    // The window resize event does not fire when the dock lays this canvas out,
    // so the element itself is observed. Without this, a village opened into a
    // pane that has not been sized yet keeps the tiny initial backing store and
    // renders as a smear.
    if (typeof ResizeObserver !== "undefined") {
        state.resizeObserver = new ResizeObserver(() => {
            if (!state.destroyed) {
                state.onResize();
            }
        });
        state.resizeObserver.observe(canvas);
    }

    primeMapTextures(state);
    void loadTilesetsForScene(state);
    ensureLocalPlayerPosition(state, state.currentMapId);
    resizeCanvas(state);
    updateCamera(state);

    function frame(timestamp) {
        if (state.destroyed) {
            return;
        }

        const delta = state.lastTimestamp === 0 ? 16 : Math.min(40, timestamp - state.lastTimestamp);
        state.lastTimestamp = timestamp;
        updateZoom(state, delta);
        updatePlayer(state, delta);
        updateRemotes(state, delta);
        updateSpatialAudio(state);
        updateCamera(state);
        draw(state);
        state.animationFrame = requestAnimationFrame(frame);
    }

    state.animationFrame = requestAnimationFrame(frame);
    draw(state);
    notifySelection(state);
    void invokeDotNet(state, "OnZoomChanged", 65);
    return runtime;
}

function setTargetZoom(state, zoom) {
    if (state.destroyed || !Number.isFinite(zoom)) {
        return;
    }

    state.targetZoom = clamp(zoom, 0.5, 2);
}

/**
 * Eases the live zoom toward the target each frame. Only the scale factor
 * changes here - the canvas backing store stays viewport-sized - so a zoom
 * glide costs no more than a normal frame.
 */
function updateZoom(state, deltaMs) {
    if (state.zoom === state.targetZoom) {
        return;
    }

    const rate = 1 - Math.exp(-deltaMs / 90);
    let next = state.zoom + (state.targetZoom - state.zoom) * rate;
    if (Math.abs(next - state.targetZoom) < 0.002) {
        next = state.targetZoom;
    }

    state.zoom = next;

    const rect = state.canvas.getBoundingClientRect();
    const map = getCurrentMap(state);
    if (rect.width >= 1) {
        state.currentScale = getVillageRenderScale(rect.width, map?.mapKind) * state.zoom;
    }

    // The HUD only shows whole percents, and each report re-renders the Blazor
    // side, so mid-glide updates are throttled; the settled value always lands.
    const percent = Math.round(state.zoom * 100);
    const settled = state.zoom === state.targetZoom;
    const now = performance.now();
    if (percent !== state.lastReportedZoomPercent && (settled || now - state.lastZoomReportAt > 100)) {
        state.lastReportedZoomPercent = percent;
        state.lastZoomReportAt = now;
        void invokeDotNet(state, "OnZoomChanged", percent);
    }
}

function createPlayerState(x, y) {
    const tileX = Math.round(x);
    const tileY = Math.round(y);
    return {
        tileX,
        tileY,
        renderX: tileX,
        renderY: tileY,
        startX: tileX,
        startY: tileY,
        targetX: tileX,
        targetY: tileY,
        moving: false,
        progressMs: 0
    };
}

/**
 * Maps declare their own tile size; the runtime must not assume the 32px the
 * proof-of-concept maps happen to use.
 */
/**
 * Loads the definition file for every tileset the scene references. Sprites are
 * drawn from a shared sheet, so a map only needs the sheet plus a key -> source
 * rectangle table; that table is what these files hold.
 *
 * Failure is non-fatal on purpose: an unfinished or missing tileset leaves the
 * definition map empty and every sprite falls back to its primitive, which is
 * how the world stays legible while art is still being authored.
 */
async function loadTilesetsForScene(state) {
    state.wallSets.clear();
    for (const rawWallSet of state.scene.buildWallSets ?? []) {
        const key = rawWallSet?.key ?? "";
        if (!key) {
            continue;
        }
        const wallSet = {
            ...rawWallSet,
            tileSize: Math.max(1, Number(rawWallSet.tileSize) || 16),
            columns: Math.max(1, Number(rawWallSet.columns) || 8),
            rows: Math.max(1, Number(rawWallSet.rows) || 7),
            originX: Math.max(0, Number(rawWallSet.originX) || 0),
            originY: Math.max(0, Number(rawWallSet.originY) || 0),
        };
        state.wallSets.set(key, wallSet);
        if (wallSet.imageUrl) {
            loadTexture(state, wallSet.imageUrl, () => draw(state));
        }
    }

    const keys = new Set();
    for (const map of state.scene.maps ?? []) {
        if (map.tilesetKey) {
            keys.add(map.tilesetKey);
        }
    }

    for (const key of keys) {
        if (state.tilesets.has(key)) {
            continue;
        }

        // Claim the slot before awaiting so two maps sharing a tileset do not
        // both fetch it.
        state.tilesets.set(key, {
            definitions: new Map(),
            terrainIndex: new Map(),
            imageUrl: null,
            tileSize: 16,
        });

        try {
            const response = await fetch(`/_content/Valour.Client/tilesets/${encodeURIComponent(key)}.json`, { cache: "no-cache" });
            if (!response.ok) {
                continue;
            }

            const parsed = await response.json();
            if (state.destroyed) {
                return;
            }
            if (!isCanonicalTilesetManifest(parsed)) {
                console.error(`Tileset ${key} is not a supported canonical Valour tileset.`);
                continue;
            }

            const normalizedDefinitions = normalizeTileDefinitions(parsed.definitions);
            const definitions = createDefinitionMap(normalizedDefinitions);
            const terrainIndex = buildTerrainIndex(
                normalizeTerrainDefinitions(parsed.terrains),
                normalizedDefinitions);
            state.tilesets.set(key, {
                definitions,
                terrainIndex,
                imageUrl: parsed.image ?? null,
                tileSize: parsed.tileSize > 0 ? parsed.tileSize : 16
            });
            // A map may have been queried for collision while the definition
            // file was still in flight. Discard that rectangular fallback now
            // that the authored per-tile masks are available, along with any
            // static layer composed from fallback art.
            state.collisionByMap.clear();
            state.staticLayer = null;

            if (parsed.image) {
                loadTexture(state, parsed.image, () => draw(state));
            }

            draw(state);
        } catch {
            // Leave the empty slot in place; primitives will stand in.
        }
    }
}

/**
 * Resolves a logical sprite key to a source rectangle on its sheet, or null when
 * the key is unknown or its sheet has not finished loading.
 */
function resolveSprite(state, map, key) {
    const wall = parseWallDefinitionKey(key);
    if (wall) {
        const wallSet = state.wallSets.get(wall.wallSetKey);
        if (!wallSet?.imageUrl) {
            return null;
        }
        const texture = loadTexture(state, wallSet.imageUrl);
        if (!texture?.loaded) {
            return null;
        }
        const size = wallSet.tileSize;
        return {
            image: texture.image,
            sx: wallSet.originX + (wall.frame % wallSet.columns) * size,
            sy: wallSet.originY + Math.floor(wall.frame / wallSet.columns) * size,
            sw: size,
            sh: size,
            tilesWide: 1,
            tilesHigh: 1,
            collision: [true],
            collisionStates: ["solid"]
        };
    }

    if (!key || !map?.tilesetKey) {
        return null;
    }

    const tileset = state.tilesets.get(map.tilesetKey);
    const definition = resolveDefinition(state, map, key);
    if (!definition || !tileset.imageUrl) {
        return null;
    }

    const texture = loadTexture(state, tileset.imageUrl);
    if (!texture?.loaded) {
        return null;
    }

    const size = tileset.tileSize;
    return {
        image: texture.image,
        sx: definition.x * size,
        sy: definition.y * size,
        sw: Math.max(1, definition.width) * size,
        sh: Math.max(1, definition.height) * size,
        tilesWide: Math.max(1, definition.width),
        tilesHigh: Math.max(1, definition.height),
        collision: definition.collision,
        collisionStates: definition.collisionStates
    };
}

function resolveDefinition(state, map, key) {
    if (!key || !map?.tilesetKey) {
        return null;
    }

    return state.tilesets.get(map.tilesetKey)?.definitions.get(key) ?? null;
}

/**
 * Draws a sprite anchored by its BOTTOM edge on the given tile. Authored art is
 * taller than its footprint - a tree's canopy overhangs the tile it stands on -
 * so anchoring at the top would sink it into the ground.
 */
function drawSpriteAtBase(ctx, state, px, sprite, tileX, tileY, footprintHeight, lift = 0) {
    const width = sprite.tilesWide * px;
    const height = sprite.tilesHigh * px;
    const x = tileX * px - state.renderCameraX;
    const baseY = (tileY + footprintHeight - lift) * px - state.renderCameraY;

    ctx.drawImage(sprite.image, sprite.sx, sprite.sy, sprite.sw, sprite.sh, x, baseY - height, width, height);
}

function getDecorationSpriteBounds(item, sprite, map = null) {
    let x = item.x + (item.width - sprite.tilesWide) / 2;
    if (item.zIndex === 5 && map) {
        const support = (map.decorations ?? []).find(other => other.zIndex === 0 &&
            item.x >= other.x && item.y >= other.y &&
            item.x + item.width <= other.x + other.width && item.y + item.height <= other.y + other.height);
        if (support) {
            const left = support.x + 0.25, right = support.x + support.width - sprite.tilesWide - 0.25;
            x = right >= left ? clamp(x, left, right) : support.x + (support.width - sprite.tilesWide) / 2;
        }
    }
    return getBottomAnchoredSpriteBounds(x, item.y - (item.zIndex === 5 ? 0.5 : 0),
        item.height, sprite.tilesWide, sprite.tilesHigh);
}

function getDecorationDepth(item, map) {
    if (item.zIndex !== 5) return item.y + item.height;
    const supports = (map.decorations ?? []).filter(other => other.zIndex === 0 &&
        rectanglesOverlap(item.x, item.y, item.width, item.height, other.x, other.y, other.width, other.height));
    return Math.max(item.y + item.height, ...supports.map(other => other.y + other.height)) + 0.01;
}

/**
 * Anything fully outside the viewport is skipped. A 512x512 map is a quarter of
 * a million tiles; drawing the ones nobody can see is the difference between a
 * smooth pan and a stuttering one.
 */
function isVisible(state, px, tileX, tileY, tilesWide, tilesHigh) {
    const left = tileX * px - state.renderCameraX;
    const top = tileY * px - state.renderCameraY;
    return left + tilesWide * px >= 0 &&
        top + tilesHigh * px >= 0 &&
        left <= state.viewportWidth &&
        top <= state.viewportHeight;
}

function tilePixelSize(state) {
    const map = getCurrentMap(state);
    return (map?.tileSize > 0 ? map.tileSize : 32) * state.currentScale;
}

function getCurrentMap(state) {
    return state.scene.maps.find((map) => map.id === state.currentMapId) ?? null;
}

/**
 * Re-measures whenever the backing store has drifted from the element's real
 * size. Resize events and ResizeObserver notifications are both unreliable
 * while the dock is animating a pane in, and a stale backing store stretches a
 * handful of pixels across the whole window, so the render loop self-heals.
 */
function ensureCanvasSize(state) {
    const rect = state.canvas.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) {
        return;
    }

    const dpr = Math.max(1, window.devicePixelRatio || 1);
    const targetWidth = Math.max(1, Math.floor(rect.width * dpr));
    const targetHeight = Math.max(1, Math.floor(rect.height * dpr));

    if (state.canvas.width !== targetWidth || state.canvas.height !== targetHeight) {
        resizeCanvas(state);
        updateCamera(state);
    }
}

function resizeCanvas(state) {
    const rect = state.canvas.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) {
        return;
    }

    const map = getCurrentMap(state);
    // Interiors are intentionally intimate. At the outdoor zoom an 18x13 room
    // can fit entirely inside a desktop pane, clamping the camera to zero and
    // making movement feel broken. One extra integer zoom step keeps pixel art
    // crisp while giving both desktop and mobile cameras room to follow.
    state.currentScale = getVillageRenderScale(rect.width, map?.mapKind) * state.zoom;
    state.viewportWidth = Math.max(1, Math.floor(rect.width));
    state.viewportHeight = Math.max(1, Math.floor(rect.height));
    state.devicePixelRatio = Math.max(1, window.devicePixelRatio || 1);
    state.canvas.width = Math.max(1, Math.floor(state.viewportWidth * state.devicePixelRatio));
    state.canvas.height = Math.max(1, Math.floor(state.viewportHeight * state.devicePixelRatio));
    state.ctx.setTransform(state.devicePixelRatio, 0, 0, state.devicePixelRatio, 0, 0);
    state.ctx.imageSmoothingEnabled = false;
}

function updatePlayer(state, deltaMs) {
    if (state.transitioning) return;
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    if (!player) {
        return;
    }

    if (player.moving) {
        player.progressMs += deltaMs;
        const t = clamp(player.progressMs / state.stepDurationMs, 0, 1);
        const eased = easeInOutQuad(t);
        player.renderX = lerp(player.startX, player.targetX, eased);
        player.renderY = lerp(player.startY, player.targetY, eased);

        if (t >= 1) {
            player.moving = false;
            player.tileX = player.targetX;
            player.tileY = player.targetY;
            player.renderX = player.tileX;
            player.renderY = player.tileY;
            reportLocalPosition(state);
            void checkPortalTransition(state);
        }
        return;
    }

    if (state.admin.enabled || state.build.enabled ||
        (state.keys.size === 0 && !state.touch.direction && !state.handheldDirection)) {
        state.moveAccumulatorMs = 0;
        return;
    }

    state.moveAccumulatorMs += deltaMs;
    if (state.moveAccumulatorMs < state.repeatDelayMs) {
        return;
    }

    state.moveAccumulatorMs = 0;
    const directionKey = getActiveDirectionKey(state);
    if (!directionKey) {
        return;
    }

    queueMovement(state, directionKey);
}

function queueMovement(state, directionKey) {
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    const map = getCurrentMap(state);
    if (!player || !map || player.moving || state.transitioning) {
        return false;
    }

    const direction = directionToVector(directionKey);
    if (!direction) {
        return false;
    }

    const nextX = player.tileX + direction.x;
    const nextY = player.tileY + direction.y;

    if (!isWalkableTile(state, map, nextX, nextY)) {
        return false;
    }

    player.startX = player.tileX;
    player.startY = player.tileY;
    player.targetX = nextX;
    player.targetY = nextY;
    player.progressMs = 0;
    player.moving = true;
    return true;
}

function ensureLocalPlayerPosition(state, mapId) {
    if (state.localPlayerByMap.has(mapId)) {
        return state.localPlayerByMap.get(mapId);
    }

    const map = state.scene.maps.find((item) => item.id === mapId);
    if (!map) {
        return null;
    }

    const spawn = map.spawnTile
        ? createPlayerState(map.spawnTile.x, map.spawnTile.y)
        : createPlayerState(Math.floor(map.width / 2), clamp(map.height - 2, 0, map.height - 1));

    state.localPlayerByMap.set(mapId, spawn);
    return spawn;
}

function updateCamera(state) {
    const map = getCurrentMap(state);
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    if (!map || !player) {
        state.cameraX = 0;
        state.cameraY = 0;
        state.renderCameraX = 0;
        state.renderCameraY = 0;
        return;
    }

    const px = tilePixelSize(state);
    const camera = getPlayerCenteredCamera(
        player.renderX,
        player.renderY,
        px,
        state.viewportWidth,
        state.viewportHeight);

    if (!state.build.enabled && map.mapKind === "Interior") {
        const width = map.width * px, height = map.height * px;
        camera.x = width <= state.viewportWidth ? (width - state.viewportWidth) / 2 : clamp(camera.x, -px / 2, width - state.viewportWidth + px / 2);
        camera.y = height <= state.viewportHeight - 100 ? (height - state.viewportHeight) / 2 - 20 : clamp(camera.y, -100, height - state.viewportHeight + 80);
    }
    state.cameraX = camera.x + (state.build.enabled ? (state.build.cameraOffset?.x ?? 0) * px : 0);
    state.cameraY = camera.y + (state.build.enabled ? (state.build.cameraOffset?.y ?? 0) * px : 0);

    state.renderCameraX = Math.round(state.cameraX);
    state.renderCameraY = Math.round(state.cameraY);
}

function draw(state) {
    const map = getCurrentMap(state);
    if (!map) {
        return;
    }

    ensureCanvasSize(state);

    const { ctx } = state;
    const px = tilePixelSize(state);
    ctx.clearRect(0, 0, state.viewportWidth, state.viewportHeight);

    const layer = ensureStaticLayer(state, map, px);
    if (layer) {
        // The whole static world is one pre-composited bitmap; blit only the
        // window the camera can see. The player-centred camera may extend past
        // a map edge, so the source rectangle must be clamped into the layer
        // and the remainder letterboxed. While
        // a zoom glide is in flight the layer may still be composed at the
        // previous scale, in which case the blit stretches it by the ratio
        // rather than recomposing the whole map every frame.
        ctx.fillStyle = map.mapKind === "Interior" ? "#101a20" : map.backgroundColor;
        ctx.fillRect(0, 0, state.viewportWidth, state.viewportHeight);
        const f = px / layer.px;
        const dx = Math.max(0, -state.renderCameraX);
        const dy = Math.max(0, -state.renderCameraY);
        const sx = Math.max(0, state.renderCameraX);
        const sy = Math.max(0, state.renderCameraY);
        const sw = Math.min(state.viewportWidth - dx, map.width * px - sx);
        const sh = Math.min(state.viewportHeight - dy, map.height * px - sy);
        if (sw > 0 && sh > 0) {
            ctx.drawImage(layer.canvas, sx / f, sy / f, sw / f, sh / f, dx, dy, sw, sh);
        }
    } else {
        drawMapBase(ctx, map, state, px);
        drawGroundTiles(ctx, map, state, px);
    }

    drawInteriorBoundaryMask(ctx, map, state, px);
    drawPlots(ctx, map.plots, state.selectedPlotId, state, px);
    drawPortalHints(ctx, map, state, px);
    drawWorldSorted(ctx, map, state, px);
    drawAdminOverlay(ctx, map, state, px);
    drawBuildOverlay(ctx, map, state, px);
    drawBubbles(ctx, state, px);
    drawTouchStick(ctx, state);
}

// Safari rejects canvases above ~16.7 million pixels; past that the static
// layer silently fails, so those maps fall back to per-frame tile drawing.
const MAX_STATIC_LAYER_PIXELS = 12_000_000;

/**
 * Returns the offscreen composite of everything on this map that never moves:
 * the base tile fill and the ground layer. Rebuilt when the map or scale
 * changes, and invalidated whenever a texture or tileset arrives late, so a
 * frame is never more than one blit behind the freshest art.
 */
function ensureStaticLayer(state, map, px) {
    const cached = state.staticLayer;
    if (cached && cached.mapId === map.id) {
        if (cached.px === px) {
            return cached;
        }

        // Mid-glide the scale changes every frame; recomposing a whole map per
        // frame would undo the point of the cache, so the stale layer is
        // served for a scaled blit until the zoom settles.
        if (state.zoom !== state.targetZoom) {
            return cached;
        }
    }

    const width = map.width * px;
    const height = map.height * px;
    if (width < 1 || height < 1 || width * height > MAX_STATIC_LAYER_PIXELS) {
        state.staticLayer = null;
        return null;
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const layerCtx = canvas.getContext("2d");
    layerCtx.imageSmoothingEnabled = false;

    // A texture that finishes loading after this compose must trigger a
    // rebuild, or the layer would keep the fallback art forever. The identity
    // guard matters: loadTexture fires callbacks for already-loaded textures
    // too (via microtask), and rebuilding on those would recompose forever.
    const invalidate = () => {
        if (state.staticLayer?.canvas === canvas) {
            state.staticLayer = null;
            draw(state);
        }
    };

    layerCtx.fillStyle = map.backgroundColor;
    layerCtx.fillRect(0, 0, width, height);

    const base = map.baseTileTextureUrl
        ? textureForCompose(state, map.baseTileTextureUrl, invalidate)
        : null;
    if (base?.loaded) {
        for (let y = 0; y < map.height; y++) {
            for (let x = 0; x < map.width; x++) {
                layerCtx.drawImage(base.image, x * px, y * px, px, px);
            }
        }
    }

    const baseSprite = resolveStaticSprite(state, map, map.baseTileDefinitionKey, invalidate);
    if (baseSprite) {
        for (let y = 0; y < map.height; y++)
            for (let x = 0; x < map.width; x++)
                layerCtx.drawImage(baseSprite.image, baseSprite.sx, baseSprite.sy, baseSprite.sw, baseSprite.sh,
                    x * px, y * px, px, px);
    }

    for (const item of [...(map.groundTiles ?? [])].sort((a, b) => a.zIndex - b.zIndex)) {
        const sprite = resolveStaticSprite(state, map, item.definitionKey, invalidate);
        if (sprite) {
            const w = sprite.tilesWide * px;
            const h = sprite.tilesHigh * px;
            layerCtx.drawImage(
                sprite.image, sprite.sx, sprite.sy, sprite.sw, sprite.sh,
                item.x * px, (item.y + item.height) * px - h, w, h);
        }
    }

    state.staticLayer = { mapId: map.id, px, canvas };
    return state.staticLayer;
}

/**
 * Fetches a texture for the static layer, subscribing the rebuild callback
 * ONLY while the texture is still in flight. Subscribing unconditionally would
 * fire on every already-loaded cache hit and recompose the layer in a loop.
 */
function textureForCompose(state, url, invalidate) {
    const texture = loadTexture(state, url);
    if (texture && !texture.loaded && !texture.failed) {
        loadTexture(state, url, invalidate);
    }

    return texture;
}

/**
 * resolveSprite, but wired to invalidate the static layer when its sheet
 * finishes loading rather than only repainting the live canvas.
 */
function resolveStaticSprite(state, map, key, invalidate) {
    if (!key || !map?.tilesetKey) {
        return null;
    }

    const tileset = state.tilesets.get(map.tilesetKey);
    const definition = resolveDefinition(state, map, key);
    if (!definition || !tileset.imageUrl) {
        return null;
    }

    const texture = textureForCompose(state, tileset.imageUrl, invalidate);
    if (!texture?.loaded) {
        return null;
    }

    const size = tileset.tileSize;
    return {
        image: texture.image,
        sx: definition.x * size,
        sy: definition.y * size,
        sw: Math.max(1, definition.width) * size,
        sh: Math.max(1, definition.height) * size,
        tilesWide: Math.max(1, definition.width),
        tilesHigh: Math.max(1, definition.height)
    };
}

/**
 * Everything that stands up off the ground is drawn in one pass ordered by the
 * bottom of its footprint, so a member walking in front of a tree overlaps it
 * and a member behind it is hidden by the canopy. Drawing objects and characters
 * in separate passes makes that impossible no matter how each pass is ordered.
 */
function drawWorldSorted(ctx, map, state, px) {
    const drawables = [];

    for (const item of map.decorations ?? []) {
        if (!isDecorationVisible(state, map, item, px)) {
            continue;
        }

        drawables.push({
            sort: getDecorationDepth(item, map),
            draw: () => drawDecoration(ctx, item, map, state, px)
        });
    }

    for (const building of map.buildings ?? []) {
        if (!isBuildingVisible(state, map, building, px)) {
            continue;
        }

        drawables.push({
            sort: building.y + building.height,
            draw: () => drawBuilding(ctx, building, map, state, px, building.id === state.selectedBuildingId)
        });
    }

    const player = ensureLocalPlayerPosition(state, state.currentMapId);

    for (const remote of state.remotes.values()) {
        drawables.push({
            sort: remote.renderY + 1,
            draw: () => drawCharacter(ctx, state, px, remote.renderX, remote.renderY, remote, false)
        });
    }

    for (const character of state.scene.characters ?? []) {
        if (character.isLocalPlayer || character.mapId !== state.currentMapId) {
            continue;
        }

        drawables.push({
            sort: character.y + 1,
            draw: () => drawCharacter(ctx, state, px, character.x, character.y, character, false)
        });
    }

    if (player) {
        drawables.push({
            sort: player.renderY + 1,
            draw: () => drawCharacter(ctx, state, px, player.renderX, player.renderY, state.localAppearance, true)
        });
    }

    drawables.sort((a, b) => a.sort - b.sort);
    for (const item of drawables) {
        item.draw();
    }
}

function isDecorationVisible(state, map, item, px) {
    if (parseWallDefinitionKey(item.definitionKey))
        return isVisible(state, px, item.x, item.y - 2, item.width, item.height + 2);
    const sprite = resolveSprite(state, map, item.definitionKey);
    if (!sprite) {
        return isVisible(state, px, item.x, item.y, item.width, item.height);
    }

    const bounds = getDecorationSpriteBounds(item, sprite, map);
    return isVisible(state, px, bounds.x, bounds.y, bounds.width, bounds.height);
}

function primeMapTextures(state) {
    for (const map of state.scene.maps ?? []) {
        if (map.baseTileTextureUrl) {
            loadTexture(state, map.baseTileTextureUrl);
        }

        for (const decoration of map.decorations ?? []) {
            if (decoration.textureUrl) {
                loadTexture(state, decoration.textureUrl);
            }
        }
    }

    for (const character of state.scene.characters ?? []) {
        if (character.avatarUrl) {
            loadTexture(state, character.avatarUrl);
        }
    }
}

function loadTexture(state, url, onLoaded) {
    return loadCachedTexture(state.textureCache, url, onLoaded);
}

function drawMapBase(ctx, map, state, px) {
    ctx.fillStyle = map.mapKind === "Interior" ? "#101a20" : map.backgroundColor;
    ctx.fillRect(0, 0, state.viewportWidth, state.viewportHeight);

    const texture = map.baseTileTextureUrl ? loadTexture(state, map.baseTileTextureUrl) : null;
    const sprite = resolveSprite(state, map, map.baseTileDefinitionKey);
    if (!texture?.loaded && !sprite) {
        return;
    }

    // Only the tiles the camera can see; a large map off-screen is not free.
    const minX = Math.max(0, Math.floor(state.renderCameraX / px));
    const minY = Math.max(0, Math.floor(state.renderCameraY / px));
    const maxX = Math.min(map.width, Math.ceil((state.renderCameraX + state.viewportWidth) / px));
    const maxY = Math.min(map.height, Math.ceil((state.renderCameraY + state.viewportHeight) / px));

    for (let y = minY; y < maxY; y++) {
        for (let x = minX; x < maxX; x++) {
            if (sprite) {
                ctx.drawImage(sprite.image, sprite.sx, sprite.sy, sprite.sw, sprite.sh,
                    x * px - state.renderCameraX, y * px - state.renderCameraY, px, px);
                continue;
            }
            ctx.drawImage(
                texture.image,
                x * px - state.renderCameraX,
                y * px - state.renderCameraY,
                px,
                px);
        }
    }
}

function drawGroundTiles(ctx, map, state, px) {
    for (const item of [...(map.groundTiles ?? [])].sort((a, b) => a.zIndex - b.zIndex)) {
        if (!isVisible(state, px, item.x, item.y, item.width, item.height)) {
            continue;
        }

        const sprite = resolveSprite(state, map, item.definitionKey);
        if (sprite) {
            drawSpriteAtBase(ctx, state, px, sprite, item.x, item.y, item.height);
        }
    }
}

function drawPlots(ctx, plots, selectedPlotId, state, px) {
    for (const plot of plots) {
        const x = plot.x * px - state.renderCameraX;
        const y = plot.y * px - state.renderCameraY;
        const width = plot.width * px;
        const height = plot.height * px;

        const selected = plot.id === selectedPlotId;
        ctx.fillStyle = plot.forSale
            ? "rgba(255, 213, 105, 0.08)"
            : plot.isOwnedByLocalMember
                ? "rgba(104, 218, 178, 0.07)"
                : plot.fillColor;
        ctx.fillRect(x, y, width, height);
        ctx.strokeStyle = selected
            ? "#ffe8a3"
            : plot.forSale
                ? "rgba(255, 211, 101, 0.72)"
                : plot.isOwnedByLocalMember
                    ? "rgba(104, 218, 178, 0.58)"
                    : plot.borderColor;
        ctx.lineWidth = selected ? 3 : 1.5;
        ctx.setLineDash([8, 6]);
        ctx.strokeRect(x + 1, y + 1, width - 2, height - 2);
        ctx.setLineDash([]);
    }
}

function pointerTile(state, event, map) {
    const rect = state.canvas.getBoundingClientRect();
    const px = tilePixelSize(state);
    return {
        x: clamp(Math.floor((event.clientX - rect.left + state.renderCameraX) / px), 0, map.width - 1),
        y: clamp(Math.floor((event.clientY - rect.top + state.renderCameraY) / px), 0, map.height - 1),
    };
}

function drawAdminOverlay(ctx, map, state, px) {
    if (!state.admin.enabled || !state.admin.start || !state.admin.current) return;
    const x = Math.min(state.admin.start.x, state.admin.current.x);
    const y = Math.min(state.admin.start.y, state.admin.current.y);
    const width = Math.abs(state.admin.current.x - state.admin.start.x) + 1;
    const height = Math.abs(state.admin.current.y - state.admin.start.y) + 1;
    const screenX = x * px - state.renderCameraX;
    const screenY = y * px - state.renderCameraY;
    ctx.save();
    ctx.fillStyle = "rgba(0, 250, 255, 0.16)";
    ctx.strokeStyle = "#00faff";
    ctx.lineWidth = 2;
    ctx.setLineDash([7, 4]);
    ctx.fillRect(screenX, screenY, width * px, height * px);
    ctx.strokeRect(screenX + 1, screenY + 1, width * px - 2, height * px - 2);
    ctx.setLineDash([]);
    ctx.fillStyle = "rgba(3, 11, 16, 0.88)";
    ctx.fillRect(screenX + 5, screenY + 5, 58, 22);
    ctx.fillStyle = "#fff";
    ctx.font = "700 12px sans-serif";
    ctx.fillText(`${width} × ${height}`, screenX + 11, screenY + 20);
    ctx.restore();
}

async function submitBuildSelection(state, map, tileX, tileY) {
    if (state.build.tool === "Pan") return;
    if (state.build.tool === "Move" && !state.build.movingObject) {
        const selected = findBuildObjectAt(state, map, tileX, tileY);
        const definition = (state.scene.buildCatalog ?? []).find(item => item.key === selected?.definitionKey);
        if (!selected || selected.zIndex <= -100 || !definition ||
            !isEditableBuildBounds(map, selected.x, selected.y, selected.width, selected.height)) return;
        state.build.movingObject = selected;
        state.build.definition = definition;
        draw(state);
        return;
    }
    const object = state.build.tool === "Erase"
        ? findBuildObjectAt(state, map, tileX, tileY)
        : null;
    const definition = state.build.definition;
    const width = object?.width ?? (isStrokeBuildTool(state.build.tool) ? 1 : definition?.footprintWidth ?? 1);
    const height = object?.height ?? (isStrokeBuildTool(state.build.tool) ? 1 : definition?.footprintHeight ?? 1);
    const targetX = object?.x ?? tileX;
    const targetY = object?.y ?? tileY;
    const valid = object
        ? isEditableBuildBounds(map, targetX, targetY, width, height)
        : state.build.tool !== "Erase" &&
          definition &&
          isValidBuildPlacement(state, map, targetX, targetY, width, height);
    if (!valid) {
        return;
    }

    const submitKey = `${targetX},${targetY},${object?.id ?? ""},${state.build.tool}`;
    const now = performance.now();
    if (state.build.submitting ||
        (submitKey === state.build.lastSubmitKey && now - state.build.lastSubmitAt <= 350)) {
        return;
    }

    state.build.lastSubmitKey = submitKey;
    state.build.lastSubmitAt = now;
    state.build.submitting = true;
    try {
        await invokeDotNet(state, "OnBuildTileSelected", targetX, targetY, object?.id ?? state.build.movingObject?.id ?? null);
    } finally {
        state.build.submitting = false;
    }
}

function addBuildStrokeCell(state, map, tileX, tileY) {
    if (!state.build.stroke ||
        (state.build.tool === "Paint" && !state.build.definition) ||
        (state.build.tool === "Walls" && !state.build.wallSet)) {
        return;
    }

    const brushSize = Math.max(1, Number(state.build.brush?.size) || 1);
    const brushRadius = Math.floor(brushSize / 2);
    if (!isValidBuildPlacement(
        state,
        map,
        tileX - brushRadius,
        tileY - brushRadius,
        brushSize,
        brushSize)) {
        return;
    }

    state.build.stroke.cells.set(`${tileX},${tileY}`, { x: tileX, y: tileY });
}

function replaceBuildStrokeArea(state, map, start, end) {
    if (!state.build.stroke || !start || !end) {
        return;
    }

    state.build.stroke.cells.clear();
    const minX = Math.max(0, Math.min(start.x, end.x));
    const maxX = Math.min(map.width - 1, Math.max(start.x, end.x));
    const minY = Math.max(0, Math.min(start.y, end.y));
    const maxY = Math.min(map.height - 1, Math.max(start.y, end.y));
    for (const cell of buildRectangleCells(
        { x: minX, y: minY }, { x: maxX, y: maxY }, state.build.tool === "Walls")) {
        addBuildStrokeCell(state, map, cell.x, cell.y);
    }
}

function addBuildStrokeLine(state, map, start, end) {
    if (!start) {
        addBuildStrokeCell(state, map, end.x, end.y);
        return;
    }

    let x = start.x;
    let y = start.y;
    const dx = Math.abs(end.x - start.x);
    const dy = Math.abs(end.y - start.y);
    const stepX = start.x < end.x ? 1 : -1;
    const stepY = start.y < end.y ? 1 : -1;
    let error = dx - dy;

    while (true) {
        addBuildStrokeCell(state, map, x, y);
        if (x === end.x && y === end.y) {
            break;
        }

        const twiceError = error * 2;
        if (twiceError > -dy) {
            error -= dy;
            x += stepX;
        }
        if (twiceError < dx) {
            error += dx;
            y += stepY;
        }
    }
}

function applyOptimisticTerrainStroke(state, stroke) {
    const map = getCurrentMap(state);
    const tileset = map?.tilesetKey ? state.tilesets.get(map.tilesetKey) : null;
    const manualBrush = state.build.brush?.key === stroke.selectionKey
        ? state.build.brush
        : null;
    if (!map || !tileset ||
        (!manualBrush && !tileset.terrainIndex?.has(stroke.selectionKey))) {
        return null;
    }

    const cells = [...stroke.cells.values()];
    if (cells.length === 0) {
        return null;
    }

    let rollback = state.build.optimisticRollback;
    if (rollback && String(rollback.mapId) !== String(map.id)) {
        restoreOptimisticTerrain(state);
        rollback = null;
    }

    if (!rollback) {
        const originalGroundTiles = (map.groundTiles ?? []).map(item => ({ ...item }));
        const terrainGrid = new Array(map.width * map.height)
            .fill(map.mapKind === "Outdoor" ? "grass" : "");
        const originalPositions = new Set();
        for (const item of originalGroundTiles) {
            if (item.zIndex > -100) continue;
            if (item.x < 0 || item.y < 0 || item.x >= map.width || item.y >= map.height) {
                continue;
            }

            const positionKey = `${item.x},${item.y}`;
            if (originalPositions.has(positionKey)) {
                continue;
            }
            originalPositions.add(positionKey);
            const definition = tileset.definitions.get(item.definitionKey);
            terrainGrid[item.y * map.width + item.x] = item.zIndex === -100
                ? definition?.terrainKey ?? ""
                : "";
        }

        rollback = {
            token: state.build.nextOptimisticId++,
            mapId: map.id,
            groundTiles: originalGroundTiles,
            terrainGrid,
            paintedCells: new Set(),
            manualChoices: new Map(),
        };
        state.build.optimisticRollback = rollback;
    }

    map.groundTiles ??= [];
    const byPosition = new Map();
    for (const item of [...(map.groundTiles ?? [])].sort((a, b) => a.zIndex - b.zIndex)) {
        if (item.zIndex > -100) continue;
        const key = `${item.x},${item.y}`;
        if (!byPosition.has(key)) {
            byPosition.set(key, item);
        }
    }

    if (manualBrush) {
        return applyOptimisticManualBrush(
            state,
            map,
            tileset,
            stroke,
            manualBrush,
            cells,
            rollback,
            byPosition);
    }

    const newCells = [];
    for (const cell of cells) {
        const key = `${cell.x},${cell.y}`;
        if (rollback.paintedCells.has(key)) {
            continue;
        }

        rollback.paintedCells.add(key);
        rollback.terrainGrid[cell.y * map.width + cell.x] = stroke.selectionKey;
        newCells.push(cell);
    }

    if (newCells.length === 0) {
        return rollback.token;
    }

    const affected = new Map();
    for (const cell of newCells) {
        for (let y = Math.max(0, cell.y - 1); y <= Math.min(map.height - 1, cell.y + 1); y++) {
            for (let x = Math.max(0, cell.x - 1); x <= Math.min(map.width - 1, cell.x + 1); x++) {
                affected.set(`${x},${y}`, { x, y });
            }
        }
    }

    for (const [key, position] of affected) {
        const isTarget = rollback.paintedCells.has(key);
        let item = byPosition.get(key);
        if (!isTarget && !item) {
            continue;
        }

        const resolved = resolveTerrainCell(
            rollback.terrainGrid,
            map.width,
            map.height,
            position.x,
            position.y,
            tileset.terrainIndex);
        if (!resolved) {
            continue;
        }

        if (!item) {
            item = {
                id: `optimistic-terrain-${state.build.nextOptimisticId++}`,
                kind: resolved.key,
                definitionKey: resolved.key,
                x: position.x,
                y: position.y,
                width: 1,
                height: 1,
                zIndex: -100,
                blocksMovement: false,
                rotation: 0,
                ownerMemberId: state.scene.localMemberId,
                isOwnedByLocalMember: true,
            };
            map.groundTiles.push(item);
            byPosition.set(key, item);
        } else {
            item.kind = resolved.key;
            item.definitionKey = resolved.key;
        }

        if (isTarget) {
            item.zIndex = -100;
            item.blocksMovement = false;
            item.ownerMemberId = state.scene.localMemberId;
            item.isOwnedByLocalMember = true;
        }
    }

    state.staticLayer = null;
    draw(state);
    return rollback.token;
}

function applyOptimisticManualBrush(
    state,
    map,
    tileset,
    stroke,
    brush,
    centers,
    rollback,
    byPosition) {
    const changedTargets = new Map();
    const size = Math.max(1, Number(brush.size) || 1);
    const radius = Math.floor(size / 2);
    for (const center of centers) {
        const centerKey = `${center.x},${center.y}`;
        if (rollback.paintedCells.has(centerKey)) {
            continue;
        }
        rollback.paintedCells.add(centerKey);

        for (let index = 0; index < (brush.cells?.length ?? 0); index++) {
            const cell = brush.cells[index];
            const definitionKey = cell?.definitionKey ?? "";
            if (!definitionKey) {
                continue;
            }

            const x = center.x - radius + index % size;
            const y = center.y - radius + Math.floor(index / size);
            if (x < 0 || y < 0 || x >= map.width || y >= map.height) {
                continue;
            }

            const key = `${x},${y}`;
            const choice = {
                definitionKey,
                strength: Math.max(1, Number(cell.strength) || 1),
                weight: Math.max(1, Number(cell.weight) || 1),
            };
            const current = rollback.manualChoices.get(key);
            if (current &&
                (choice.strength < current.strength ||
                 (choice.strength === current.strength && choice.weight < current.weight))) {
                continue;
            }

            rollback.manualChoices.set(key, choice);
            rollback.terrainGrid[y * map.width + x] = "";
            changedTargets.set(key, { x, y });
        }
    }

    if (changedTargets.size === 0) {
        return rollback.token;
    }

    const affected = new Map();
    for (const position of changedTargets.values()) {
        for (let y = Math.max(0, position.y - 1); y <= Math.min(map.height - 1, position.y + 1); y++) {
            for (let x = Math.max(0, position.x - 1); x <= Math.min(map.width - 1, position.x + 1); x++) {
                affected.set(`${x},${y}`, { x, y });
            }
        }
    }

    for (const [key, position] of affected) {
        const manualChoice = rollback.manualChoices.get(key);
        let item = byPosition.get(key);
        let resolved = manualChoice
            ? tileset.definitions.get(manualChoice.definitionKey)
            : null;

        if (!manualChoice) {
            if (!item || item.zIndex !== -100) {
                continue;
            }
            resolved = resolveTerrainCell(
                rollback.terrainGrid,
                map.width,
                map.height,
                position.x,
                position.y,
                tileset.terrainIndex);
        }
        if (!resolved) {
            continue;
        }

        if (!item) {
            item = {
                id: `optimistic-terrain-${state.build.nextOptimisticId++}`,
                kind: resolved.key,
                definitionKey: resolved.key,
                x: position.x,
                y: position.y,
                width: 1,
                height: 1,
                zIndex: manualChoice ? -101 : -100,
                blocksMovement: manualChoice
                    ? (resolved.collision ?? []).some(Boolean)
                    : false,
                rotation: 0,
                ownerMemberId: state.scene.localMemberId,
                isOwnedByLocalMember: true,
            };
            map.groundTiles.push(item);
            byPosition.set(key, item);
        } else {
            item.kind = resolved.key;
            item.definitionKey = resolved.key;
        }

        if (manualChoice) {
            item.zIndex = -101;
            item.blocksMovement = (resolved.collision ?? []).some(Boolean);
            item.ownerMemberId = state.scene.localMemberId;
            item.isOwnedByLocalMember = true;
        }
    }

    state.collisionByMap.delete(map.id);
    state.staticLayer = null;
    draw(state);
    return rollback.token;
}

function restoreOptimisticTerrain(state) {
    const rollback = state.build.optimisticRollback;
    if (!rollback) {
        return;
    }

    const map = state.scene.maps?.find(item => String(item.id) === String(rollback.mapId));
    if (map) {
        map.groundTiles = rollback.groundTiles;
        state.collisionByMap.delete(map.id);
    }
    state.build.optimisticRollback = null;
    state.staticLayer = null;
}

async function submitBuildStroke(state, stroke) {
    const cells = [...stroke.cells.values()];
    if (cells.length === 0 || state.build.submitting) {
        return;
    }

    const isWallStroke = state.build.tool === "Walls";
    const optimisticToken = isWallStroke
        ? null
        : stroke.optimisticToken ?? applyOptimisticTerrainStroke(state, stroke);
    state.build.submitting = true;
    try {
        await invokeDotNet(state,
            isWallStroke ? "OnBuildWallStrokeSelected" : "OnBuildTerrainStrokeSelected",
            cells,
            stroke.selectionKey);
    } finally {
        state.build.submitting = false;
        if (optimisticToken !== null &&
            state.build.optimisticRollback?.token === optimisticToken) {
            restoreOptimisticTerrain(state);
            draw(state);
        }
    }
}

function updateBuildHover(state, event) {
    const rect = state.canvas.getBoundingClientRect();
    const px = tilePixelSize(state);
    state.build.hoverX = Math.floor((event.clientX - rect.left + state.renderCameraX) / px);
    state.build.hoverY = Math.floor((event.clientY - rect.top + state.renderCameraY) / px);
    draw(state);
}

function drawBuildOverlay(ctx, map, state, px) {
    if (!state.build.enabled || state.build.tool === "Pan" || state.build.pan) {
        return;
    }

    ctx.save();
    const editableAreas = map.canEdit
        ? [{ x: 0, y: 0, width: map.width, height: map.height }]
        : (map.plots ?? []).filter(plot => plot.canEdit);

    ctx.fillStyle = "rgba(84, 220, 159, 0.075)";
    ctx.strokeStyle = "rgba(119, 239, 187, 0.8)";
    ctx.lineWidth = 2;
    ctx.setLineDash([7, 5]);
    for (const area of editableAreas) {
        const x = area.x * px - state.renderCameraX;
        const y = area.y * px - state.renderCameraY;
        ctx.fillRect(x, y, area.width * px, area.height * px);
        ctx.strokeRect(x + 1, y + 1, area.width * px - 2, area.height * px - 2);
    }
    ctx.setLineDash([]);

    if (state.build.stroke?.cells?.size > 0) {
        ctx.fillStyle = "rgba(83, 226, 157, 0.32)";
        ctx.strokeStyle = "rgba(131, 240, 185, 0.9)";
        ctx.lineWidth = 1.5;
        const positions = new Set((map.decorations ?? [])
            .filter(item => parseWallDefinitionKey(item.definitionKey)).map(item => `${item.x},${item.y}`));
        for (const cell of state.build.stroke.cells.values()) positions.add(`${cell.x},${cell.y}`);
        for (const cell of state.build.stroke.cells.values()) {
            if (state.build.tool === "Walls") {
                const frame = resolveWallFrame(getWallNeighborMask((x, y) => positions.has(`${x},${y}`), cell.x, cell.y));
                ctx.globalAlpha = 0.85;
                drawWallTile(ctx, state, map, px, cell.x, cell.y, state.build.wallSet, frame);
                ctx.globalAlpha = 1;
            }
            const x = cell.x * px - state.renderCameraX;
            const y = cell.y * px - state.renderCameraY;
            ctx.fillRect(x, y, px, px);
            ctx.strokeRect(x + 0.75, y + 0.75, px - 1.5, px - 1.5);
        }
    }

    if (state.build.hoverX === null || state.build.hoverY === null) {
        ctx.restore();
        return;
    }

    const object = state.build.tool === "Erase"
        ? findBuildObjectAt(state, map, state.build.hoverX, state.build.hoverY)
        : null;
    const definition = state.build.definition;
    const brushSize = state.build.tool === "Paint"
        ? Math.max(1, Number(state.build.brush?.size) || 1)
        : 1;
    const brushRadius = Math.floor(brushSize / 2);
    const tileX = object?.x ?? (state.build.hoverX - brushRadius);
    const tileY = object?.y ?? (state.build.hoverY - brushRadius);
    const width = object?.width ?? (isStrokeBuildTool(state.build.tool) ? brushSize : definition?.footprintWidth ?? 1);
    const height = object?.height ?? (isStrokeBuildTool(state.build.tool) ? brushSize : definition?.footprintHeight ?? 1);
    const valid = object
        ? isEditableBuildBounds(map, tileX, tileY, width, height)
        : isValidBuildPlacement(state, map, tileX, tileY, width, height);

    if (!object && definition && state.build.tool !== "Erase" && !state.build.brush) {
        const sprite = resolveSprite(state, map, definition.key);
        if (sprite) {
            ctx.globalAlpha = valid ? 0.68 : 0.35;
            drawDecoration(ctx, { definitionKey: definition.key, x: tileX, y: tileY, width, height,
                zIndex: definition.placementLayer === "Surface" ? 5 : 0 }, map, state, px);
            ctx.globalAlpha = 1;
        }
    }

    if (!object && state.build.tool === "Walls" && state.build.wallSet) {
        ctx.globalAlpha = valid ? 0.72 : 0.36;
        const positions = new Set((map.decorations ?? [])
            .filter(item => parseWallDefinitionKey(item.definitionKey))
            .map(item => `${item.x},${item.y}`));
        for (const cell of state.build.stroke?.cells?.values() ?? []) positions.add(`${cell.x},${cell.y}`);
        positions.add(`${tileX},${tileY}`);
        const frame = resolveWallFrame(getWallNeighborMask((x, y) => positions.has(`${x},${y}`), tileX, tileY));
        drawWallTile(ctx, state, map, px, tileX, tileY, state.build.wallSet, frame);
        ctx.globalAlpha = 1;
    }

    const screenX = tileX * px - state.renderCameraX;
    const screenY = tileY * px - state.renderCameraY;
    ctx.fillStyle = valid ? "rgba(83, 226, 157, 0.2)" : "rgba(255, 100, 112, 0.22)";
    ctx.strokeStyle = valid ? "#83f0b9" : "#ff7b87";
    ctx.lineWidth = 3;
    ctx.fillRect(screenX, screenY, width * px, height * px);
    ctx.strokeRect(screenX + 1.5, screenY + 1.5, width * px - 3, height * px - 3);
    ctx.restore();
}

function isValidBuildPlacement(state, map, x, y, width, height) {
    if (!isEditableBuildBounds(map, x, y, width, height)) {
        return false;
    }
    if (state.build.tool === "Paint" || state.build.tool === "Erase") {
        return true;
    }
    if (x < 0 || y < 0 || x + width > map.width || y + height > map.height) {
        return false;
    }
    if (rectanglesOverlap(x, y, width, height, map.spawnTile?.x ?? -1, map.spawnTile?.y ?? -1, 1, 1)) {
        return false;
    }
    if ((map.buildings ?? []).some(item =>
        rectanglesOverlap(x, y, width, height, item.x, item.y, item.width, item.height))) {
        return false;
    }
    const layer = state.build.definition?.placementLayer;
    if (state.build.tool === "Walls" || (layer !== "Floor" && layer !== "Surface" && state.build.definition?.blocksMovement)) {
        const people = [...(state.remotes?.values() ?? [])];
        const local = state.localPlayerByMap?.get(map.id);
        if (local) people.push(local);
        if (people.some(person => rectanglesOverlap(x, y, width, height, person.tileX, person.tileY, 1, 1))) return false;
    }
    if (state.build.tool !== "Walls" && layer === "Floor") {
        return !(map.groundTiles ?? []).some(item => item.zIndex === -10 && item.id !== state.build.movingObject?.id &&
            rectanglesOverlap(x, y, width, height, item.x, item.y, item.width, item.height));
    }
    let supported = layer !== "Surface";
    for (const item of map.decorations ?? []) {
        if (item.id === state.build.movingObject?.id) continue;
        if (!rectanglesOverlap(x, y, width, height, item.x, item.y, item.width, item.height)) continue;
        if (state.build.tool === "Walls" && parseWallDefinitionKey(item.definitionKey)) continue;
        const definition = (state.scene.buildCatalog ?? []).find(entry => entry.key === item.definitionKey);
        if (layer === "Surface" && item.zIndex === 0 && definition?.supportsItems &&
            x >= item.x && y >= item.y && x + width <= item.x + item.width && y + height <= item.y + item.height) {
            supported = true;
            continue;
        }
        return false;
    }
    return supported;
}

function isEditableBuildBounds(map, x, y, width, height) {
    if (map.canEdit) {
        return x >= 0 && y >= 0 && x + width <= map.width && y + height <= map.height;
    }
    return (map.plots ?? []).some(plot =>
        plot.canEdit &&
        x >= plot.x && y >= plot.y &&
        x + width <= plot.x + plot.width &&
        y + height <= plot.y + plot.height);
}

function rectanglesOverlap(ax, ay, aw, ah, bx, by, bw, bh) {
    return ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
}

function findBuildObjectAt(state, map, tileX, tileY) {
    for (const building of [...(map.buildings ?? [])].reverse()) {
        const sprite = resolveSprite(state, map, building.spriteKey);
        const bounds = sprite
            ? getBottomAnchoredSpriteBounds(
                building.x, building.y, building.height, sprite.tilesWide, sprite.tilesHigh)
            : building;
        if (rectContains(bounds, tileX, tileY)) {
            return building;
        }
    }

    const decorations = [...(map.decorations ?? [])].sort((a, b) => getDecorationDepth(b, map) - getDecorationDepth(a, map));
    for (const item of decorations) {
        const sprite = resolveSprite(state, map, item.definitionKey);
        const bounds = sprite
            ? getDecorationSpriteBounds(item, sprite, map)
            : item;
        if (rectContains(bounds, tileX, tileY)) {
            return item;
        }
    }

    return [...(map.groundTiles ?? [])].sort((a, b) => b.zIndex - a.zIndex).find(item =>
        rectContains(item, tileX, tileY)) ?? null;
}

/**
 * Prefers the authored sprite for this object; falls back to the old primitive
 * when the tileset has no such key, so a half-authored world stays readable.
 */
function drawDecoration(ctx, item, map, state, px) {
    const wallInfo = parseWallDefinitionKey(item.definitionKey);
    if (wallInfo && drawWallTile(ctx, state, map, px, item.x, item.y,
        state.wallSets.get(wallInfo.wallSetKey), wallInfo.frame)) return;
    const sprite = resolveSprite(state, map, item.definitionKey);
    if (sprite) {
        const bounds = getDecorationSpriteBounds(item, sprite, map);
        ctx.drawImage(sprite.image, sprite.sx, sprite.sy, sprite.sw, sprite.sh,
            bounds.x * px - state.renderCameraX, bounds.y * px - state.renderCameraY,
            bounds.width * px, bounds.height * px);
        return;
    }

    const x = item.x * px - state.renderCameraX;
    const y = item.y * px - state.renderCameraY;

    const wall = parseWallDefinitionKey(item.definitionKey);
    if (wall) {
        drawWallFallback(ctx, state, px, item.x, item.y, state.wallSets.get(wall.wallSetKey), wall.frame);
        return;
    }

    const texture = item.textureUrl ? loadTexture(state, item.textureUrl) : null;
    if (texture?.loaded) {
        for (let tileY = 0; tileY < item.height; tileY++) {
            for (let tileX = 0; tileX < item.width; tileX++) {
                ctx.drawImage(
                    texture.image,
                    (item.x + tileX) * px - state.renderCameraX,
                    (item.y + tileY) * px - state.renderCameraY,
                    px,
                    px);
            }
        }
        return;
    }

    if (item.kind === "Tree") {
        ctx.fillStyle = "#6f4f2f";
        ctx.fillRect(x + 0.38 * px, y + 0.5 * px, 0.24 * px, 0.5 * px);
        ctx.beginPath();
        ctx.fillStyle = item.color;
        ctx.arc(x + 0.5 * px, y + 0.45 * px, 0.42 * px, 0, Math.PI * 2);
        ctx.fill();
        return;
    }

    ctx.fillStyle = item.color;
    roundRect(ctx, x, y, item.width * px, item.height * px, px * 0.16, true, false);
}

function drawInteriorBoundaryMask(ctx, map, state, px) {
    if (map.mapKind !== "Interior") return;
    ctx.fillStyle = "#101a20";
    for (const item of map.decorations ?? []) {
        const wall = parseWallDefinitionKey(item.definitionKey);
        if (!wall || state.wallSets.get(wall.wallSetKey)?.layout !== "RoomBuilder") continue;
        const x = item.x * px - state.renderCameraX, y = item.y * px - state.renderCameraY;
        if (item.x === 0) ctx.fillRect(x, y, 5 / 16 * px, px);
        if (item.x === map.width - 1) ctx.fillRect(x + 11 / 16 * px, y, 5 / 16 * px, px);
        if (item.y === map.height - 1) ctx.fillRect(x, y + 11 / 16 * px, px, 5 / 16 * px);
    }
}

function drawWallTile(ctx, state, map, px, tileX, tileY, wallSet, frame) {
    if (wallSet?.layout !== "RoomBuilder") {
        const sprite = wallSet && resolveSprite(state, map, makeWallDefinitionKey(wallSet.key, frame));
        if (sprite) drawSpriteAtBase(ctx, state, px, sprite, tileX, tileY, 1);
        else drawWallFallback(ctx, state, px, tileX, tileY, wallSet, frame);
        return true;
    }
    const texture = loadTexture(state, wallSet.imageUrl);
    if (!texture?.loaded) {
        drawWallFallback(ctx, state, px, tileX, tileY, wallSet, frame);
        return true;
    }
    wallSet.renderedFrames ??= new Map();
    let wallImage = wallSet.renderedFrames.get(frame);
    if (!wallImage) {
        wallImage = typeof OffscreenCanvas !== "undefined"
            ? new OffscreenCanvas(ROOM_WALL_SIZE, ROOM_WALL_SIZE + ROOM_WALL_RISE)
            : document.createElement("canvas");
        wallImage.width = ROOM_WALL_SIZE;
        wallImage.height = ROOM_WALL_SIZE + ROOM_WALL_RISE;
        renderRoomWall(wallImage.getContext("2d"), texture.image, wallSet.originX, wallSet.originY,
            wallMaskForFrame(frame), wallSet.topColor || "#f5f5f3");
        wallSet.renderedFrames.set(frame, wallImage);
    }
    ctx.drawImage(wallImage, tileX * px - state.renderCameraX,
        (tileY - ROOM_WALL_RISE / ROOM_WALL_SIZE) * px - state.renderCameraY,
        px, wallImage.height / ROOM_WALL_SIZE * px);
    return true;
}

function drawWallFallback(ctx, state, px, tileX, tileY, wallSet, frame = 46) {
    const x = tileX * px - state.renderCameraX;
    const y = tileY * px - state.renderCameraY;
    const mask = wallMaskForFrame(frame);
    const left = mask & 64 ? 0 : 0.14;
    const right = mask & 4 ? 1 : 0.86;
    const top = mask & 1 ? 0 : 0.14;
    const bottom = mask & 16 ? 1 : 0.72;
    ctx.save();
    ctx.fillStyle = "rgba(6, 14, 16, 0.24)";
    ctx.fillRect(x + left * px, y + 0.3 * px, (right - left) * px, 0.7 * px);
    ctx.fillStyle = wallSet?.faceColor || "#758782";
    ctx.fillRect(x + left * px, y + top * px, (right - left) * px, (bottom - top + 0.25) * px);
    ctx.fillStyle = wallSet?.topColor || "#c9d5cf";
    ctx.fillRect(x + left * px, y + (top - 0.18) * px, (right - left) * px, (bottom - top) * px);
    if (!(mask & 16)) {
        ctx.fillStyle = "rgba(19, 31, 34, 0.35)";
        ctx.fillRect(x + left * px, y + (bottom + 0.18) * px, (right - left) * px, 0.06 * px);
    }
    ctx.restore();
}

function isStrokeBuildTool(tool) {
    return tool === "Paint" || tool === "Walls";
}

/**
 * Prefers the authored building sprite; falls back to the blocky roof-and-wall
 * primitive otherwise. The selection outline is drawn either way.
 */
function drawBuilding(ctx, building, map, state, px, isSelected) {
    const x = building.x * px - state.renderCameraX;
    const y = building.y * px - state.renderCameraY;
    const width = building.width * px;
    const height = building.height * px;

    const sprite = resolveSprite(state, map, building.spriteKey);
    if (sprite) {
        drawSpriteAtBase(ctx, state, px, sprite, building.x, building.y, building.height);
    } else {
        ctx.fillStyle = building.roofColor;
        ctx.beginPath();
        ctx.moveTo(x - 0.2 * px, y + 0.55 * px);
        ctx.lineTo(x + width / 2, y - 0.45 * px);
        ctx.lineTo(x + width + 0.2 * px, y + 0.55 * px);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = building.color;
        roundRect(ctx, x, y + 0.45 * px, width, height - 0.45 * px, px * 0.2, true, false);

        const entrance = getBuildingEntrance(building);
        ctx.fillStyle = "#62462c";
        ctx.fillRect(
            entrance.x * px - state.renderCameraX + 0.2 * px,
            entrance.y * px - state.renderCameraY + 0.15 * px,
            0.6 * px,
            0.85 * px);

        ctx.fillStyle = "rgba(255,255,255,0.65)";
        ctx.fillRect(x + width * 0.18, y + height * 0.72, width * 0.18, height * 0.16);
        ctx.fillRect(x + width * 0.64, y + height * 0.72, width * 0.18, height * 0.16);
    }

    if (isSelected) {
        const selectedX = building.x * px - state.renderCameraX;
        const selectedY = sprite
            ? (building.y + building.height - sprite.tilesHigh) * px - state.renderCameraY
            : y;
        const selectedWidth = (sprite?.tilesWide ?? building.width) * px;
        const selectedHeight = (sprite?.tilesHigh ?? building.height) * px;
        ctx.strokeStyle = "#ffe07f";
        ctx.lineWidth = 3;
        ctx.strokeRect(selectedX - 2, selectedY - 2, selectedWidth + 4, selectedHeight + 4);
    }
}

function isBuildingVisible(state, map, building, px) {
    const sprite = resolveSprite(state, map, building.spriteKey);
    const top = building.y + building.height - (sprite?.tilesHigh ?? building.height);
    return isVisible(
        state,
        px,
        building.x,
        top,
        sprite?.tilesWide ?? building.width,
        sprite?.tilesHigh ?? building.height);
}

function buildingContainsTile(state, map, building, tileX, tileY) {
    const sprite = resolveSprite(state, map, building.spriteKey);
    const left = building.x;
    const right = building.x + (sprite?.tilesWide ?? building.width);
    const bottom = building.y + building.height;
    const top = bottom - (sprite?.tilesHigh ?? building.height);

    return tileX >= left && tileX < right && tileY >= top && tileY < bottom;
}

function drawPortalHints(ctx, map, state, px) {
    for (const portal of map.portals ?? []) {
        drawDoorTile(ctx, portal.x, portal.y, state, px, portal.color ?? "#fff2a8");
    }
}

function drawDoorTile(ctx, tileX, tileY, state, px, color) {
    const x = tileX * px - state.renderCameraX;
    const y = tileY * px - state.renderCameraY;
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.55;
    roundRect(ctx, x + 0.14 * px, y + 0.14 * px, 0.72 * px, 0.72 * px, 0.16 * px, true, false);
    ctx.globalAlpha = 1;
}

/**
 * Eases each remote toward the tile the server last reported. Remote positions
 * arrive at walking cadence rather than per frame, so without this they would
 * visibly teleport a tile at a time.
 */
/**
 * Feeds the current geometry to the audio graph. Done per frame rather than per
 * network update so panning follows the eased positions the player actually
 * sees, instead of jumping a tile at a time.
 */
function updateSpatialAudio(state) {
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    if (player) {
        state.spatialAudio.setListener(player.renderX, player.renderY);
    }

    for (const [userId, peer] of state.voicePeers) {
        const remote = state.remotes.get(peer.userId);
        if (!remote) {
            state.spatialAudio.remove(userId);
            const existingElement = state.voiceElements.get(userId);
            if (existingElement) {
                // A participant outside the current map is outside the spatial
                // space. This remains intentionally silent even if Web Audio
                // itself is unavailable; disabling spatial sound restores it.
                existingElement.muted = state.spatialAudioEnabled;
            }
            continue;
        }

        let element = state.voiceElements.get(userId);
        if (!element?.isConnected) {
            element = findCallAudioElement(peer.peerId);
            if (element) {
                state.voiceElements.set(userId, element);
            }
        }

        const routed = state.spatialAudio.upsert(
            userId,
            remote.renderX,
            remote.renderY,
            element?.srcObject instanceof MediaStream ? element.srcObject : null,
            element?.volume ?? 1);

        if (element) {
            element.muted = state.spatialAudioEnabled && routed;
        }
    }
}

function findCallAudioElement(peerId) {
    if (!peerId) {
        return null;
    }

    return document.getElementById(getCallAudioElementId(peerId));
}

/**
 * Browsers keep an AudioContext suspended until a gesture, so the first key or
 * click in the village is what actually starts positional voice.
 */
function unlockAudio(state) {
    if (state.audioUnlocked) {
        return;
    }

    state.audioUnlocked = true;
    void state.spatialAudio.resume();
}

function updateRemotes(state, deltaMs) {
    // Covers one tile in roughly the same time the local player takes to walk
    // it, so remote and local movement read at the same speed.
    const rate = deltaMs / state.stepDurationMs;

    for (const remote of state.remotes.values()) {
        const dx = remote.tileX - remote.renderX;
        const dy = remote.tileY - remote.renderY;

        if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
            remote.renderX = remote.tileX;
            remote.renderY = remote.tileY;
            continue;
        }

        // Snap rather than glide when someone is far away: that is a teleport
        // through a door or a late join, not a walk.
        if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
            remote.renderX = remote.tileX;
            remote.renderY = remote.tileY;
            continue;
        }

        remote.renderX += Math.sign(dx) * Math.min(Math.abs(dx), rate);
        remote.renderY += Math.sign(dy) * Math.min(Math.abs(dy), rate);
    }
}

/**
 * Tells Blazor where the local player ended up so it can broadcast the move.
 */
function reportLocalPosition(state) {
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    if (!player) {
        return;
    }

    const map = getCurrentMap(state);

    state.moveReportPromise = invokeDotNet(
        state,
        "OnLocalMoved",
        player.tileX,
        player.tileY,
        facingFromDirection(state.lastDirectionKey),
        // Building occupancy is authoritative per interior map. Treating the
        // outdoor doorway row as "inside" races room acquisition ahead of the
        // portal join, which the server correctly rejects because presence is
        // still outdoors at that instant.
        map?.parentBuildingId ?? null,
        state.currentMapId);
}

function facingFromDirection(directionKey) {
    if (directionKey === "up") return 3;
    if (directionKey === "left") return 1;
    if (directionKey === "right") return 2;
    return 0;
}

/**
 * Chat bubbles above whoever said them. Drawn after every character so a bubble
 * is never hidden behind someone standing in front of the speaker.
 */
/**
 * The on-screen stick, drawn only while a finger is down. Positions are in
 * viewport space rather than world space so it does not scroll with the camera.
 */
/**
 * Where the resting joystick affordance sits: bottom-left, floated above
 * whatever bottom HUD (composer, hint bar) is currently present.
 */
function getTouchStickAnchor(state) {
    const bottomInset = getBottomHudInset(state);
    return {
        x: 84,
        y: state.viewportHeight - bottomInset - 84,
    };
}

function getBottomHudInset(state) {
    const host = state.canvas.parentElement;
    if (!host) {
        return 0;
    }

    let elements = state.bottomHudElements;
    if (!elements || elements.host !== host || elements.list.some((element) => element && !element.isConnected)) {
        elements = {
            host,
            list: [
                host.querySelector(":scope > .village-chat-composer"),
                host.querySelector(":scope > .village-movement-hint"),
            ],
        };
        state.bottomHudElements = elements;
    }

    const canvasBottom = state.canvas.getBoundingClientRect().bottom;
    let bottom = 0;
    for (const element of elements.list) {
        if (element) {
            bottom = Math.max(bottom, canvasBottom - element.getBoundingClientRect().top);
        }
    }

    return clamp(bottom, 0, state.viewportHeight * 0.4);
}

/**
 * The resting affordance for touch players. The floating stick has always
 * worked from any touch, but nothing on screen said so; this ghost is the
 * "there is a joystick" signal, and touching it anchors the stick in place.
 */
function drawTouchStickGhost(ctx, state) {
    const anchor = getTouchStickAnchor(state);
    const radius = 46;

    ctx.save();
    ctx.globalAlpha = 0.3;

    ctx.beginPath();
    ctx.arc(anchor.x, anchor.y, radius, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(10, 12, 18, 0.55)";
    ctx.fill();
    ctx.lineWidth = 2;
    ctx.strokeStyle = "rgba(255, 255, 255, 0.55)";
    ctx.stroke();

    ctx.beginPath();
    ctx.arc(anchor.x, anchor.y, radius * 0.4, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(255, 255, 255, 0.6)";
    ctx.fill();

    // Four direction ticks so it reads as a movement control at a glance.
    ctx.fillStyle = "rgba(255, 255, 255, 0.65)";
    for (const [dx, dy, rot] of [[0, -1, 0], [1, 0, Math.PI / 2], [0, 1, Math.PI], [-1, 0, -Math.PI / 2]]) {
        ctx.save();
        ctx.translate(anchor.x + dx * (radius - 11), anchor.y + dy * (radius - 11));
        ctx.rotate(rot);
        ctx.beginPath();
        ctx.moveTo(0, -5);
        ctx.lineTo(5, 3);
        ctx.lineTo(-5, 3);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
    }

    ctx.restore();
}

function drawTouchStick(ctx, state) {
    if (state.build.enabled || state.admin.enabled) {
        return;
    }
    if (!state.touch.active || !state.touch.moved) {
        if (state.showTouchControls && !state.touch.active && !state.pinch.active) {
            drawTouchStickGhost(ctx, state);
        }

        return;
    }

    const rect = state.canvas.getBoundingClientRect();
    const originX = state.touch.originX - rect.left;
    const originY = state.touch.originY - rect.top;

    const maxRadius = 46;
    const distance = Math.min(maxRadius, Math.hypot(state.touch.dx, state.touch.dy));
    const angle = Math.atan2(state.touch.dy, state.touch.dx);
    const knobX = originX + Math.cos(angle) * distance;
    const knobY = originY + Math.sin(angle) * distance;

    ctx.save();
    ctx.globalAlpha = 0.5;

    ctx.beginPath();
    ctx.arc(originX, originY, maxRadius, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(10, 12, 18, 0.5)";
    ctx.fill();
    ctx.lineWidth = 2;
    ctx.strokeStyle = "rgba(255, 255, 255, 0.5)";
    ctx.stroke();

    ctx.beginPath();
    ctx.arc(knobX, knobY, maxRadius * 0.42, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(255, 255, 255, 0.85)";
    ctx.fill();

    ctx.restore();
}

function drawBubbles(ctx, state, px) {
    const now = performance.now();

    for (const [userId, queue] of [...state.bubbles.entries()]) {
        const visible = queue.filter(
            bubble => now - bubble.bornAt <= VILLAGE_BUBBLE_HOLD_MS + VILLAGE_BUBBLE_FADE_MS);
        if (visible.length === 0) {
            state.bubbles.delete(userId);
            continue;
        }
        state.bubbles.set(userId, visible);

        const position = resolveBubbleAnchor(state, userId);
        if (!position) {
            continue;
        }

        // Newest stays closest to the speaker; older messages climb upward.
        let verticalOffset = 0;
        for (let index = visible.length - 1; index >= 0; index--) {
            const bubble = visible[index];
            const alpha = getVillageBubbleAlpha(now - bubble.bornAt);
            const boxHeight = drawBubble(
                ctx,
                state,
                px,
                position.x,
                position.y,
                bubble.text,
                alpha,
                verticalOffset,
                index === visible.length - 1);
            verticalOffset += boxHeight + Math.max(4, px * 0.12);
        }
    }
}

/**
 * Bubbles follow the eased render position so they travel with the speaker
 * rather than snapping between tiles.
 */
function resolveBubbleAnchor(state, userId) {
    const localId = state.localAppearance?.userId;
    if (localId !== undefined && String(userId) === String(localId)) {
        const player = ensureLocalPlayerPosition(state, state.currentMapId);
        return player ? { x: player.renderX, y: player.renderY } : null;
    }

    const remote = state.remotes.get(userId) ?? state.remotes.get(Number(userId));
    if (remote) {
        return { x: remote.renderX, y: remote.renderY };
    }

    return null;
}

function drawBubble(ctx, state, px, tileX, tileY, text, alpha, verticalOffset, showTail) {
    const fontSize = Math.max(10, Math.round(px * 0.24));
    ctx.font = `500 ${fontSize}px var(--font-family, sans-serif)`;

    const paddingX = fontSize * 0.6;
    const paddingY = fontSize * 0.42;
    const maxWidth = px * 6;
    const lines = wrapBubbleText(ctx, text, maxWidth);
    const lineHeight = fontSize * 1.25;

    let width = 0;
    for (const line of lines) {
        width = Math.max(width, ctx.measureText(line).width);
    }

    const boxWidth = width + paddingX * 2;
    const boxHeight = lines.length * lineHeight + paddingY * 2;

    const centerX = (tileX + 0.5) * px - state.renderCameraX;
    const bottomY = (tileY + 0.35) * px - state.renderCameraY - px * 0.42 - verticalOffset;
    const boxX = centerX - boxWidth / 2;
    const boxY = bottomY - boxHeight;

    ctx.save();
    ctx.globalAlpha = Math.max(0, Math.min(1, alpha));

    ctx.fillStyle = "rgba(16, 18, 26, 0.88)";
    ctx.strokeStyle = "rgba(255, 255, 255, 0.18)";
    ctx.lineWidth = 1;
    roundRect(ctx, boxX, boxY, boxWidth, boxHeight, fontSize * 0.5, true, true);

    if (showTail) {
        // Only the bubble nearest the avatar needs a tail; repeating tails in
        // the stack makes the older cards look like speech from empty space.
        ctx.beginPath();
        ctx.moveTo(centerX - fontSize * 0.32, boxY + boxHeight);
        ctx.lineTo(centerX, boxY + boxHeight + fontSize * 0.45);
        ctx.lineTo(centerX + fontSize * 0.32, boxY + boxHeight);
        ctx.closePath();
        ctx.fill();
    }

    ctx.fillStyle = "#f2f4f8";
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    for (let i = 0; i < lines.length; i++) {
        ctx.fillText(lines[i], centerX, boxY + paddingY + i * lineHeight);
    }

    ctx.textAlign = "start";
    ctx.textBaseline = "alphabetic";
    ctx.restore();
    return boxHeight;
}

function wrapBubbleText(ctx, text, maxWidth) {
    const words = text.split(/\s+/);
    const lines = [];
    let current = "";

    for (const word of words) {
        const candidate = current ? `${current} ${word}` : word;
        if (ctx.measureText(candidate).width > maxWidth && current) {
            lines.push(current);
            current = word;
        } else {
            current = candidate;
        }
    }

    if (current) {
        lines.push(current);
    }

    // Three lines is plenty for a glance; the channel holds the rest.
    return lines.slice(0, 3);
}

function drawCharacter(ctx, state, px, x, y, character, isLocalPlayer) {
    const centerX = (x + 0.5) * px - state.renderCameraX;
    const centerY = (y + 0.35) * px - state.renderCameraY;
    const radius = 0.36 * px;
    const accent = character.accentColor || "#4780d9";

    ctx.save();
    ctx.beginPath();
    ctx.ellipse(centerX, centerY + radius * 0.95, radius * 0.8, radius * 0.32, 0, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(0, 0, 0, 0.28)";
    ctx.fill();
    ctx.restore();

    const texture = character.avatarUrl ? loadTexture(state, character.avatarUrl) : null;

    ctx.save();
    ctx.beginPath();
    ctx.arc(centerX, centerY, radius, 0, Math.PI * 2);
    ctx.closePath();

    if (texture?.loaded) {
        ctx.clip();
        // Cover-fit so non-square avatars are cropped rather than squashed.
        const source = Math.min(texture.image.width, texture.image.height);
        ctx.drawImage(
            texture.image,
            (texture.image.width - source) / 2,
            (texture.image.height - source) / 2,
            source,
            source,
            centerX - radius,
            centerY - radius,
            radius * 2,
            radius * 2);
    } else {
        ctx.fillStyle = accent;
        ctx.fill();
    }

    ctx.restore();

    ctx.beginPath();
    ctx.arc(centerX, centerY, radius, 0, Math.PI * 2);
    ctx.strokeStyle = isLocalPlayer ? "#ffffff" : accent;
    ctx.lineWidth = Math.max(2, px * (isLocalPlayer ? 0.07 : 0.05));
    ctx.stroke();

    drawCharacterName(ctx, state, px, centerX, centerY + radius, character);
}

function drawCharacterName(ctx, state, px, centerX, bottomY, character) {
    if (!character.name) {
        return;
    }

    const fontSize = Math.max(9, Math.round(px * 0.26));
    ctx.font = `600 ${fontSize}px var(--font-family, sans-serif)`;
    ctx.textAlign = "center";
    ctx.textBaseline = "top";

    const y = bottomY + fontSize * 0.35;
    ctx.lineWidth = Math.max(2, fontSize * 0.3);
    ctx.strokeStyle = "rgba(0, 0, 0, 0.72)";
    ctx.lineJoin = "round";
    ctx.strokeText(character.name, centerX, y);
    ctx.fillStyle = "#ffffff";
    ctx.fillText(character.name, centerX, y);

    ctx.textAlign = "start";
    ctx.textBaseline = "alphabetic";
}

function isWalkableTile(state, map, tileX, tileY) {
    if (tileX < 0 || tileY < 0 || tileX >= map.width || tileY >= map.height) {
        return false;
    }

    return !getCollisionSet(state, map).has(tileKey(tileX, tileY));
}

/**
 * Collision is derived from the authored objects themselves - decorations that
 * block, building footprints, and any standalone blockers the map declares -
 * rather than a parallel list that has to be kept in sync by hand.
 */
function getCollisionSet(state, map) {
    const cached = state.collisionByMap.get(map.id);
    if (cached) {
        return cached;
    }

    const blocked = new Set();
    const addRect = (rect) => {
        if (!rect) {
            return;
        }

        for (let y = rect.y; y < rect.y + rect.height; y++) {
            for (let x = rect.x; x < rect.x + rect.width; x++) {
                blocked.add(tileKey(x, y));
            }
        }
    };

    for (const rect of map.blockedTiles ?? []) {
        addRect(rect);
    }

    for (const decoration of map.decorations ?? []) {
        if (!decoration.blocksMovement) {
            continue;
        }

        const definition = resolveDefinition(state, map, decoration.definitionKey);
        if (decoration.definitionKey?.toLowerCase().startsWith("buildings.")) {
            if (definition?.collision) {
                for (const cell of getBottomAnchoredCollisionCells(decoration.x, decoration.y, decoration.height, definition)) {
                    if (rectContains(decoration, cell.x, cell.y)) blocked.add(tileKey(cell.x, cell.y));
                }
            } else {
                addRect(decoration);
            }
        } else if (definition?.collision?.some(Boolean)) {
            for (const cell of getBottomAnchoredCollisionCells(
                decoration.x,
                decoration.y,
                decoration.height,
                definition)) {
                blocked.add(tileKey(cell.x, cell.y));
            }
        } else {
            addRect(decoration);
        }
    }

    for (const building of map.buildings ?? []) {
        for (const rect of building.collisionRects ?? []) {
            addRect(rect);
        }
        for (const entrance of getBuildingEntrances(building)) {
            blocked.delete(tileKey(entrance.x, entrance.y));
        }
    }

    // Doorways win over the footprint they sit in, otherwise a building whose
    // collision covers its own entrance can never be entered.
    for (const portal of map.portals ?? []) {
        blocked.delete(tileKey(portal.x, portal.y));
    }

    state.collisionByMap.set(map.id, blocked);
    return blocked;
}

function tileKey(x, y) {
    return `${x},${y}`;
}

async function checkPortalTransition(state) {
    const map = getCurrentMap(state);
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    if (!map || !player) {
        return;
    }

    const entranceBuilding = (map.buildings ?? []).find(building =>
        building.interiorMapId &&
        getBuildingEntrances(building).some(entrance =>
            entrance.x === player.tileX && entrance.y === player.tileY));
    const portal = entranceBuilding
        ? null
        : (map.portals ?? []).find((item) => item.x === player.tileX && item.y === player.tileY);
    const targetMapId = entranceBuilding?.interiorMapId ?? portal?.targetMapId;
    if (!targetMapId) {
        return;
    }

    const targetMap = state.scene.maps.find((item) => item.id === targetMapId);
    if (!targetMap) {
        return;
    }

    await transitionToMap(state, targetMap.id, portal ? {x: portal.targetX, y: portal.targetY} : null);
}

async function transitionToMap(state, mapId, destination) {
    if (state.transitioning || state.currentMapId === mapId) return;
    const targetMap = state.scene.maps.find(map => map.id === mapId);
    if (!targetMap) return;
    state.transitioning = true;
    state.keys.clear();
    state.touch.direction = null;
    state.handheldDirection = null;
    const previousMapId = state.currentMapId;
    const previousPlayer = ensureLocalPlayerPosition(state, previousMapId);
    try {
        await state.moveReportPromise;
        const player = ensureLocalPlayerPosition(state, mapId);
        const arrival = destination ?? targetMap.spawnTile ?? {x: player.tileX, y: player.tileY};
        teleportPlayer(player, arrival.x, arrival.y);
        state.zoomByMap.set(state.currentMapId, state.targetZoom);
        state.zoom = state.targetZoom = state.zoomByMap.get(mapId) ?? (targetMap.mapKind === "Interior" ? 0.5 : 0.65);
        state.currentMapId = mapId;
        state.build.enabled = false;
        state.admin.enabled = false;
        state.canvas.classList.remove("admin-mode");
        await invokeDotNet(state, "OnZoomChanged", Math.round(state.zoom * 100));
        state.selectedBuildingId = null;
        state.selectedPlotId = null;
        state.build.cameraOffset = {x: 0, y: 0};
        state.moveAccumulatorMs = 0;
        resizeCanvas(state);
        updateCamera(state);
        const joined = await invokeDotNet(state, "OnMapChanged", mapId, player.tileX, player.tileY);
        if (joined === false) {
            state.currentMapId = previousMapId;
            await invokeDotNet(state, "OnMapChanged", previousMapId, previousPlayer.tileX, previousPlayer.tileY);
        }
        await invokeDotNet(state, "OnBuildingSelected", null, state.currentMapId);
        await invokeDotNet(state, "OnPlotSelected", null, state.currentMapId);
    } finally {
        state.transitioning = false;
        state.canvas.focus({preventScroll: true});
        resizeCanvas(state);
        updateCamera(state);
        draw(state);
    }
}

function getBuildingEntrance(building) {
    return building.entranceTile ?? getBuildingEntrances(building)[0];
}

function getBuildingEntrances(building) {
    if (Array.isArray(building.entranceTiles) && building.entranceTiles.length > 0) {
        return building.entranceTiles;
    }
    return [building.entranceTile ?? {
        x: building.x + Math.floor(building.width / 2),
        y: building.y + building.height - 1
    }];
}

function rectContains(rect, x, y) {
    return x >= rect.x && x < rect.x + rect.width && y >= rect.y && y < rect.y + rect.height;
}

function teleportPlayer(player, x, y) {
    player.tileX = x;
    player.tileY = y;
    player.renderX = x;
    player.renderY = y;
    player.startX = x;
    player.startY = y;
    player.targetX = x;
    player.targetY = y;
    player.progressMs = 0;
    player.moving = false;
}

async function notifySelection(state) {
    const map = getCurrentMap(state);
    if (!map) {
        return;
    }

    await invokeDotNet(state, "OnBuildingSelected", state.selectedBuildingId, map.id);
    await invokeDotNet(state, "OnPlotSelected", state.selectedPlotId, map.id);
}

/**
 * Movement keys are captured at the window level, so the runtime must not swallow
 * them while the user is typing elsewhere or while its own tab is hidden behind
 * another dock window.
 */
function acceptsInput(state, event) {
    if (state.destroyed || isTextInput(event.target)) {
        return false;
    }

    return state.canvas.isConnected && state.canvas.offsetParent !== null;
}

/**
 * Blazor disposes the .NET reference before the runtime is torn down in some
 * teardown orders, so every callback is best-effort.
 */
async function invokeDotNet(state, methodName, ...args) {
    if (state.destroyed || !state.dotNetRef) {
        return;
    }

    try {
        return await state.dotNetRef.invokeMethodAsync(methodName, ...args);
    } catch (error) {
        if (!state.destroyed) console.warn(`Village ${methodName} failed`, error);
        return false;
    }
}

function directionToVector(directionKey) {
    if (directionKey === "up") return { x: 0, y: -1 };
    if (directionKey === "down") return { x: 0, y: 1 };
    if (directionKey === "left") return { x: -1, y: 0 };
    if (directionKey === "right") return { x: 1, y: 0 };
    return null;
}

function getActiveDirectionKey(state) {
    if (state.handheldDirection) {
        return state.handheldDirection;
    }

    // A held joystick wins over stale keyboard state.
    if (state.touch.direction) {
        return state.touch.direction;
    }

    if (state.lastDirectionKey && state.keys.has(state.lastDirectionKey)) {
        return state.lastDirectionKey;
    }

    for (const key of ["up", "down", "left", "right"]) {
        if (state.keys.has(key)) {
            return key;
        }
    }

    return null;
}

function roundRect(ctx, x, y, width, height, radius, fill, stroke) {
    const r = Math.min(radius, width / 2, height / 2);
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + width, y, x + width, y + height, r);
    ctx.arcTo(x + width, y + height, x, y + height, r);
    ctx.arcTo(x, y + height, x, y, r);
    ctx.arcTo(x, y, x + width, y, r);
    ctx.closePath();
    if (fill) ctx.fill();
    if (stroke) ctx.stroke();
}

function lerp(a, b, t) {
    return a + (b - a) * t;
}

function easeInOutQuad(t) {
    return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
}

export { isValidBuildPlacement, replaceBuildStrokeArea, restoreOptimisticTerrain, drawWallTile,
    drawDecoration, getDecorationSpriteBounds, getDecorationDepth, drawInteriorBoundaryMask };

export function isHandledPointerClick(event, handled, now) {
    return event.type === "click" && !!handled && now - handled.at < 500 &&
        Math.abs(event.clientX - handled.x) < 1 && Math.abs(event.clientY - handled.y) < 1;
}
