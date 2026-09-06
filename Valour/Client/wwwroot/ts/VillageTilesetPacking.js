export const TILESET_FORMAT = "valour.tileset";
export const TILESET_FORMAT_VERSION = 1;
function value(source, lower, upper, fallback) {
    return (source[lower] ?? source[upper] ?? fallback);
}
function positiveInteger(raw, fallback) {
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? Math.max(1, Math.floor(parsed)) : fallback;
}
function nonNegativeInteger(raw, fallback = 0) {
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? Math.max(0, Math.floor(parsed)) : fallback;
}
function nextPowerOfTwo(value) {
    let result = 1;
    while (result < value) {
        result *= 2;
    }
    return result;
}
function clone(input) {
    return JSON.parse(JSON.stringify(input));
}
function setCoordinate(target, lower, upper, coordinate) {
    const matchingUpperCoordinate = upper.startsWith("Source") ? upper.slice("Source".length) : "";
    if (Object.prototype.hasOwnProperty.call(target, upper) ||
        (matchingUpperCoordinate && Object.prototype.hasOwnProperty.call(target, matchingUpperCoordinate))) {
        target[upper] = coordinate;
    }
    else {
        target[lower] = coordinate;
    }
}
function getDefinitionSourceRect(definition, tileSize) {
    const x = nonNegativeInteger(value(definition, "sourceX", "SourceX", value(definition, "x", "X", 0)));
    const y = nonNegativeInteger(value(definition, "sourceY", "SourceY", value(definition, "y", "Y", 0)));
    const width = positiveInteger(value(definition, "width", "Width", 1), 1);
    const height = positiveInteger(value(definition, "height", "Height", 1), 1);
    return {
        sourceX: x * tileSize,
        sourceY: y * tileSize,
        width: width * tileSize,
        height: height * tileSize
    };
}
function getWallSetSourceRect(wallSet, defaultTileSize) {
    const tileSize = positiveInteger(value(wallSet, "tileSize", "TileSize", defaultTileSize), defaultTileSize);
    const sourceX = nonNegativeInteger(value(wallSet, "sourceOriginX", "SourceOriginX", value(wallSet, "originX", "OriginX", 0)));
    const sourceY = nonNegativeInteger(value(wallSet, "sourceOriginY", "SourceOriginY", value(wallSet, "originY", "OriginY", 0)));
    const columns = positiveInteger(value(wallSet, "columns", "Columns", 8), 8);
    const rows = positiveInteger(value(wallSet, "rows", "Rows", 7), 7);
    return {
        sourceX,
        sourceY,
        width: columns * tileSize,
        height: rows * tileSize
    };
}
function validateSourceRect(region, sourceWidth, sourceHeight, label) {
    if (region.sourceX + region.width > sourceWidth || region.sourceY + region.height > sourceHeight) {
        throw new Error(`${label} falls outside the ${sourceWidth}x${sourceHeight} source sheet.`);
    }
}
function validateSourceSheetContainment(region, source, label) {
    const rawSheets = source?.sheets ?? source?.Sheets;
    const sheets = Array.isArray(rawSheets) ? rawSheets : [];
    if (sheets.length === 0) {
        return;
    }
    const containingSheet = sheets.find((sheet) => {
        const offsetX = nonNegativeInteger(value(sheet, "offsetX", "OffsetX", 0));
        const offsetY = nonNegativeInteger(value(sheet, "offsetY", "OffsetY", 0));
        const width = positiveInteger(value(sheet, "width", "Width", 1), 1);
        const height = positiveInteger(value(sheet, "height", "Height", 1), 1);
        return region.sourceX >= offsetX &&
            region.sourceY >= offsetY &&
            region.sourceX + region.width <= offsetX + width &&
            region.sourceY + region.height <= offsetY + height;
    });
    if (!containingSheet) {
        throw new Error(`${label} crosses a source-sheet boundary or aligned padding.`);
    }
}
/**
 * Creates a deterministic, tile-aligned shelf pack. A one-tile transparent
 * gutter avoids texture bleeding while keeping the existing integer tile
 * coordinate contract intact.
 */
export function createTilesetPackPlan(manifest, sourceWidth, sourceHeight, paddingTiles = 1) {
    const tileSize = positiveInteger(value(manifest, "tileSize", "TileSize", 16), 16);
    if (sourceWidth < tileSize || sourceHeight < tileSize) {
        throw new Error("The source sheet is smaller than one tile.");
    }
    const definitions = Array.isArray(manifest.definitions) ? manifest.definitions : [];
    const wallSets = Array.isArray(manifest.wallSets) ? manifest.wallSets : [];
    const deduplicated = new Map();
    for (const definition of definitions) {
        const key = String(value(definition, "key", "Key", "")).trim();
        if (!key) {
            continue;
        }
        const rect = getDefinitionSourceRect(definition, tileSize);
        validateSourceRect(rect, sourceWidth, sourceHeight, `Definition ${key}`);
        validateSourceSheetContainment(rect, manifest.source, `Definition ${key}`);
        const rectKey = `${rect.sourceX}:${rect.sourceY}:${rect.width}:${rect.height}`;
        const region = deduplicated.get(rectKey) ?? { ...rect, definitionKeys: [], wallSetKeys: [] };
        region.definitionKeys.push(key);
        deduplicated.set(rectKey, region);
    }
    for (const wallSet of wallSets) {
        const key = String(value(wallSet, "key", "Key", "")).trim();
        if (!key) {
            continue;
        }
        const rect = getWallSetSourceRect(wallSet, tileSize);
        validateSourceRect(rect, sourceWidth, sourceHeight, `Wall set ${key}`);
        validateSourceSheetContainment(rect, manifest.source, `Wall set ${key}`);
        const rectKey = `${rect.sourceX}:${rect.sourceY}:${rect.width}:${rect.height}`;
        const region = deduplicated.get(rectKey) ?? { ...rect, definitionKeys: [], wallSetKeys: [] };
        region.wallSetKeys.push(key);
        deduplicated.set(rectKey, region);
    }
    const pending = [...deduplicated.values()].sort((left, right) => right.height - left.height ||
        right.width - left.width ||
        left.sourceY - right.sourceY ||
        left.sourceX - right.sourceX);
    if (pending.length === 0) {
        throw new Error("The tileset has no definitions or wall sets to pack.");
    }
    const padding = Math.max(0, Math.floor(paddingTiles)) * tileSize;
    const totalArea = pending.reduce((sum, region) => sum + (region.width + padding * 2) * (region.height + padding * 2), 0);
    const widest = Math.max(...pending.map(region => region.width + padding * 2));
    const atlasWidth = nextPowerOfTwo(Math.max(widest, Math.ceil(Math.sqrt(totalArea))));
    let cursorX = 0;
    let cursorY = 0;
    let shelfHeight = 0;
    let usedWidth = 0;
    const regions = [];
    for (const pendingRegion of pending) {
        const outerWidth = pendingRegion.width + padding * 2;
        const outerHeight = pendingRegion.height + padding * 2;
        if (cursorX > 0 && cursorX + outerWidth > atlasWidth) {
            cursorX = 0;
            cursorY += shelfHeight;
            shelfHeight = 0;
        }
        regions.push({
            id: `region-${regions.length + 1}`,
            sourceX: pendingRegion.sourceX,
            sourceY: pendingRegion.sourceY,
            width: pendingRegion.width,
            height: pendingRegion.height,
            atlasX: cursorX + padding,
            atlasY: cursorY + padding,
            definitionKeys: [...pendingRegion.definitionKeys].sort(),
            wallSetKeys: [...pendingRegion.wallSetKeys].sort()
        });
        cursorX += outerWidth;
        shelfHeight = Math.max(shelfHeight, outerHeight);
        usedWidth = Math.max(usedWidth, cursorX);
    }
    const usedHeight = cursorY + shelfHeight;
    return {
        tileSize,
        sourceWidth,
        sourceHeight,
        atlasWidth: nextPowerOfTwo(Math.max(tileSize, Math.min(atlasWidth, usedWidth))),
        atlasHeight: nextPowerOfTwo(Math.max(tileSize, usedHeight)),
        paddingTiles: Math.max(0, Math.floor(paddingTiles)),
        regions
    };
}
/** Produces the one canonical manifest used by both the game and editor. */
export function compileTilesetManifest(manifest, plan, image, sha256) {
    const compiled = clone(manifest);
    compiled.format = TILESET_FORMAT;
    compiled.version = TILESET_FORMAT_VERSION;
    compiled.tileSize = plan.tileSize;
    compiled.image = image;
    compiled.source = {
        ...(manifest.source ?? {}),
        width: plan.sourceWidth,
        height: plan.sourceHeight,
        sha256: sha256 || "",
        fileName: String(manifest.source?.fileName ?? "")
    };
    compiled.atlas = {
        width: plan.atlasWidth,
        height: plan.atlasHeight,
        paddingTiles: plan.paddingTiles,
        packed: true
    };
    const definitionsByKey = new Map();
    for (const definition of compiled.definitions ?? []) {
        const key = String(value(definition, "key", "Key", ""));
        if (key) {
            definitionsByKey.set(key, definition);
        }
    }
    const wallSetsByKey = new Map();
    for (const wallSet of compiled.wallSets ?? []) {
        const key = String(value(wallSet, "key", "Key", ""));
        if (key) {
            wallSetsByKey.set(key, wallSet);
        }
    }
    for (const region of plan.regions) {
        for (const key of region.definitionKeys) {
            const definition = definitionsByKey.get(key);
            if (!definition) {
                continue;
            }
            setCoordinate(definition, "sourceX", "SourceX", region.sourceX / plan.tileSize);
            setCoordinate(definition, "sourceY", "SourceY", region.sourceY / plan.tileSize);
            setCoordinate(definition, "x", "X", region.atlasX / plan.tileSize);
            setCoordinate(definition, "y", "Y", region.atlasY / plan.tileSize);
        }
        for (const key of region.wallSetKeys) {
            const wallSet = wallSetsByKey.get(key);
            if (!wallSet) {
                continue;
            }
            setCoordinate(wallSet, "sourceOriginX", "SourceOriginX", region.sourceX);
            setCoordinate(wallSet, "sourceOriginY", "SourceOriginY", region.sourceY);
            setCoordinate(wallSet, "originX", "OriginX", region.atlasX);
            setCoordinate(wallSet, "originY", "OriginY", region.atlasY);
            if (Object.prototype.hasOwnProperty.call(wallSet, "Image")) {
                wallSet.Image = image;
            }
            else {
                wallSet.image = image;
            }
        }
    }
    return { manifest: compiled, plan };
}
export function isCanonicalTilesetManifest(manifest) {
    return manifest?.format === TILESET_FORMAT && manifest?.version === TILESET_FORMAT_VERSION;
}
export function getReconstructionRegions(manifest) {
    if (!isCanonicalTilesetManifest(manifest)) {
        throw new Error("Unsupported tileset manifest format.");
    }
    const tileSize = positiveInteger(manifest.tileSize, 16);
    const regions = new Map();
    for (const definition of manifest.definitions ?? []) {
        const key = String(value(definition, "key", "Key", ""));
        const source = getDefinitionSourceRect(definition, tileSize);
        const atlasX = nonNegativeInteger(value(definition, "x", "X", 0)) * tileSize;
        const atlasY = nonNegativeInteger(value(definition, "y", "Y", 0)) * tileSize;
        const regionKey = `${source.sourceX}:${source.sourceY}:${source.width}:${source.height}:${atlasX}:${atlasY}`;
        const region = regions.get(regionKey) ?? {
            id: `region-${regions.size + 1}`,
            ...source,
            atlasX,
            atlasY,
            definitionKeys: [],
            wallSetKeys: []
        };
        region.definitionKeys.push(key);
        regions.set(regionKey, region);
    }
    for (const wallSet of manifest.wallSets ?? []) {
        const key = String(value(wallSet, "key", "Key", ""));
        const source = getWallSetSourceRect(wallSet, tileSize);
        const atlasX = nonNegativeInteger(value(wallSet, "originX", "OriginX", 0));
        const atlasY = nonNegativeInteger(value(wallSet, "originY", "OriginY", 0));
        const regionKey = `${source.sourceX}:${source.sourceY}:${source.width}:${source.height}:${atlasX}:${atlasY}`;
        const region = regions.get(regionKey) ?? {
            id: `region-${regions.size + 1}`,
            ...source,
            atlasX,
            atlasY,
            definitionKeys: [],
            wallSetKeys: []
        };
        region.wallSetKeys.push(key);
        regions.set(regionKey, region);
    }
    return [...regions.values()];
}
function canvasToBlob(canvas) {
    return new Promise((resolve, reject) => {
        canvas.toBlob(blob => {
            if (blob) {
                resolve(blob);
            }
            else {
                reject(new Error("The browser could not encode the packed tileset."));
            }
        }, "image/png");
    });
}
function drawExtrudedRegion(context, image, region, paddingPixels) {
    context.drawImage(image, region.sourceX, region.sourceY, region.width, region.height, region.atlasX, region.atlasY, region.width, region.height);
    if (paddingPixels < 1) {
        return;
    }
    const left = region.atlasX;
    const top = region.atlasY;
    const right = left + region.width;
    const bottom = top + region.height;
    context.drawImage(image, region.sourceX, region.sourceY, region.width, 1, left, top - 1, region.width, 1);
    context.drawImage(image, region.sourceX, region.sourceY + region.height - 1, region.width, 1, left, bottom, region.width, 1);
    context.drawImage(image, region.sourceX, region.sourceY, 1, region.height, left - 1, top, 1, region.height);
    context.drawImage(image, region.sourceX + region.width - 1, region.sourceY, 1, region.height, right, top, 1, region.height);
    context.drawImage(image, region.sourceX, region.sourceY, 1, 1, left - 1, top - 1, 1, 1);
    context.drawImage(image, region.sourceX + region.width - 1, region.sourceY, 1, 1, right, top - 1, 1, 1);
    context.drawImage(image, region.sourceX, region.sourceY + region.height - 1, 1, 1, left - 1, bottom, 1, 1);
    context.drawImage(image, region.sourceX + region.width - 1, region.sourceY + region.height - 1, 1, 1, right, bottom, 1, 1);
}
export async function renderTilesetPackage(sourceImage, manifest, imageFileName, sourceSha256) {
    const plan = createTilesetPackPlan(manifest, sourceImage.width, sourceImage.height);
    const canvas = document.createElement("canvas");
    canvas.width = plan.atlasWidth;
    canvas.height = plan.atlasHeight;
    const context = canvas.getContext("2d");
    if (!context) {
        throw new Error("Canvas rendering is unavailable.");
    }
    context.imageSmoothingEnabled = false;
    const paddingPixels = plan.paddingTiles * plan.tileSize;
    for (const region of plan.regions) {
        drawExtrudedRegion(context, sourceImage, region, paddingPixels);
    }
    const result = compileTilesetManifest(manifest, plan, imageFileName, sourceSha256);
    return {
        ...result,
        json: JSON.stringify(result.manifest, null, 2),
        imageBlob: await canvasToBlob(canvas)
    };
}
export async function reconstructTilesetSource(atlasImage, manifest) {
    if (!isCanonicalTilesetManifest(manifest)) {
        throw new Error("Unsupported tileset manifest format.");
    }
    const width = positiveInteger(manifest.source?.width, 1);
    const height = positiveInteger(manifest.source?.height, 1);
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) {
        throw new Error("Canvas rendering is unavailable.");
    }
    context.imageSmoothingEnabled = false;
    for (const region of getReconstructionRegions(manifest)) {
        context.drawImage(atlasImage, region.atlasX, region.atlasY, region.width, region.height, region.sourceX, region.sourceY, region.width, region.height);
    }
    return { imageBlob: await canvasToBlob(canvas), width, height };
}
//# sourceMappingURL=VillageTilesetPacking.js.map