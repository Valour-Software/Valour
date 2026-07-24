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
        return existing;
    }
    const texture = {
        url,
        image: new Image(),
        loaded: false,
        failed: false
    };
    texture.image.referrerPolicy = "no-referrer";
    texture.image.onload = () => {
        texture.loaded = true;
        onLoaded?.();
    };
    texture.image.onerror = () => {
        texture.failed = true;
        onLoaded?.();
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
    return {
        kind: stringValue(source.kind ?? source.Kind) || "Tile",
        name: stringValue(source.name ?? source.Name) || key,
        key,
        x: numberValue(source.x ?? source.X),
        y: numberValue(source.y ?? source.Y),
        width: Math.max(1, numberValue(source.width ?? source.Width, 1)),
        height: Math.max(1, numberValue(source.height ?? source.Height, 1))
    };
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