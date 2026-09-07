import test from "node:test";
import assert from "node:assert/strict";

import {
    normalizeMovementKey,
    distanceToBuildingInteraction
} from "../../Client/wwwroot/ts/VillageHandheldControls.js";

test("handheld directions share keyboard movement normalization", () => {
    assert.equal(normalizeMovementKey("up"), "up");
    assert.equal(normalizeMovementKey("ArrowDown"), "down");
    assert.equal(normalizeMovementKey("a"), "left");
    assert.equal(normalizeMovementKey("RIGHT"), "right");
    assert.equal(normalizeMovementKey("Enter"), null);
});

test("interaction uses a building entrance and only reaches adjacent places", () => {
    const building = {
        x: 8,
        y: 3,
        width: 4,
        height: 4,
        entranceTiles: [{ x: 9, y: 7 }]
    };

    assert.equal(distanceToBuildingInteraction(building, { tileX: 9, tileY: 7 }), 0);
    assert.equal(distanceToBuildingInteraction(building, { tileX: 9, tileY: 8 }), 1);
    assert.equal(distanceToBuildingInteraction(building, { tileX: 9, tileY: 10 }), 3);
});
