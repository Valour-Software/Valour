const renderingModulePath = "../../../ts/VillageTileRendering.js";
const importModule = new Function("path", "return import(path)");
let renderingModulePromise = null;
function loadRenderingModule() {
    renderingModulePromise ??= importModule(renderingModulePath);
    return renderingModulePromise;
}
export async function init(canvasId, dotNetRef, config) {
    const rendering = await loadRenderingModule();
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        throw new Error(`Map editor canvas '${canvasId}' was not found.`);
    }
    const ctx = canvas.getContext("2d");
    if (!ctx) {
        throw new Error("Map editor canvas 2D context could not be created.");
    }
    const definitions = rendering.normalizeTileDefinitions(config.definitions);
    const brushes = rendering.normalizeBrushDefinitions(config.brushes);
    const state = {
        canvas,
        ctx,
        dotNetRef,
        imageUrl: config.image,
        tileSize: Math.max(1, Number(config.tileSize) || 16),
        mapWidth: rendering.clamp(Math.round(Number(config.mapWidth) || 32), 1, 240),
        mapHeight: rendering.clamp(Math.round(Number(config.mapHeight) || 20), 1, 240),
        tiles: [],
        tileBrushStrengths: [],
        tileBrushWeights: [],
        tileBrushKeys: [],
        tileBrushStrokeIds: [],
        sprites: [],
        nextSpriteId: 1,
        nextBrushStrokeId: 1,
        currentBrushStrokeId: 0,
        definitions,
        definitionsByKey: rendering.createDefinitionMap(definitions),
        brushes,
        brushesByKey: new Map(brushes.map(brush => [brush.key, brush])),
        selectedTileKey: config.selectedTileKey || definitions.find(definition => definition.kind.toLowerCase() !== "sprite")?.key || "",
        selectedBrushKey: config.selectedBrushKey || brushes[0]?.key || "",
        selectedSpriteKey: config.selectedSpriteKey || definitions.find(definition => definition.kind.toLowerCase() === "sprite")?.key || "",
        tileRadius: rendering.clamp(Math.round(Number(config.tileRadius) || 0), 0, 24),
        tool: config.tool || "Tile",
        rendering,
        textureCache: rendering.createTextureCache(),
        scale: 2,
        offsetX: 24,
        offsetY: 24,
        viewportWidth: 1,
        viewportHeight: 1,
        devicePixelRatio: 1,
        dragging: false,
        panning: false,
        dragStart: null,
        hoverTile: null,
        destroyed: false,
        onResize: () => undefined,
        onMouseDown: () => undefined,
        onMouseMove: () => undefined,
        onMouseUp: () => undefined,
        onWheel: () => undefined,
        onKeyDown: () => undefined
    };
    state.tiles = new Array(state.mapWidth * state.mapHeight).fill("");
    state.tileBrushStrengths = new Array(state.tiles.length).fill(0);
    state.tileBrushWeights = new Array(state.tiles.length).fill(0);
    state.tileBrushKeys = new Array(state.tiles.length).fill("");
    state.tileBrushStrokeIds = new Array(state.tiles.length).fill(0);
    const runtime = {
        setTool(tool) {
            state.tool = tool || "Tile";
            draw(state);
        },
        setSelectedTile(key) {
            state.selectedTileKey = key || "";
            draw(state);
        },
        setSelectedBrush(key) {
            state.selectedBrushKey = key || "";
            draw(state);
        },
        setSelectedSprite(key) {
            state.selectedSpriteKey = key || "";
            draw(state);
        },
        setTileRadius(radius) {
            state.tileRadius = state.rendering.clamp(Math.round(Number(radius) || 0), 0, 24);
            draw(state);
        },
        resizeMap(width, height) {
            resizeMap(state, width, height);
            draw(state);
            notifyMapChanged(state);
        },
        clearMap() {
            state.tiles.fill("");
            state.tileBrushStrengths.fill(0);
            state.tileBrushWeights.fill(0);
            state.tileBrushKeys.fill("");
            state.tileBrushStrokeIds.fill(0);
            state.sprites = [];
            draw(state);
            notifyMapChanged(state);
        },
        fit() {
            fitMap(state);
            draw(state);
        },
        dispose() {
            state.destroyed = true;
            window.removeEventListener("resize", state.onResize);
            window.removeEventListener("keydown", state.onKeyDown);
            canvas.removeEventListener("mousedown", state.onMouseDown);
            canvas.removeEventListener("mousemove", state.onMouseMove);
            canvas.removeEventListener("mouseup", state.onMouseUp);
            canvas.removeEventListener("mouseleave", state.onMouseUp);
            canvas.removeEventListener("wheel", state.onWheel);
        }
    };
    state.onResize = () => {
        resizeCanvas(state);
        draw(state);
    };
    state.onMouseDown = (event) => {
        if (event.button === 1 || event.altKey || event.metaKey) {
            state.panning = true;
            state.dragStart = {
                x: event.clientX,
                y: event.clientY,
                offsetX: state.offsetX,
                offsetY: state.offsetY
            };
            return;
        }
        if (event.button !== 0) {
            return;
        }
        const point = getMapPoint(state, event);
        if (!point) {
            return;
        }
        state.dragging = true;
        state.currentBrushStrokeId = state.nextBrushStrokeId++;
        applyToolAt(state, point.x, point.y);
        draw(state);
        notifyMapChanged(state);
    };
    state.onMouseMove = (event) => {
        if (state.panning && state.dragStart) {
            state.offsetX = state.dragStart.offsetX + event.clientX - state.dragStart.x;
            state.offsetY = state.dragStart.offsetY + event.clientY - state.dragStart.y;
            draw(state);
            return;
        }
        const point = getMapPoint(state, event);
        state.hoverTile = point;
        if (state.dragging && point) {
            applyToolAt(state, point.x, point.y);
            notifyMapChanged(state);
        }
        draw(state);
    };
    state.onMouseUp = () => {
        state.dragging = false;
        state.panning = false;
        state.dragStart = null;
        state.currentBrushStrokeId = 0;
    };
    state.onWheel = (event) => {
        event.preventDefault();
        const before = getWorldPoint(state, event);
        const factor = Math.exp(-event.deltaY * 0.00024);
        state.scale = rendering.clamp(state.scale * factor, 0.75, 8);
        const rect = state.canvas.getBoundingClientRect();
        state.offsetX = event.clientX - rect.left - before.x * state.tileSize * state.scale;
        state.offsetY = event.clientY - rect.top - before.y * state.tileSize * state.scale;
        draw(state);
    };
    state.onKeyDown = (event) => {
        if (rendering.isTextInput(event.target)) {
            return;
        }
        const key = event.key.toLowerCase();
        const step = 32;
        if (key === "w" || key === "arrowup")
            state.offsetY += step;
        else if (key === "s" || key === "arrowdown")
            state.offsetY -= step;
        else if (key === "a" || key === "arrowleft")
            state.offsetX += step;
        else if (key === "d" || key === "arrowright")
            state.offsetX -= step;
        else
            return;
        event.preventDefault();
        draw(state);
    };
    window.addEventListener("resize", state.onResize);
    window.addEventListener("keydown", state.onKeyDown);
    canvas.addEventListener("mousedown", state.onMouseDown);
    canvas.addEventListener("mousemove", state.onMouseMove);
    canvas.addEventListener("mouseup", state.onMouseUp);
    canvas.addEventListener("mouseleave", state.onMouseUp);
    canvas.addEventListener("wheel", state.onWheel, { passive: false });
    state.rendering.loadTexture(state.textureCache, state.imageUrl, () => {
        if (state.destroyed) {
            return;
        }
        const texture = state.rendering.loadTexture(state.textureCache, state.imageUrl);
        if (texture?.loaded) {
            state.dotNetRef
                .invokeMethodAsync("OnTilesetImageLoaded", texture.image.width, texture.image.height)
                .catch(error => console.warn("Failed to report tileset size.", error));
        }
        else {
            // Without this the palette silently renders blank swatches and the
            // canvas stays empty, with nothing explaining why.
            state.dotNetRef
                .invokeMethodAsync("OnTilesetImageFailed", state.imageUrl)
                .catch(error => console.warn("Failed to report tileset load failure.", error));
        }
        draw(state);
    });
    resizeCanvas(state);
    fitMap(state);
    draw(state);
    notifyMapChanged(state);
    return runtime;
}
function resizeCanvas(state) {
    const rect = state.canvas.getBoundingClientRect();
    state.viewportWidth = Math.max(1, Math.floor(rect.width));
    state.viewportHeight = Math.max(1, Math.floor(rect.height));
    state.devicePixelRatio = Math.max(1, window.devicePixelRatio || 1);
    state.canvas.width = Math.floor(state.viewportWidth * state.devicePixelRatio);
    state.canvas.height = Math.floor(state.viewportHeight * state.devicePixelRatio);
    state.ctx.setTransform(state.devicePixelRatio, 0, 0, state.devicePixelRatio, 0, 0);
    state.ctx.imageSmoothingEnabled = false;
}
function fitMap(state) {
    const fitX = (state.viewportWidth - 48) / Math.max(1, state.mapWidth * state.tileSize);
    const fitY = (state.viewportHeight - 48) / Math.max(1, state.mapHeight * state.tileSize);
    state.scale = state.rendering.clamp(Math.floor(Math.min(fitX, fitY) * 4) / 4, 1, 6);
    const cellSize = getCellSize(state);
    state.offsetX = Math.round((state.viewportWidth - state.mapWidth * cellSize) / 2);
    state.offsetY = Math.round((state.viewportHeight - state.mapHeight * cellSize) / 2);
}
function resizeMap(state, width, height) {
    const nextWidth = state.rendering.clamp(Math.round(Number(width) || state.mapWidth), 1, 240);
    const nextHeight = state.rendering.clamp(Math.round(Number(height) || state.mapHeight), 1, 240);
    const nextTiles = new Array(nextWidth * nextHeight).fill("");
    const nextBrushStrengths = new Array(nextWidth * nextHeight).fill(0);
    const nextBrushWeights = new Array(nextWidth * nextHeight).fill(0);
    const nextBrushKeys = new Array(nextWidth * nextHeight).fill("");
    const nextBrushStrokeIds = new Array(nextWidth * nextHeight).fill(0);
    for (let y = 0; y < Math.min(state.mapHeight, nextHeight); y++) {
        for (let x = 0; x < Math.min(state.mapWidth, nextWidth); x++) {
            const previousIndex = y * state.mapWidth + x;
            const nextIndex = y * nextWidth + x;
            nextTiles[nextIndex] = state.tiles[previousIndex];
            nextBrushStrengths[nextIndex] = state.tileBrushStrengths[previousIndex] || 0;
            nextBrushWeights[nextIndex] = state.tileBrushWeights[previousIndex] || 0;
            nextBrushKeys[nextIndex] = state.tileBrushKeys[previousIndex] || "";
            nextBrushStrokeIds[nextIndex] = state.tileBrushStrokeIds[previousIndex] || 0;
        }
    }
    state.mapWidth = nextWidth;
    state.mapHeight = nextHeight;
    state.tiles = nextTiles;
    state.tileBrushStrengths = nextBrushStrengths;
    state.tileBrushWeights = nextBrushWeights;
    state.tileBrushKeys = nextBrushKeys;
    state.tileBrushStrokeIds = nextBrushStrokeIds;
    state.sprites = state.sprites.filter(sprite => sprite.x < nextWidth && sprite.y < nextHeight);
    fitMap(state);
}
function applyToolAt(state, x, y) {
    if (!isInsideMap(state, x, y)) {
        return;
    }
    if (state.tool === "Erase") {
        eraseAt(state, x, y);
        return;
    }
    if (state.tool === "Brush") {
        paintBrushAt(state, x, y);
        return;
    }
    if (state.tool === "Sprite") {
        placeSpriteAt(state, x, y);
        return;
    }
    paintTileRadiusAt(state, x, y, state.selectedTileKey);
}
function paintTileRadiusAt(state, x, y, tileKey) {
    forEachTileInRadius(state, x, y, state.tileRadius, (tileX, tileY) => {
        paintTileAt(state, tileX, tileY, tileKey);
    });
}
function paintTileAt(state, x, y, tileKey) {
    if (!tileKey || !isInsideMap(state, x, y)) {
        return;
    }
    const definition = state.definitionsByKey.get(tileKey);
    if (!definition || definition.kind.toLowerCase() === "sprite") {
        return;
    }
    const index = y * state.mapWidth + x;
    state.tiles[index] = tileKey;
    state.tileBrushStrengths[index] = 0;
    state.tileBrushWeights[index] = 0;
    state.tileBrushKeys[index] = "";
    state.tileBrushStrokeIds[index] = 0;
}
function forEachTileInRadius(state, centerX, centerY, radius, callback) {
    const normalizedRadius = state.rendering.clamp(Math.round(Number(radius) || 0), 0, 24);
    for (let offsetY = -normalizedRadius; offsetY <= normalizedRadius; offsetY++) {
        for (let offsetX = -normalizedRadius; offsetX <= normalizedRadius; offsetX++) {
            if (Math.hypot(offsetX, offsetY) > normalizedRadius + 0.000001) {
                continue;
            }
            const x = centerX + offsetX;
            const y = centerY + offsetY;
            if (isInsideMap(state, x, y)) {
                callback(x, y);
            }
        }
    }
}
function paintBrushAt(state, x, y) {
    const brush = state.brushesByKey.get(state.selectedBrushKey);
    if (!brush) {
        paintTileAt(state, x, y, state.selectedTileKey);
        return;
    }
    const originX = x - Math.floor(brush.size / 2);
    const originY = y - Math.floor(brush.size / 2);
    for (let cellY = 0; cellY < brush.size; cellY++) {
        for (let cellX = 0; cellX < brush.size; cellX++) {
            const cell = brush.cells[cellY * brush.size + cellX];
            if (!cell?.tileKey) {
                continue;
            }
            paintBrushCellAt(state, originX + cellX, originY + cellY, brush.key, cell.tileKey, getBrushCellPriority(cellX, cellY, brush.size, cell.strength), cell.weight);
        }
    }
}
function getBrushCellPriority(cellX, cellY, brushSize, strength) {
    const center = (Math.max(1, brushSize) - 1) / 2;
    const maxDistance = Math.max(1, Math.hypot(center, center));
    const distance = Math.hypot(cellX - center, cellY - center);
    const distanceFalloff = 1 - Math.min(1, distance / maxDistance);
    const baseStrength = Math.max(1, Number(strength) || 1);
    return baseStrength + distanceFalloff;
}
function paintBrushCellAt(state, x, y, brushKey, tileKey, priority, weight) {
    if (!tileKey || !isInsideMap(state, x, y)) {
        return;
    }
    const definition = state.definitionsByKey.get(tileKey);
    if (!definition || definition.kind.toLowerCase() === "sprite") {
        return;
    }
    const index = y * state.mapWidth + x;
    const brushPriority = Math.max(1, Number(priority) || 1);
    const brushWeight = Math.max(1, Number(weight) || 1);
    if (!state.currentBrushStrokeId) {
        state.currentBrushStrokeId = state.nextBrushStrokeId++;
    }
    const strokeId = state.currentBrushStrokeId;
    const isSameBrush = state.tileBrushKeys[index] === brushKey;
    const currentStrength = isSameBrush
        ? state.tileBrushStrengths[index] || 0
        : 0;
    const currentWeight = isSameBrush
        ? state.tileBrushWeights[index] || 0
        : 0;
    if (isSameBrush && isLowerBrushPriority(brushPriority, brushWeight, currentStrength, currentWeight)) {
        return;
    }
    state.tiles[index] = tileKey;
    state.tileBrushStrengths[index] = brushPriority;
    state.tileBrushWeights[index] = brushWeight;
    state.tileBrushKeys[index] = brushKey;
    state.tileBrushStrokeIds[index] = strokeId;
}
function isLowerBrushPriority(priority, weight, currentPriority, currentWeight) {
    const epsilon = 0.000001;
    const priorityDelta = priority - currentPriority;
    return priorityDelta < -epsilon || (Math.abs(priorityDelta) <= epsilon && weight < currentWeight);
}
function placeSpriteAt(state, x, y) {
    const definition = state.definitionsByKey.get(state.selectedSpriteKey);
    if (!definition || definition.kind.toLowerCase() !== "sprite") {
        return;
    }
    state.sprites = state.sprites.filter(sprite => !(sprite.x === x && sprite.y === y));
    state.sprites.push({
        id: state.nextSpriteId++,
        key: definition.key,
        x,
        y,
        width: definition.width,
        height: definition.height
    });
}
function eraseAt(state, x, y) {
    const spriteIndex = findTopSpriteIndexAt(state, x, y);
    if (spriteIndex >= 0) {
        state.sprites.splice(spriteIndex, 1);
        return;
    }
    const index = y * state.mapWidth + x;
    state.tiles[index] = "";
    state.tileBrushStrengths[index] = 0;
    state.tileBrushWeights[index] = 0;
    state.tileBrushKeys[index] = "";
    state.tileBrushStrokeIds[index] = 0;
}
function findTopSpriteIndexAt(state, x, y) {
    for (let index = state.sprites.length - 1; index >= 0; index--) {
        const sprite = state.sprites[index];
        if (x >= sprite.x && x < sprite.x + sprite.width && y >= sprite.y && y < sprite.y + sprite.height) {
            return index;
        }
    }
    return -1;
}
function draw(state) {
    const { ctx } = state;
    ctx.clearRect(0, 0, state.viewportWidth, state.viewportHeight);
    ctx.fillStyle = "#12151d";
    ctx.fillRect(0, 0, state.viewportWidth, state.viewportHeight);
    const cellSize = getCellSize(state);
    const mapWidthPx = state.mapWidth * cellSize;
    const mapHeightPx = state.mapHeight * cellSize;
    ctx.save();
    ctx.translate(Math.round(state.offsetX), Math.round(state.offsetY));
    ctx.fillStyle = "#181d27";
    ctx.fillRect(0, 0, mapWidthPx, mapHeightPx);
    const texture = state.rendering.loadTexture(state.textureCache, state.imageUrl, () => draw(state));
    if (texture?.loaded) {
        drawTileLayer(state, texture.image, cellSize);
        drawSprites(state, texture.image, cellSize);
    }
    drawGrid(state, cellSize);
    drawHover(state, cellSize);
    ctx.restore();
}
function drawTileLayer(state, image, cellSize) {
    const { ctx } = state;
    for (let y = 0; y < state.mapHeight; y++) {
        for (let x = 0; x < state.mapWidth; x++) {
            const tileKey = state.tiles[y * state.mapWidth + x];
            if (!tileKey) {
                continue;
            }
            const definition = state.definitionsByKey.get(tileKey);
            if (!definition) {
                continue;
            }
            state.rendering.drawTilesetDefinition(ctx, image, definition, state.tileSize, x * cellSize, y * cellSize, cellSize);
        }
    }
}
function drawSprites(state, image, cellSize) {
    const { ctx } = state;
    const orderedSprites = [...state.sprites].sort((a, b) => a.y === b.y ? a.x - b.x : a.y - b.y);
    for (const sprite of orderedSprites) {
        const definition = state.definitionsByKey.get(sprite.key);
        if (!definition) {
            continue;
        }
        state.rendering.drawTilesetDefinition(ctx, image, definition, state.tileSize, sprite.x * cellSize, sprite.y * cellSize, cellSize);
    }
}
function drawGrid(state, cellSize) {
    const { ctx } = state;
    const width = state.mapWidth * cellSize;
    const height = state.mapHeight * cellSize;
    ctx.save();
    ctx.strokeStyle = "rgba(255,255,255,0.16)";
    ctx.lineWidth = 1;
    for (let x = 0; x <= state.mapWidth; x++) {
        const screenX = Math.round(x * cellSize) + 0.5;
        ctx.beginPath();
        ctx.moveTo(screenX, 0);
        ctx.lineTo(screenX, height);
        ctx.stroke();
    }
    for (let y = 0; y <= state.mapHeight; y++) {
        const screenY = Math.round(y * cellSize) + 0.5;
        ctx.beginPath();
        ctx.moveTo(0, screenY);
        ctx.lineTo(width, screenY);
        ctx.stroke();
    }
    ctx.strokeStyle = "rgba(255,255,255,0.4)";
    ctx.lineWidth = 2;
    ctx.strokeRect(1, 1, width - 2, height - 2);
    ctx.restore();
}
function drawHover(state, cellSize) {
    if (!state.hoverTile) {
        return;
    }
    const { x, y } = state.hoverTile;
    if (!isInsideMap(state, x, y)) {
        return;
    }
    const { ctx } = state;
    ctx.save();
    ctx.strokeStyle = state.tool === "Erase" ? "#ff7474" : "#ffd35b";
    ctx.lineWidth = 2;
    let width = 1;
    let height = 1;
    let brush;
    let definition;
    if (state.tool === "Brush") {
        brush = state.brushesByKey.get(state.selectedBrushKey);
        width = brush?.size ?? 1;
        height = brush?.size ?? 1;
    }
    else if (state.tool === "Sprite") {
        definition = state.definitionsByKey.get(state.selectedSpriteKey);
        width = definition?.width ?? 1;
        height = definition?.height ?? 1;
    }
    else if (state.tool === "Tile") {
        definition = state.definitionsByKey.get(state.selectedTileKey);
    }
    const originX = state.tool === "Brush" ? x - Math.floor(width / 2) : x;
    const originY = state.tool === "Brush" ? y - Math.floor(height / 2) : y;
    const texture = state.rendering.loadTexture(state.textureCache, state.imageUrl, () => draw(state));
    if (state.tool === "Tile") {
        drawTileRadiusGhost(state, texture?.loaded ? texture.image : null, definition, x, y, cellSize);
    }
    else if (state.tool === "Erase" || !texture?.loaded) {
        ctx.fillStyle = state.tool === "Erase" ? "rgba(255, 100, 100, 0.22)" : "rgba(255, 211, 91, 0.16)";
        ctx.fillRect(originX * cellSize, originY * cellSize, width * cellSize, height * cellSize);
    }
    else if (state.tool === "Brush" && brush) {
        drawBrushGhost(state, texture.image, brush, originX, originY, cellSize);
    }
    else if ((state.tool === "Sprite" || state.tool === "Tile") && definition) {
        drawDefinitionGhost(state, texture.image, definition, originX, originY, cellSize, 0.58);
    }
    if (state.tool !== "Tile") {
        ctx.strokeRect(originX * cellSize + 1, originY * cellSize + 1, width * cellSize - 2, height * cellSize - 2);
    }
    ctx.restore();
}
function drawTileRadiusGhost(state, image, definition, centerX, centerY, cellSize) {
    const { ctx } = state;
    ctx.fillStyle = "rgba(255, 211, 91, 0.16)";
    ctx.strokeStyle = "#ffd35b";
    forEachTileInRadius(state, centerX, centerY, state.tileRadius, (x, y) => {
        if (image && definition) {
            drawDefinitionGhost(state, image, definition, x, y, cellSize, 0.58);
        }
        else {
            ctx.fillRect(x * cellSize, y * cellSize, cellSize, cellSize);
        }
        ctx.strokeRect(x * cellSize + 1, y * cellSize + 1, cellSize - 2, cellSize - 2);
    });
}
function drawBrushGhost(state, image, brush, originX, originY, cellSize) {
    for (let cellY = 0; cellY < brush.size; cellY++) {
        for (let cellX = 0; cellX < brush.size; cellX++) {
            const cell = brush.cells[cellY * brush.size + cellX];
            const x = originX + cellX;
            const y = originY + cellY;
            if (!cell?.tileKey || !isInsideMap(state, x, y)) {
                continue;
            }
            const definition = state.definitionsByKey.get(cell.tileKey);
            if (!definition) {
                continue;
            }
            const priority = getBrushCellPriority(cellX, cellY, brush.size, cell.strength);
            const alpha = state.rendering.clamp(0.24 + priority * 0.12, 0.34, 0.72);
            drawDefinitionGhost(state, image, definition, x, y, cellSize, alpha);
        }
    }
}
function drawDefinitionGhost(state, image, definition, x, y, cellSize, alpha) {
    const { ctx } = state;
    ctx.save();
    ctx.globalAlpha = alpha;
    state.rendering.drawTilesetDefinition(ctx, image, definition, state.tileSize, x * cellSize, y * cellSize, cellSize);
    ctx.restore();
}
function getMapPoint(state, event) {
    const world = getWorldPoint(state, event);
    const x = Math.floor(world.x);
    const y = Math.floor(world.y);
    if (!isInsideMap(state, x, y)) {
        return null;
    }
    return { x, y };
}
function getWorldPoint(state, event) {
    const rect = state.canvas.getBoundingClientRect();
    const cellSize = getCellSize(state);
    return {
        x: (event.clientX - rect.left - state.offsetX) / cellSize,
        y: (event.clientY - rect.top - state.offsetY) / cellSize
    };
}
function getCellSize(state) {
    return state.tileSize * state.scale;
}
function isInsideMap(state, x, y) {
    return x >= 0 && y >= 0 && x < state.mapWidth && y < state.mapHeight;
}
function notifyMapChanged(state) {
    const exportJson = JSON.stringify(exportMap(state), null, 2);
    state.dotNetRef
        .invokeMethodAsync("OnMapChanged", exportJson, state.mapWidth, state.mapHeight, countPaintedTiles(state), state.sprites.length)
        .catch(error => console.warn("Failed to report map editor changes.", error));
}
function exportMap(state) {
    return {
        width: state.mapWidth,
        height: state.mapHeight,
        tileSize: state.tileSize,
        tileset: state.imageUrl,
        tiles: state.tiles.map(tileKey => tileKey || null),
        sprites: state.sprites.map(sprite => ({
            key: sprite.key,
            x: sprite.x,
            y: sprite.y,
            width: sprite.width,
            height: sprite.height
        }))
    };
}
function countPaintedTiles(state) {
    return state.tiles.reduce((count, tileKey) => tileKey ? count + 1 : count, 0);
}
//# sourceMappingURL=VillageMapEditorWindowComponent.razor.js.map