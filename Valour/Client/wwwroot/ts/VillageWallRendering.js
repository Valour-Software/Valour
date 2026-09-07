export const WALL_DEFINITION_PREFIX = "wall:";
export const WALL_FRAME_COUNT = 47;
export const WALL_NORTH = 1;
export const WALL_NORTH_EAST = 2;
export const WALL_EAST = 4;
export const WALL_SOUTH_EAST = 8;
export const WALL_SOUTH = 16;
export const WALL_SOUTH_WEST = 32;
export const WALL_WEST = 64;
export const WALL_NORTH_WEST = 128;
const FRAME_BY_MASK = new Map([
    [255, 0], [127, 1], [253, 2], [125, 3],
    [247, 4], [119, 5], [245, 6], [117, 7],
    [223, 8], [95, 9], [221, 10], [93, 11],
    [215, 12], [87, 13], [213, 14], [85, 15],
    [31, 16], [29, 17], [23, 18], [21, 19],
    [124, 20], [116, 21], [92, 22], [84, 23],
    [241, 24], [209, 25], [113, 26], [81, 27],
    [199, 28], [71, 29], [197, 30], [69, 31],
    [17, 32], [68, 33], [28, 34], [20, 35],
    [112, 36], [80, 37], [193, 38], [65, 39],
    [7, 40], [5, 41], [16, 42], [4, 43],
    [1, 44], [64, 45], [0, 46]
]);
export function normalizeWallMask(rawMask) {
    let mask = (Number.isFinite(rawMask) ? rawMask : 0) & 0xff;
    if ((mask & (WALL_NORTH | WALL_EAST)) !== (WALL_NORTH | WALL_EAST)) {
        mask &= ~WALL_NORTH_EAST;
    }
    if ((mask & (WALL_EAST | WALL_SOUTH)) !== (WALL_EAST | WALL_SOUTH)) {
        mask &= ~WALL_SOUTH_EAST;
    }
    if ((mask & (WALL_SOUTH | WALL_WEST)) !== (WALL_SOUTH | WALL_WEST)) {
        mask &= ~WALL_SOUTH_WEST;
    }
    if ((mask & (WALL_WEST | WALL_NORTH)) !== (WALL_WEST | WALL_NORTH)) {
        mask &= ~WALL_NORTH_WEST;
    }
    return mask;
}
export function resolveWallFrame(rawMask) {
    return FRAME_BY_MASK.get(normalizeWallMask(rawMask)) ?? 46;
}
export function makeWallDefinitionKey(wallSetKey, frame) {
    if (!isValidWallSetKey(wallSetKey) || !Number.isInteger(frame) || frame < 0 || frame >= WALL_FRAME_COUNT) {
        return null;
    }
    return `${WALL_DEFINITION_PREFIX}${wallSetKey}:${frame}`;
}
export function parseWallDefinitionKey(value) {
    if (typeof value !== "string" || !value.startsWith(WALL_DEFINITION_PREFIX)) {
        return null;
    }
    const separator = value.lastIndexOf(":");
    const wallSetKey = value.slice(WALL_DEFINITION_PREFIX.length, separator);
    const rawFrame = value.slice(separator + 1);
    if (!isValidWallSetKey(wallSetKey) || !/^\d+$/.test(rawFrame)) {
        return null;
    }
    const frame = Number(rawFrame);
    return frame >= 0 && frame < WALL_FRAME_COUNT ? { wallSetKey, frame } : null;
}
export function isValidWallSetKey(value) {
    return typeof value === "string" &&
        value.length > 0 &&
        value.length <= 64 &&
        /^[A-Za-z0-9._-]+$/.test(value);
}
export function getWallNeighborMask(hasWall, x, y) {
    const offsets = [[0, -1], [1, -1], [1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0], [-1, -1]];
    return normalizeWallMask(offsets.reduce((mask, [dx, dy], bit) => hasWall(x + dx, y + dy) ? mask | (1 << bit) : mask, 0));
}
export function wallMaskForFrame(frame) {
    for (const [mask, candidate] of FRAME_BY_MASK) {
        if (candidate === frame)
            return mask;
    }
    return 0;
}
export function buildRectangleCells(start, end, outline) {
    const cells = [];
    const left = Math.min(start.x, end.x), right = Math.max(start.x, end.x);
    const top = Math.min(start.y, end.y), bottom = Math.max(start.y, end.y);
    for (let y = top; y <= bottom; y++) {
        for (let x = left; x <= right; x++) {
            if (!outline || x === left || x === right || y === top || y === bottom)
                cells.push({ x, y });
        }
    }
    return cells;
}
export const ROOM_WALL_SIZE = 16;
export const ROOM_WALL_RISE = 26;
/** The logical blob mask describes the footprint, not a frame in LimeZu's room builder sheet. */
export function roomWallContains(rawMask, x, y) {
    const mask = normalizeWallMask(rawMask);
    const west = x < 5, east = x >= 11, north = y < 5, south = y >= 11;
    if (!west && !east && !north && !south)
        return true;
    if (north && west)
        return !!(mask & WALL_NORTH_WEST);
    if (north && east)
        return !!(mask & WALL_NORTH_EAST);
    if (south && west)
        return !!(mask & WALL_SOUTH_WEST);
    if (south && east)
        return !!(mask & WALL_SOUTH_EAST);
    if (north)
        return !!(mask & WALL_NORTH);
    if (south)
        return !!(mask & WALL_SOUTH);
    if (west)
        return !!(mask & WALL_WEST);
    return !!(mask & WALL_EAST);
}
export function renderRoomWall(ctx, image, originX, originY, mask, topColor) {
    ctx.imageSmoothingEnabled = false;
    // Exposed south edges are extruded at the source art's native pixel scale.
    // Neighbouring footprints share edges, so long walls have no repeated end caps.
    for (let y = 0; y < ROOM_WALL_SIZE; y++) {
        for (let x = 0; x < ROOM_WALL_SIZE;) {
            if (!roomWallContains(mask, x, y) || roomWallContains(mask, x, y + 1)) {
                x++;
                continue;
            }
            const start = x++;
            while (x < ROOM_WALL_SIZE && roomWallContains(mask, x, y) && !roomWallContains(mask, x, y + 1))
                x++;
            const width = x - start, top = y + 1;
            ctx.drawImage(image, originX + 64 + start, originY + 38, width, ROOM_WALL_RISE, start, top, width, ROOM_WALL_RISE);
            ctx.fillStyle = "#38374d";
            if (start > 0 || !(mask & WALL_WEST))
                ctx.fillRect(start, top, 1, ROOM_WALL_RISE);
            if (x < ROOM_WALL_SIZE || !(mask & WALL_EAST))
                ctx.fillRect(x - 1, top, 1, ROOM_WALL_RISE);
        }
    }
    for (let y = 0; y < ROOM_WALL_SIZE; y++) {
        for (let x = 0; x < ROOM_WALL_SIZE; x++) {
            if (!roomWallContains(mask, x, y))
                continue;
            const border = !roomWallContains(mask, x - 1, y) || !roomWallContains(mask, x + 1, y) ||
                !roomWallContains(mask, x, y - 1) || !roomWallContains(mask, x, y + 1);
            ctx.fillStyle = border ? "#38374d" : topColor;
            ctx.fillRect(x, y, 1, 1);
        }
    }
}
//# sourceMappingURL=VillageWallRendering.js.map