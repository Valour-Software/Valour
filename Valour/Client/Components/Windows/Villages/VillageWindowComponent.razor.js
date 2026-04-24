export function init(canvasId, dotNetRef, scene) {
    const canvas = document.getElementById(canvasId);
    const ctx = canvas.getContext("2d");
    ctx.imageSmoothingEnabled = false;
    const localAppearance = scene.characters.find((character) => character.isLocalPlayer) ?? {
        name: "You",
        bodyColor: "#f4d1b5",
        hairColor: "#5a3825",
        topColor: "#4780d9",
        bottomColor: "#385068"
    };

    const state = {
        canvas,
        ctx,
        dotNetRef,
        scene,
        localAppearance,
        currentMapId: scene.startingMapId,
        selectedBuildingId: null,
        tileSize: 32,
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
            await dotNetRef.invokeMethodAsync("OnMapChanged", mapId);
            await dotNetRef.invokeMethodAsync("OnBuildingSelected", null, mapId);
            draw(state);
        },
        dispose() {
            state.destroyed = true;
            window.removeEventListener("resize", state.onResize);
            window.removeEventListener("keydown", state.onKeyDown);
            window.removeEventListener("keyup", state.onKeyUp);
            canvas.removeEventListener("click", state.onClick);
            if (state.animationFrame) {
                cancelAnimationFrame(state.animationFrame);
            }
        }
    };

    state.onResize = () => {
        resizeCanvas(state);
        updateCamera(state);
        draw(state);
    };

    state.onKeyDown = (event) => {
        const normalizedKey = normalizeMovementKey(event.key);
        if (!normalizedKey) {
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
        const px = state.tileSize * state.currentScale;
        const worldX = event.clientX - rect.left + state.cameraX;
        const worldY = event.clientY - rect.top + state.cameraY;
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

    window.addEventListener("resize", state.onResize);
    window.addEventListener("keydown", state.onKeyDown);
    window.addEventListener("keyup", state.onKeyUp);
    canvas.addEventListener("click", state.onClick);

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

function getCurrentMap(state) {
    return state.scene.maps.find((map) => map.id === state.currentMapId) ?? null;
}

function resizeCanvas(state) {
    const rect = state.canvas.getBoundingClientRect();
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

    if (!isWalkableTile(map, nextX, nextY)) {
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
        return;
    }

    const px = state.tileSize * state.currentScale;
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

    if (state.canvas.width === 0 || state.canvas.height === 0) {
        resizeCanvas(state);
    }

    const { ctx } = state;
    const px = state.tileSize * state.currentScale;
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
}

function loadTexture(state, url) {
    if (!url) {
        return null;
    }

    if (state.textureCache.has(url)) {
        return state.textureCache.get(url);
    }

    const texture = {
        url,
        image: new Image(),
        loaded: false
    };

    texture.image.onload = () => {
        texture.loaded = true;
    };
    texture.image.src = url;
    state.textureCache.set(url, texture);
    return texture;
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

function drawCharacters(ctx, state, px) {
    const player = ensureLocalPlayerPosition(state, state.currentMapId);
    const remoteCharacters = state.scene.characters.filter((character) =>
        !character.isLocalPlayer && character.mapId === state.currentMapId);

    for (const character of remoteCharacters) {
        drawCharacter(ctx, state, px, character.x, character.y, character, false);
    }

    if (player) {
        drawCharacter(ctx, state, px, player.renderX, player.renderY, state.localAppearance, true);
    }
}

function drawCharacter(ctx, state, px, x, y, character, isLocalPlayer) {
    const centerX = (x + 0.5) * px - state.renderCameraX;
    const centerY = (y + 0.5) * px - state.renderCameraY;

    ctx.fillStyle = character.hairColor;
    ctx.beginPath();
    ctx.arc(centerX, centerY - 0.12 * px, 0.28 * px, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = character.bodyColor;
    ctx.beginPath();
    ctx.arc(centerX, centerY + 0.04 * px, 0.23 * px, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = character.topColor;
    roundRect(ctx, centerX - 0.26 * px, centerY + 0.28 * px, 0.52 * px, 0.42 * px, 0.12 * px, true, false);

    ctx.fillStyle = character.bottomColor;
    ctx.fillRect(centerX - 0.2 * px, centerY + 0.64 * px, 0.16 * px, 0.32 * px);
    ctx.fillRect(centerX + 0.04 * px, centerY + 0.64 * px, 0.16 * px, 0.32 * px);

    if (isLocalPlayer) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(centerX, centerY + 0.2 * px, 0.55 * px, 0, Math.PI * 2);
        ctx.stroke();
    }
}

function isWalkableTile(map, tileX, tileY) {
    if (tileX < 0 || tileY < 0 || tileX >= map.width || tileY >= map.height) {
        return false;
    }

    for (const blocked of map.blockedTiles ?? []) {
        if (rectContains(blocked, tileX, tileY)) {
            return false;
        }
    }

    return true;
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
    await state.dotNetRef.invokeMethodAsync("OnMapChanged", targetMap.id);
    await state.dotNetRef.invokeMethodAsync("OnBuildingSelected", state.selectedBuildingId, targetMap.id);
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

    await state.dotNetRef.invokeMethodAsync("OnBuildingSelected", state.selectedBuildingId, map.id);
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

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}
