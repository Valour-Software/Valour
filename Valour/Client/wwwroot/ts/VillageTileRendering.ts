export type TileDefinition = {
    kind: string;
    name: string;
    key: string;
    x: number;
    y: number;
    width: number;
    height: number;
};

export type BrushCellDefinition = {
    tileKey: string;
    strength: number;
    weight: number;
};

export type BrushDefinition = {
    name: string;
    key: string;
    size: number;
    cells: BrushCellDefinition[];
};

export type LoadedTexture = {
    url: string;
    image: HTMLImageElement;
    loaded: boolean;
    failed: boolean;
    pending: Array<() => void>;
};

export type TextureCache = Map<string, LoadedTexture>;

type UnknownRecord = Record<string, any>;

export function clamp(value: number, min: number, max: number): number {
    return Math.max(min, Math.min(max, value));
}

export function isTextInput(element: EventTarget | null): boolean {
    if (!(element instanceof HTMLElement)) {
        return false;
    }

    const tagName = element.tagName.toLowerCase();
    return tagName === "input" ||
        tagName === "textarea" ||
        tagName === "select" ||
        element.isContentEditable;
}

export function createTextureCache(): TextureCache {
    return new Map<string, LoadedTexture>();
}

export function loadStandaloneImage(url: string): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.referrerPolicy = "no-referrer";
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error(`Unable to load image: ${url}`));
        image.src = url;
    });
}

export function loadTexture(cache: TextureCache, url: string, onLoaded?: () => void): LoadedTexture | null {
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
            } else {
                existing.pending.push(onLoaded);
            }
        }

        return existing;
    }

    const texture: LoadedTexture = {
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

export function normalizeTileDefinitions(definitions: any): TileDefinition[] {
    if (!Array.isArray(definitions)) {
        return [];
    }

    return definitions
        .map(normalizeTileDefinition)
        .filter((definition): definition is TileDefinition => definition !== null);
}

export function normalizeTileDefinition(definition: any): TileDefinition | null {
    if (!definition || typeof definition !== "object") {
        return null;
    }

    const source = definition as UnknownRecord;
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

export function normalizeBrushDefinitions(brushes: any): BrushDefinition[] {
    if (!Array.isArray(brushes)) {
        return [];
    }

    return brushes
        .map(normalizeBrushDefinition)
        .filter((brush): brush is BrushDefinition => brush !== null);
}

export function normalizeBrushDefinition(brush: any): BrushDefinition | null {
    if (!brush || typeof brush !== "object") {
        return null;
    }

    const source = brush as UnknownRecord;
    const key = stringValue(source.key ?? source.Key);
    if (!key) {
        return null;
    }

    const size = Math.max(1, numberValue(source.size ?? source.Size, 1));
    const rawCells = Array.isArray(source.cells) ? source.cells : Array.isArray(source.Cells) ? source.Cells : [];
    const cells = rawCells.map((cell: UnknownRecord) => ({
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

export function isSpriteDefinition(definition: TileDefinition): boolean {
    return definition.kind.toLowerCase() === "sprite";
}

export function createDefinitionMap(definitions: TileDefinition[]): Map<string, TileDefinition> {
    return new Map(definitions.map(definition => [definition.key, definition]));
}

export function drawTilesetDefinition(
    ctx: CanvasRenderingContext2D,
    image: CanvasImageSource,
    definition: TileDefinition,
    sourceTileSize: number,
    destinationX: number,
    destinationY: number,
    destinationTileSize: number
): void {
    ctx.drawImage(
        image,
        definition.x * sourceTileSize,
        definition.y * sourceTileSize,
        definition.width * sourceTileSize,
        definition.height * sourceTileSize,
        destinationX,
        destinationY,
        definition.width * destinationTileSize,
        definition.height * destinationTileSize
    );
}

function stringValue(value: any): string {
    return typeof value === "string" ? value : value?.toString?.() ?? "";
}

function numberValue(value: any, fallback = 0): number {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}
