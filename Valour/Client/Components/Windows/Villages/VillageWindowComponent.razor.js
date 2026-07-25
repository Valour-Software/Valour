import { clamp, isTextInput, loadTexture as loadCachedTexture } from "../../../ts/VillageTileRendering.js";

export function init(canvasId, dotNetRef, scene) {
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
        selectedBuildingId: null,
        keys: new Set(),
        lastDirectionKey: null,
        repeatDelayMs: 160,
        moveAccumulatorMs: 0,
        stepDurationMs: 130,
        animationFrame: 0,
        destroyed: false,
        lastTimestamp: 0,
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
        textureCache: new Map()
    };

    for (const character of scene.characters) {
        if (character.isLocalPlayer) {
            state.localPlayerByMap.set(character.mapId, createPlayerState(character.x, character.y));
        }
    }

    const runtime = {
        resetView() {
            state.selectedBuildingId = null;
            updateCamera(state);
            notifySelection(state);
            draw(state);
        },
        async setMap(mapId) {
            if (state.currentMapId === mapId) {
                return;
            }

            ensureLocalPlayerPosition(state, mapId);
            state.currentMapId = mapId;
            state.selectedBuildingId = null;
            state.moveAccumulatorMs = 0;
            resizeCanvas(state);
            updateCamera(state);
            await invokeDotNet(state, "OnMapChanged", mapId);
            await invokeDotNet(state, "OnBuildingSelected", null, mapId);
            draw(state);
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
        dispose() {
            state.destroyed = true;
            window.removeEventListener("resize", state.onResize);
            window.removeEventListener("keydown", state.onKeyDown);
            window.removeEventListener("keyup", state.onKeyUp);
            window.removeEventListener("blur", state.onBlur);
            canvas.removeEventListener("click", state.onClick);
            state.resizeObserver?.disconnect();
            state.resizeObserver = null;
            if (state.animationFrame) {
                cancelAnimationFrame(state.animationFrame);
                state.animationFrame = 0;
            }
            state.keys.clear();
        }
    };

    state.onResize = () => {
        resizeCanvas(state);
        updateCamera(state);
        draw(state);
    };

    state.onKeyDown = (event) => {
        const normalizedKey = normalizeMovementKey(event.key);
        if (!normalizedKey || !acceptsInput(state, event)) {
            return;
        }

        event.preventDefault();
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

        const building = map.buildings.find((item) =>
            tileX >= item.x &&
            tileX < item.x + item.width &&
            tileY >= item.y &&
            tileY < item.y + item.height);

        state.selectedBuildingId = building ? building.id : null;
        await notifySelection(state);
        draw(state);
    };

    state.onBlur = () => {
        state.keys.clear();
        state.lastDirectionKey = null;
        state.moveAccumulatorMs = 0;
    };

    window.addEventListener("resize", state.onResize);
    window.addEventListener("keydown", state.onKeyDown);
    window.addEventListener("keyup", state.onKeyUp);
    window.addEventListener("blur", state.onBlur);
    canvas.addEventListener("click", state.onClick);

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
    ensureLocalPlayerPosition(state, state.currentMapId);
    resizeCanvas(state);
    updateCamera(state);

    function frame(timestamp) {
        if (state.destroyed) {
            return;
        }

        const delta = state.lastTimestamp === 0 ? 16 : Math.min(40, timestamp - state.lastTimestamp);
        state.lastTimestamp = timestamp;
        updatePlayer(state, delta);
        updateRemotes(state, delta);
        updateCamera(state);
        draw(state);
        state.animationFrame = requestAnimationFrame(frame);
    }

    state.animationFrame = requestAnimationFrame(frame);
    draw(state);
    notifySelection(state);
    return runtime;
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

    state.currentScale = rect.width < 760 ? 1 : 2;
    state.viewportWidth = Math.max(1, Math.floor(rect.width));
    state.viewportHeight = Math.max(1, Math.floor(rect.height));
    state.devicePixelRatio = Math.max(1, window.devicePixelRatio || 1);
    state.canvas.width = Math.max(1, Math.floor(state.viewportWidth * state.devicePixelRatio));
    state.canvas.height = Math.max(1, Math.floor(state.viewportHeight * state.devicePixelRatio));
    state.ctx.setTransform(state.devicePixelRatio, 0, 0, state.devicePixelRatio, 0, 0);
    state.ctx.imageSmoothingEnabled = false;
}

function updatePlayer(state, deltaMs) {
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

    if (state.keys.size === 0) {
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
    if (!player || !map || player.moving) {
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
    const mapWidthPx = map.width * px;
    const mapHeightPx = map.height * px;
    const targetX = (player.renderX + 0.5) * px - state.viewportWidth / 2;
    const targetY = (player.renderY + 0.6) * px - state.viewportHeight / 2;

    state.cameraX = clamp(targetX, 0, Math.max(0, mapWidthPx - state.viewportWidth));
    state.cameraY = clamp(targetY, 0, Math.max(0, mapHeightPx - state.viewportHeight));
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

    drawMapBase(ctx, map, state, px);
    drawDecorations(ctx, map.decorations, state, px);
    drawPlots(ctx, map.plots, state, px);
    drawBuildings(ctx, map.buildings, state.selectedBuildingId, state, px);
    drawPortalHints(ctx, map, state, px);
    drawCharacters(ctx, state, px);
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

function loadTexture(state, url) {
    return loadCachedTexture(state.textureCache, url);
}

function drawMapBase(ctx, map, state, px) {
    ctx.fillStyle = map.backgroundColor;
    ctx.fillRect(0, 0, state.viewportWidth, state.viewportHeight);

    const texture = map.baseTileTextureUrl ? loadTexture(state, map.baseTileTextureUrl) : null;
    if (!texture?.loaded) {
        return;
    }

    for (let y = 0; y < map.height; y++) {
        for (let x = 0; x < map.width; x++) {
            ctx.drawImage(
                texture.image,
                x * px - state.renderCameraX,
                y * px - state.renderCameraY,
                px,
                px);
        }
    }
}

function drawPlots(ctx, plots, state, px) {
    for (const plot of plots) {
        const x = plot.x * px - state.renderCameraX;
        const y = plot.y * px - state.renderCameraY;
        const width = plot.width * px;
        const height = plot.height * px;

        ctx.fillStyle = plot.fillColor;
        ctx.fillRect(x, y, width, height);
        ctx.strokeStyle = plot.borderColor;
        ctx.lineWidth = 2;
        ctx.strokeRect(x + 1, y + 1, width - 2, height - 2);
    }
}

function drawDecorations(ctx, decorations, state, px) {
    for (const item of decorations) {
        const x = item.x * px - state.renderCameraX;
        const y = item.y * px - state.renderCameraY;

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
            continue;
        }

        if (item.kind === "Tree") {
            ctx.fillStyle = "#6f4f2f";
            ctx.fillRect(x + 0.38 * px, y + 0.5 * px, 0.24 * px, 0.5 * px);
            ctx.beginPath();
            ctx.fillStyle = item.color;
            ctx.arc(x + 0.5 * px, y + 0.45 * px, 0.42 * px, 0, Math.PI * 2);
            ctx.fill();
            continue;
        }

        ctx.fillStyle = item.color;
        roundRect(ctx, x, y, item.width * px, item.height * px, px * 0.16, true, false);
    }
}

function drawBuildings(ctx, buildings, selectedId, state, px) {
    for (const building of buildings) {
        const isSelected = building.id === selectedId;
        const x = building.x * px - state.renderCameraX;
        const y = building.y * px - state.renderCameraY;
        const width = building.width * px;
        const height = building.height * px;

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

        if (isSelected) {
            ctx.strokeStyle = "#ffe07f";
            ctx.lineWidth = 3;
            ctx.strokeRect(x - 2, y - 2, width + 4, height + 4);
        }
    }
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
    const building = map?.buildings?.find((item) =>
        player.tileX >= item.x && player.tileX < item.x + item.width &&
        player.tileY >= item.y && player.tileY < item.y + item.height);

    void invokeDotNet(
        state,
        "OnLocalMoved",
        player.tileX,
        player.tileY,
        facingFromDirection(state.lastDirectionKey),
        building ? building.id : (map?.parentBuildingId ?? null));
}

function facingFromDirection(directionKey) {
    if (directionKey === "up") return 3;
    if (directionKey === "left") return 1;
    if (directionKey === "right") return 2;
    return 0;
}

function drawCharacters(ctx, state, px) {
    const player = ensureLocalPlayerPosition(state, state.currentMapId);

    // Live members first, then any scene-authored characters standing on this
    // map. Sorted by Y so someone further down the screen correctly overlaps
    // someone standing behind them.
    const drawables = [];

    for (const remote of state.remotes.values()) {
        drawables.push({
            y: remote.renderY,
            draw: () => drawCharacter(ctx, state, px, remote.renderX, remote.renderY, remote, false)
        });
    }

    for (const character of state.scene.characters) {
        if (character.isLocalPlayer || character.mapId !== state.currentMapId) {
            continue;
        }

        drawables.push({
            y: character.y,
            draw: () => drawCharacter(ctx, state, px, character.x, character.y, character, false)
        });
    }

    if (player) {
        drawables.push({
            y: player.renderY,
            draw: () => drawCharacter(ctx, state, px, player.renderX, player.renderY, state.localAppearance, true)
        });
    }

    drawables.sort((a, b) => a.y - b.y);
    for (const item of drawables) {
        item.draw();
    }
}

/**
 * Characters are the member's own avatar drawn as a circular token, rather than
 * an authored sprite. The accent ring keeps players distinguishable while an
 * avatar is still loading, and stands in for it entirely if the image fails.
 */
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
        if (decoration.blocksMovement) {
            addRect(decoration);
        }
    }

    for (const building of map.buildings ?? []) {
        for (const rect of building.collisionRects ?? []) {
            addRect(rect);
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

    const portal = (map.portals ?? []).find((item) => item.x === player.tileX && item.y === player.tileY);
    if (!portal?.targetMapId) {
        return;
    }

    const targetMap = state.scene.maps.find((item) => item.id === portal.targetMapId);
    if (!targetMap) {
        return;
    }

    const targetPlayer = ensureLocalPlayerPosition(state, targetMap.id);
    if (targetPlayer) {
        teleportPlayer(
            targetPlayer,
            portal.targetX ?? targetMap.spawnTile?.x ?? targetPlayer.tileX,
            portal.targetY ?? targetMap.spawnTile?.y ?? targetPlayer.tileY);
    }

    state.currentMapId = targetMap.id;
    state.selectedBuildingId = portal.buildingId ?? targetMap.parentBuildingId ?? null;
    state.moveAccumulatorMs = 0;
    resizeCanvas(state);
    updateCamera(state);
    await invokeDotNet(state, "OnMapChanged", targetMap.id);
    await invokeDotNet(state, "OnBuildingSelected", state.selectedBuildingId, targetMap.id);
}

function getBuildingEntrance(building) {
    return building.entranceTile ?? {
        x: building.x + Math.floor(building.width / 2),
        y: building.y + building.height - 1
    };
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
        await state.dotNetRef.invokeMethodAsync(methodName, ...args);
    } catch {
        state.dotNetRef = null;
    }
}

function normalizeMovementKey(key) {
    const lowered = key.toLowerCase();
    if (lowered === "w" || lowered === "arrowup") return "up";
    if (lowered === "s" || lowered === "arrowdown") return "down";
    if (lowered === "a" || lowered === "arrowleft") return "left";
    if (lowered === "d" || lowered === "arrowright") return "right";
    return null;
}

function directionToVector(directionKey) {
    if (directionKey === "up") return { x: 0, y: -1 };
    if (directionKey === "down") return { x: 0, y: 1 };
    if (directionKey === "left") return { x: -1, y: 0 };
    if (directionKey === "right") return { x: 1, y: 0 };
    return null;
}

function getActiveDirectionKey(state) {
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
