export type VillageDirection = "up" | "down" | "left" | "right";

type VillagePoint = {
    x: number;
    y: number;
};

type VillageBuildingTarget = {
    x: number;
    y: number;
    width: number;
    height: number;
    entranceTile?: VillagePoint | null;
    entranceTiles?: VillagePoint[] | null;
};

type VillagePlayerPosition = {
    tileX: number;
    tileY: number;
};

export function normalizeMovementKey(key: string): VillageDirection | null {
    const lowered = key.toLowerCase();
    if (lowered === "up" || lowered === "w" || lowered === "arrowup") return "up";
    if (lowered === "down" || lowered === "s" || lowered === "arrowdown") return "down";
    if (lowered === "left" || lowered === "a" || lowered === "arrowleft") return "left";
    if (lowered === "right" || lowered === "d" || lowered === "arrowright") return "right";
    return null;
}

export function distanceToBuildingInteraction(
    building: VillageBuildingTarget,
    player: VillagePlayerPosition): number {
    if (buildingContainsPoint(building, player.tileX, player.tileY)) {
        return 0;
    }

    const entrances = building.entranceTiles?.length
        ? building.entranceTiles
        : [building.entranceTile ?? {
            x: building.x + Math.floor(building.width / 2),
            y: building.y + building.height - 1
        }];

    return Math.min(...entrances.map(point =>
        Math.abs(point.x - player.tileX) + Math.abs(point.y - player.tileY)));
}

function buildingContainsPoint(building: VillageBuildingTarget, x: number, y: number): boolean {
    return x >= building.x &&
        x < building.x + building.width &&
        y >= building.y &&
        y < building.y + building.height;
}
