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
    const collision = rawCollision
        .slice(0, width * height)
        .map(value => value === true || value === 1 || value === "true");
    while (collision.length < width * height) {
        collision.push(false);
    }
    return {
        kind: stringValue(source.kind ?? source.Kind) || "Tile",
        name: stringValue(source.name ?? source.Name) || key,
        key,
        x: numberValue(source.x ?? source.X),
        y: numberValue(source.y ?? source.Y),
        width,
        height,
        collision
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
function stringValue(value) {
    return typeof value === "string" ? value : value?.toString?.() ?? "";
}
function numberValue(value, fallback = 0) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}
//# sourceMappingURL=VillageTileRendering.js.map