export const COLLISION_STATE_EMPTY = "empty";
export const COLLISION_STATE_SOLID = "solid";
export const COLLISION_STATE_DOOR = "door";
export function normalizeCollisionState(value) {
    if (value === true || value === 1) {
        return COLLISION_STATE_SOLID;
    }
    if (value === false || value === 0 || value === null || value === undefined) {
        return COLLISION_STATE_EMPTY;
    }
    const normalized = String(value).trim().toLowerCase();
    switch (normalized) {
        case "":
        case "false":
        case "0":
        case "none":
            return COLLISION_STATE_EMPTY;
        case "true":
        case "1":
        case "blocked":
            return COLLISION_STATE_SOLID;
        default:
            return normalized;
    }
}
export function collisionStateBlocksMovement(value) {
    const state = normalizeCollisionState(value);
    return state !== COLLISION_STATE_EMPTY && state !== COLLISION_STATE_DOOR;
}
export function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}
export function isTextInput(element) {
    if (!(element instanceof HTMLElement)) {
        return false;
    }
    const tagName = element.tagName.toLowerCase();
    return tagName === "input" ||
        tagName === "textarea" ||
        tagName === "select" ||
        element.isContentEditable;
}
export function createTextureCache() {
    return new Map();
}
export function loadStandaloneImage(url) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.referrerPolicy = "no-referrer";
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error(`Unable to load image: ${url}`));
        image.src = url;
    });
}
export function loadTexture(cache, url, onLoaded) {
    if (!url) {
        return null;
    }
    const existing = cache.get(url);
    if (existing) {
        // A cached entry must still notify. Callers use onLoaded to report the
        // sheet's dimensions back to Blazor, and dropping it on a cache hit
        // leaves them stuck on their placeholder size.
        if (onLoaded) {
            if (existing.loaded || existing.failed) {
                queueMicrotask(onLoaded);
            }
            else {
                existing.pending.push(onLoaded);
            }
        }
        return existing;
    }
    const texture = {
        url,
        image: new Image(),
        loaded: false,
        failed: false,
        pending: onLoaded ? [onLoaded] : []
    };
    const settle = () => {
        const callbacks = texture.pending;
        texture.pending = [];
        for (const callback of callbacks) {
            callback();
        }
    };
    texture.image.referrerPolicy = "no-referrer";
    texture.image.onload = () => {
        texture.loaded = true;
        settle();
    };
    texture.image.onerror = () => {
        texture.failed = true;
        settle();
    };
    texture.image.src = url;
    cache.set(url, texture);
    return texture;
}
export function normalizeTileDefinitions(definitions) {
    if (!Array.isArray(definitions)) {
        return [];
    }
    return definitions
        .map(normalizeTileDefinition)
        .filter((definition) => definition !== null);
}
export function normalizeTileDefinition(definition) {
    if (!definition || typeof definition !== "object") {
        return null;
    }
    const source = definition;
    const key = stringValue(source.key ?? source.Key);
    if (!key) {
        return null;
    }
    const width = Math.max(1, numberValue(source.width ?? source.Width, 1));
    const height = Math.max(1, numberValue(source.height ?? source.Height, 1));
    const rawCollision = Array.isArray(source.collision)
        ? source.collision
        : Array.isArray(source.Collision)
            ? source.Collision
            : [];
    const collisionStates = rawCollision
        .slice(0, width * height)
        .map(normalizeCollisionState);
    while (collisionStates.length < width * height) {
        collisionStates.push(COLLISION_STATE_EMPTY);
    }
    const collision = collisionStates.map(collisionStateBlocksMovement);
    return {
        kind: stringValue(source.kind ?? source.Kind) || "Tile",
        name: stringValue(source.name ?? source.Name) || key,
        key,
        x: numberValue(source.x ?? source.X),
        y: numberValue(source.y ?? source.Y),
        width,
        height,
        collisionStates,
        collision,
        terrainKey: stringValue(source.terrainKey ?? source.TerrainKey),
        terrainRole: normalizeTerrainRole(stringValue(source.terrainRole ?? source.TerrainRole)),
        terrainDirection: normalizeTerrainDirection(stringValue(source.terrainDirection ?? source.TerrainDirection)),
        terrainAgainst: stringValue(source.terrainAgainst ?? source.TerrainAgainst),
        terrainWeight: Math.max(1, numberValue(source.terrainWeight ?? source.TerrainWeight, 1))
    };
}
export function getVillageRenderScale(viewportWidth, mapKind) {
    const mobile = viewportWidth < 760;
    return mapKind === "Interior"
        ? (mobile ? 2 : 3)
        : (mobile ? 1 : 2);
}
export function adjustVillageZoom(currentZoom, stepDelta) {
    const current = Number.isFinite(currentZoom) ? currentZoom : 1;
    const stepped = Math.round((current + stepDelta * 0.25) * 4) / 4;
    return clamp(stepped, 0.5, 2);
}
/**
 * Returns the camera offset that places the rendered centre of the local
 * character at the exact centre of the viewport. The vertical anchor matches
 * drawCharacter rather than the centre of the character's walkable tile.
 */
export function getPlayerCenteredCamera(playerX, playerY, tilePixels, viewportWidth, viewportHeight) {
    return {
        x: (playerX + 0.5) * tilePixels - viewportWidth / 2,
        y: (playerY + 0.35) * tilePixels - viewportHeight / 2
    };
}
/**
 * Village objects store the top-left of their walkable footprint, while tall
 * sprites include canopy/roof pixels above that footprint. Keeping this
 * conversion in one place prevents drawing, culling and collision from
 * disagreeing about where the same authored sprite lives.
 */
export function getBottomAnchoredSpriteBounds(tileX, tileY, footprintHeight, spriteWidth, spriteHeight) {
    return {
        x: tileX,
        y: tileY + Math.max(1, footprintHeight) - Math.max(1, spriteHeight),
        width: Math.max(1, spriteWidth),
        height: Math.max(1, spriteHeight)
    };
}
/**
 * Projects a row-major tileset collision mask into map coordinates using the
 * same bottom anchor as the renderer. Empty/transparent cells stay walkable.
 */
export function getBottomAnchoredCollisionCells(tileX, tileY, footprintHeight, definition) {
    const bounds = getBottomAnchoredSpriteBounds(tileX, tileY, footprintHeight, definition.width, definition.height);
    const cells = [];
    for (let index = 0; index < definition.width * definition.height; index++) {
        if (!definition.collision[index]) {
            continue;
        }
        cells.push({
            x: bounds.x + index % definition.width,
            y: bounds.y + Math.floor(index / definition.width)
        });
    }
    return cells;
}
/**
 * Projects cells carrying a semantic state (for example a door) into map
 * coordinates without collapsing them into the blocking boolean mask.
 */
export function getBottomAnchoredStateCells(tileX, tileY, footprintHeight, definition, requestedState) {
    const bounds = getBottomAnchoredSpriteBounds(tileX, tileY, footprintHeight, definition.width, definition.height);
    const normalizedState = normalizeCollisionState(requestedState);
    const cells = [];
    for (let index = 0; index < definition.width * definition.height; index++) {
        if (normalizeCollisionState(definition.collisionStates[index]) !== normalizedState) {
            continue;
        }
        cells.push({
            x: bounds.x + index % definition.width,
            y: bounds.y + Math.floor(index / definition.width)
        });
    }
    return cells;
}
export function normalizeBrushDefinitions(brushes) {
    if (!Array.isArray(brushes)) {
        return [];
    }
    return brushes
        .map(normalizeBrushDefinition)
        .filter((brush) => brush !== null);
}
export function normalizeBrushDefinition(brush) {
    if (!brush || typeof brush !== "object") {
        return null;
    }
    const source = brush;
    const key = stringValue(source.key ?? source.Key);
    if (!key) {
        return null;
    }
    const size = Math.max(1, numberValue(source.size ?? source.Size, 1));
    const rawCells = Array.isArray(source.cells) ? source.cells : Array.isArray(source.Cells) ? source.Cells : [];
    const cells = rawCells.map((cell) => ({
        tileKey: stringValue(cell?.tileKey ?? cell?.TileKey),
        strength: numberValue(cell?.strength ?? cell?.Strength, 1),
        weight: numberValue(cell?.weight ?? cell?.Weight, 1)
    }));
    while (cells.length < size * size) {
        cells.push({ tileKey: "", strength: 1, weight: 1 });
    }
    return {
        name: stringValue(source.name ?? source.Name) || key,
        key,
        size,
        cells: cells.slice(0, size * size)
    };
}
export function isSpriteDefinition(definition) {
    return definition.kind.toLowerCase() === "sprite";
}
export function createDefinitionMap(definitions) {
    return new Map(definitions.map(definition => [definition.key, definition]));
}
/**
 * Must stay byte-for-byte compatible with CallPanelComponent's element ids.
 * Peer ids may contain non-ASCII display/provider data, so UTF-8 bytes are
 * encoded rather than JavaScript UTF-16 code units.
 */
export function getCallAudioElementId(peerId) {
    const bytes = new TextEncoder().encode(peerId || "unknown");
    let suffix = "";
    for (const byte of bytes) {
        suffix += byte.toString(16).padStart(2, "0");
    }
    return `call-audio-${suffix}`;
}
export function drawTilesetDefinition(ctx, image, definition, sourceTileSize, destinationX, destinationY, destinationTileSize) {
    ctx.drawImage(image, definition.x * sourceTileSize, definition.y * sourceTileSize, definition.width * sourceTileSize, definition.height * sourceTileSize, destinationX, destinationY, definition.width * destinationTileSize, definition.height * destinationTileSize);
}
const TERRAIN_EDGE_DIRECTIONS = ["N", "E", "S", "W"];
const TERRAIN_CORNER_DIRECTIONS = ["NE", "SE", "SW", "NW"];
const TERRAIN_DIRECTION_ALIASES = {
    "n": "N", "north": "N", "up": "N",
    "e": "E", "east": "E", "right": "E",
    "s": "S", "south": "S", "down": "S",
    "w": "W", "west": "W", "left": "W",
    "ne": "NE", "northeast": "NE", "upright": "NE",
    "se": "SE", "southeast": "SE", "downright": "SE",
    "sw": "SW", "southwest": "SW", "downleft": "SW",
    "nw": "NW", "northwest": "NW", "upleft": "NW"
};
const TERRAIN_ROLE_ALIASES = {
    "base": "Base",
    "variant": "Base",
    "edge": "Edge",
    "corner": "Corner",
    "outercorner": "Corner",
    "inner": "InnerCorner",
    "innercorner": "InnerCorner"
};
export function normalizeTerrainDirection(value) {
    return TERRAIN_DIRECTION_ALIASES[value.trim().toLowerCase()] ?? "None";
}
export function normalizeTerrainRole(value) {
    return TERRAIN_ROLE_ALIASES[value.trim().toLowerCase()] ?? "Base";
}
export function normalizeTerrainDefinitions(terrains) {
    if (!Array.isArray(terrains)) {
        return [];
    }
    return terrains
        .map((terrain) => {
        const key = stringValue(terrain?.key ?? terrain?.Key);
        if (!key) {
            return null;
        }
        return {
            key,
            name: stringValue(terrain.name ?? terrain.Name) || key,
            priority: numberValue(terrain.priority ?? terrain.Priority, 0)
        };
    })
        .filter((terrain) => terrain !== null);
}
/**
 * Groups tile definitions into per-terrain rulesets. A tile participates by
 * carrying a terrainKey; terrains referenced only by tiles (absent from the
 * declared list) are created with default priority so a partially-authored
 * tileset still resolves.
 */
export function buildTerrainIndex(terrains, definitions) {
    const index = new Map();
    const ensureEntry = (key) => {
        let entry = index.get(key);
        if (!entry) {
            entry = {
                terrain: { key, name: key, priority: 0 },
                baseTiles: [],
                transitions: new Map()
            };
            index.set(key, entry);
        }
        return entry;
    };
    for (const terrain of terrains) {
        ensureEntry(terrain.key).terrain = terrain;
    }
    for (const definition of definitions) {
        if (!definition.terrainKey) {
            continue;
        }
        const entry = ensureEntry(definition.terrainKey);
        if (definition.terrainRole === "Base") {
            entry.baseTiles.push(definition);
            continue;
        }
        const direction = definition.terrainDirection;
        const isCornerRole = definition.terrainRole !== "Edge";
        const validDirections = isCornerRole ? TERRAIN_CORNER_DIRECTIONS : TERRAIN_EDGE_DIRECTIONS;
        if (!validDirections.includes(direction)) {
            continue;
        }
        let set = entry.transitions.get(definition.terrainAgainst);
        if (!set) {
            set = { edges: new Map(), corners: new Map(), inners: new Map() };
            entry.transitions.set(definition.terrainAgainst, set);
        }
        const bucket = definition.terrainRole === "Edge"
            ? set.edges
            : definition.terrainRole === "Corner"
                ? set.corners
                : set.inners;
        const existing = bucket.get(direction);
        if (existing) {
            existing.push(definition);
        }
        else {
            bucket.set(direction, [definition]);
        }
    }
    return index;
}
function getTransitionSet(entry, against) {
    return entry.transitions.get(against) ?? entry.transitions.get("") ?? null;
}
/**
 * Exactly one side of a terrain boundary draws transition art, or the seam
 * shows both materials fringing into each other. The side with authored art
 * wins outright; when both sides have art, a set authored for this specific
 * pair beats a wildcard, then higher priority wins, with the key as a
 * deterministic tiebreak.
 */
function blendsToward(index, entry, neighborKey) {
    if (!neighborKey || neighborKey === entry.terrain.key) {
        return false;
    }
    if (!getTransitionSet(entry, neighborKey)) {
        return false;
    }
    const neighbor = index.get(neighborKey);
    if (!neighbor || !getTransitionSet(neighbor, entry.terrain.key)) {
        return true;
    }
    const selfSpecific = entry.transitions.has(neighborKey);
    const neighborSpecific = neighbor.transitions.has(entry.terrain.key);
    if (selfSpecific !== neighborSpecific) {
        return selfSpecific;
    }
    if (neighbor.terrain.priority !== entry.terrain.priority) {
        return entry.terrain.priority > neighbor.terrain.priority;
    }
    return entry.terrain.key < neighbor.terrain.key;
}
/**
 * Deterministic per-cell hash so weighted variant picks are stable across
 * recomposites - a repaint must not reshuffle every grass tuft on the map.
 */
function hashCell(x, y) {
    let hash = (Math.imul(x, 374761393) + Math.imul(y, 668265263)) | 0;
    hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
    return (hash ^ (hash >>> 16)) >>> 0;
}
function pickWeighted(tiles, x, y) {
    if (!tiles || tiles.length === 0) {
        return null;
    }
    if (tiles.length === 1) {
        return tiles[0];
    }
    const totalWeight = tiles.reduce((sum, tile) => sum + tile.terrainWeight, 0);
    let remaining = hashCell(x, y) % totalWeight;
    for (const tile of tiles) {
        remaining -= tile.terrainWeight;
        if (remaining < 0) {
            return tile;
        }
    }
    return tiles[tiles.length - 1];
}
const TERRAIN_NEIGHBOR_OFFSETS = [
    { flag: "n", dx: 0, dy: -1, cardinal: true },
    { flag: "e", dx: 1, dy: 0, cardinal: true },
    { flag: "s", dx: 0, dy: 1, cardinal: true },
    { flag: "w", dx: -1, dy: 0, cardinal: true },
    { flag: "ne", dx: 1, dy: -1, cardinal: false },
    { flag: "se", dx: 1, dy: 1, cardinal: false },
    { flag: "sw", dx: -1, dy: 1, cardinal: false },
    { flag: "nw", dx: -1, dy: -1, cardinal: false }
];
function getTerrainNeighborhood(grid, width, height, x, y, index, entry) {
    const neighborhood = {
        n: false, e: false, s: false, w: false,
        ne: false, se: false, sw: false, nw: false,
        dominantAgainst: ""
    };
    // Out-of-bounds and unpainted cells are inert rather than foreign: a map
    // edge or a hand-placed tile next to terrain must not sprout fringe art.
    const againstVotes = new Map();
    for (const offset of TERRAIN_NEIGHBOR_OFFSETS) {
        const nx = x + offset.dx;
        const ny = y + offset.dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) {
            continue;
        }
        const neighborKey = grid[ny * width + nx] || "";
        if (!blendsToward(index, entry, neighborKey)) {
            continue;
        }
        neighborhood[offset.flag] = true;
        againstVotes.set(neighborKey, (againstVotes.get(neighborKey) ?? 0) + (offset.cardinal ? 2 : 1));
    }
    let bestVotes = 0;
    for (const [key, votes] of againstVotes) {
        if (votes > bestVotes || (votes === bestVotes && key < neighborhood.dominantAgainst)) {
            bestVotes = votes;
            neighborhood.dominantAgainst = key;
        }
    }
    return neighborhood;
}
/**
 * Picks the tile for one terrain cell from its 8-neighbor foreign mask using
 * the minimal blob set (edges, outer corners, inner corners). Every miss
 * steps down a ladder toward the base tile, so a ruleset without a given
 * piece renders a hard seam instead of a hole.
 */
export function resolveTerrainCell(grid, width, height, x, y, index) {
    if (x < 0 || y < 0 || x >= width || y >= height) {
        return null;
    }
    const terrainKey = grid[y * width + x] || "";
    if (!terrainKey) {
        return null;
    }
    const entry = index.get(terrainKey);
    if (!entry) {
        return null;
    }
    const base = pickWeighted(entry.baseTiles, x, y);
    const hood = getTerrainNeighborhood(grid, width, height, x, y, index, entry);
    if (!hood.dominantAgainst) {
        return base;
    }
    const set = getTransitionSet(entry, hood.dominantAgainst);
    if (!set) {
        return base;
    }
    const edge = (direction) => pickWeighted(set.edges.get(direction), x, y);
    const corner = (direction) => pickWeighted(set.corners.get(direction), x, y);
    const inner = (direction) => pickWeighted(set.inners.get(direction), x, y);
    const cardinals = [[hood.n, "N"], [hood.e, "E"], [hood.s, "S"], [hood.w, "W"]];
    const foreignCardinals = cardinals.filter(([foreign]) => foreign).map(([, direction]) => direction);
    if (foreignCardinals.length === 0) {
        const diagonals = [[hood.ne, "NE"], [hood.se, "SE"], [hood.sw, "SW"], [hood.nw, "NW"]];
        for (const [foreign, direction] of diagonals) {
            if (!foreign) {
                continue;
            }
            const tile = inner(direction);
            if (tile) {
                return tile;
            }
        }
        return base;
    }
    if (foreignCardinals.length === 2) {
        const pair = foreignCardinals.join("");
        const cornerDirection = pair === "NE" ? "NE" : pair === "ES" ? "SE" : pair === "SW" ? "SW" : pair === "NW" ? "NW" : "";
        if (cornerDirection) {
            const tile = corner(cornerDirection);
            if (tile) {
                return tile;
            }
        }
    }
    // One foreign side wants its edge; unmatched shapes (opposite sides, three
    // or four foreign sides, a corner with no art) fall back to the first
    // foreign side's edge - a 1-wide strip has no art in a minimal set.
    for (const direction of foreignCardinals) {
        const tile = edge(direction);
        if (tile) {
            return tile;
        }
    }
    return base;
}
export function resolveTerrainGrid(grid, width, height, index) {
    const resolved = new Array(width * height).fill(null);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            resolved[y * width + x] = resolveTerrainCell(grid, width, height, x, y, index);
        }
    }
    return resolved;
}
function stringValue(value) {
    return typeof value === "string" ? value : value?.toString?.() ?? "";
}
function numberValue(value, fallback = 0) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}
//# sourceMappingURL=VillageTileRendering.js.map