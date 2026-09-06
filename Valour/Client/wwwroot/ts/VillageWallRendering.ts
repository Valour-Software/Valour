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

const FRAME_BY_MASK = new Map<number, number>([
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

export type WallDefinition = {
    wallSetKey: string;
    frame: number;
};

export function normalizeWallMask(rawMask: number): number {
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

export function resolveWallFrame(rawMask: number): number {
    return FRAME_BY_MASK.get(normalizeWallMask(rawMask)) ?? 46;
}

export function makeWallDefinitionKey(wallSetKey: string, frame: number): string | null {
    if (!isValidWallSetKey(wallSetKey) || !Number.isInteger(frame) || frame < 0 || frame >= WALL_FRAME_COUNT) {
        return null;
    }
    return `${WALL_DEFINITION_PREFIX}${wallSetKey}:${frame}`;
}

export function parseWallDefinitionKey(value: unknown): WallDefinition | null {
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

export function isValidWallSetKey(value: unknown): value is string {
    return typeof value === "string" &&
        value.length > 0 &&
        value.length <= 64 &&
        /^[A-Za-z0-9._-]+$/.test(value);
}
