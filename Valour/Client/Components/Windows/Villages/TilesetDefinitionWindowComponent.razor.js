export function init(canvasId, fileInputId, dotNetRef, initialUrl) {
    const canvas = document.getElementById(canvasId);
    const fileInput = document.getElementById(fileInputId);
    const ctx = canvas.getContext("2d");

    const state = {
        canvas,
        ctx,
        dotNetRef,
        image: new Image(),
        imageUrl: initialUrl,
        imageLoaded: false,
        tileSize: 16,
        scale: 2,
        offsetX: 24,
        offsetY: 24,
        devicePixelRatio: 1,
        viewportWidth: 1,
        viewportHeight: 1,
        selection: { x: 0, y: 0, width: 1, height: 1 },
        savedDefinitions: [],
        dragging: false,
        panning: false,
        dragStart: null,
        panStart: null,
        keyboardPanSpeed: 24,
        localObjectUrl: null,
        destroyed: false
    };

    const runtime = {
        loadImageUrl(url) {
            return loadImage(state, url);
        },
        fit() {
            fitImage(state);
            draw(state);
        },
        setDefinitions(definitions) {
            state.savedDefinitions = normalizeDefinitions(definitions);
            draw(state);
        },
        setSelection(x, y, width, height) {
            state.selection = {
                x: Number(x) || 0,
                y: Number(y) || 0,
                width: Math.max(1, Number(width) || 1),
                height: Math.max(1, Number(height) || 1)
            };
            draw(state);
        },
        dispose() {
            state.destroyed = true;
            window.removeEventListener("resize", state.onResize);
            canvas.removeEventListener("mousedown", state.onMouseDown);
            canvas.removeEventListener("mousemove", state.onMouseMove);
            canvas.removeEventListener("mouseup", state.onMouseUp);
            canvas.removeEventListener("mouseleave", state.onMouseUp);
            canvas.removeEventListener("wheel", state.onWheel);
            window.removeEventListener("keydown", state.onKeyDown);
            fileInput.removeEventListener("change", state.onFileChange);
            revokeLocalObjectUrl(state);
        }
    };

    state.onResize = () => {
        resizeCanvas(state);
        draw(state);
    };

    state.onMouseDown = async (event) => {
        const point = getSheetPoint(state, event);
        if (event.button === 1 || event.altKey || event.metaKey) {
            state.panning = true;
            state.panStart = {
                x: event.clientX,
                y: event.clientY,
                offsetX: state.offsetX,
                offsetY: state.offsetY
            };
            return;
        }

        if (!point) {
            return;
        }

        state.dragging = true;
        state.dragStart = point;
        updateSelectionFromPoints(state, point, point);
        await notifySelection(state);
        draw(state);
    };

    state.onMouseMove = async (event) => {
        if (state.panning && state.panStart) {
            state.offsetX = state.panStart.offsetX + event.clientX - state.panStart.x;
            state.offsetY = state.panStart.offsetY + event.clientY - state.panStart.y;
            draw(state);
            return;
        }

        if (!state.dragging || !state.dragStart) {
            return;
        }

        const point = getSheetPoint(state, event);
        if (!point) {
            return;
        }

        updateSelectionFromPoints(state, state.dragStart, point);
        await notifySelection(state);
        draw(state);
    };

    state.onMouseUp = () => {
        state.dragging = false;
        state.panning = false;
        state.dragStart = null;
        state.panStart = null;
    };

    state.onWheel = (event) => {
        event.preventDefault();
        const before = getWorldPoint(state, event);
        const factor = Math.exp(-event.deltaY * 0.00024);
        state.scale = clamp(state.scale * factor, 0.5, 10);
        const rect = canvas.getBoundingClientRect();
        state.offsetX = event.clientX - rect.left - before.x * state.scale;
        state.offsetY = event.clientY - rect.top - before.y * state.scale;
        draw(state);
    };

    state.onKeyDown = (event) => {
        if (isTextInput(event.target)) {
            return;
        }

        const key = event.key.toLowerCase();
        if (!["w", "a", "s", "d"].includes(key)) {
            return;
        }

        event.preventDefault();
        panWithKey(state, key);
    };

    state.onFileChange = async () => {
        const file = fileInput.files?.[0];
        if (!file) {
            return;
        }

        revokeLocalObjectUrl(state);
        const url = URL.createObjectURL(file);
        state.localObjectUrl = url;
        fileInput.value = "";

        try {
            await loadImage(state, url);
            await dotNetRef.invokeMethodAsync("OnImageChanged", file.name, url);
        } catch (error) {
            console.error("Failed to load local tilesheet.", error);
        }
    };

    window.addEventListener("resize", state.onResize);
    canvas.addEventListener("mousedown", state.onMouseDown);
    canvas.addEventListener("mousemove", state.onMouseMove);
    canvas.addEventListener("mouseup", state.onMouseUp);
    canvas.addEventListener("mouseleave", state.onMouseUp);
    canvas.addEventListener("wheel", state.onWheel, { passive: false });
    window.addEventListener("keydown", state.onKeyDown);
    fileInput.addEventListener("change", state.onFileChange);

    resizeCanvas(state);
    loadImage(state, initialUrl);
    draw(state);
    return runtime;
}

function panWithKey(state, key) {
    switch (key) {
        case "w":
            state.offsetY += state.keyboardPanSpeed;
            break;
        case "a":
            state.offsetX += state.keyboardPanSpeed;
            break;
        case "s":
            state.offsetY -= state.keyboardPanSpeed;
            break;
        case "d":
            state.offsetX -= state.keyboardPanSpeed;
            break;
    }

    draw(state);
}

function isTextInput(element) {
    if (!element) {
        return false;
    }

    const tagName = element.tagName?.toLowerCase();
    return tagName === "input" ||
        tagName === "textarea" ||
        tagName === "select" ||
        element.isContentEditable;
}

function revokeLocalObjectUrl(state) {
    if (!state.localObjectUrl) {
        return;
    }

    URL.revokeObjectURL(state.localObjectUrl);
    state.localObjectUrl = null;
}

function loadImage(state, url) {
    return new Promise((resolve, reject) => {
        state.imageLoaded = false;
        state.imageUrl = url;
        const image = new Image();
        state.image = image;

        image.onload = () => {
            state.imageLoaded = true;
            state.dotNetRef
                .invokeMethodAsync("OnImageLoaded", image.width, image.height)
                .catch(error => console.warn("Failed to report tilesheet size.", error));
            fitImage(state);
            draw(state);
            resolve();
        };

        image.onerror = () => {
            reject(new Error(`Unable to load tilesheet image: ${url}`));
        };

        image.src = url;
    });
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

function fitImage(state) {
    if (!state.imageLoaded) {
        return;
    }

    const fitX = (state.viewportWidth - 48) / state.image.width;
    const fitY = (state.viewportHeight - 48) / state.image.height;
    state.scale = clamp(Math.floor(Math.min(fitX, fitY) * 2) / 2, 0.5, 6);
    state.offsetX = Math.round((state.viewportWidth - state.image.width * state.scale) / 2);
    state.offsetY = Math.round((state.viewportHeight - state.image.height * state.scale) / 2);
}

function draw(state) {
    const { ctx } = state;
    ctx.clearRect(0, 0, state.viewportWidth, state.viewportHeight);
    ctx.fillStyle = "#151922";
    ctx.fillRect(0, 0, state.viewportWidth, state.viewportHeight);

    if (!state.imageLoaded) {
        return;
    }

    const view = getView(state);
    ctx.drawImage(state.image, view.x, view.y, view.width, view.height);

    drawGrid(state, view);
    drawSavedDefinitions(state, view);
    drawSelection(state, view);
}

function normalizeDefinitions(definitions) {
    if (!Array.isArray(definitions)) {
        return [];
    }

    return definitions
        .map(definition => ({
            x: Number(definition.x) || 0,
            y: Number(definition.y) || 0,
            width: Math.max(1, Number(definition.width) || 1),
            height: Math.max(1, Number(definition.height) || 1),
            kind: definition.kind || "Tile",
            name: definition.name || definition.key || "",
            key: definition.key || "",
            hasTerrain: Boolean(definition.hasTerrain)
        }))
        .filter(definition => definition.width > 0 && definition.height > 0);
}

function drawGrid(state, view) {
    const { ctx } = state;

    ctx.save();
    ctx.strokeStyle = "rgba(255,255,255,0.28)";
    ctx.lineWidth = 1;
    for (let x = 0; x <= state.image.width; x += state.tileSize) {
        const screenX = Math.round(view.x + x * view.scale) + 0.5;
        ctx.beginPath();
        ctx.moveTo(screenX, view.y);
        ctx.lineTo(screenX, view.y + view.height);
        ctx.stroke();
    }

    for (let y = 0; y <= state.image.height; y += state.tileSize) {
        const screenY = Math.round(view.y + y * view.scale) + 0.5;
        ctx.beginPath();
        ctx.moveTo(view.x, screenY);
        ctx.lineTo(view.x + view.width, screenY);
        ctx.stroke();
    }
    ctx.restore();
}

function drawSavedDefinitions(state, view) {
    const { ctx } = state;
    if (state.savedDefinitions.length === 0) {
        return;
    }

    ctx.save();
    ctx.font = "700 11px sans-serif";
    ctx.textBaseline = "top";

    for (const definition of state.savedDefinitions) {
        const rect = getDefinitionRect(state, view, definition);
        const color = getDefinitionColor(definition);
        const label = definition.name || definition.key;

        ctx.fillStyle = color.fill;
        ctx.strokeStyle = color.stroke;
        ctx.lineWidth = 2;
        ctx.setLineDash(definition.kind === "Sprite" ? [5, 3] : []);
        ctx.fillRect(rect.x, rect.y, rect.width, rect.height);
        ctx.strokeRect(rect.x + 1, rect.y + 1, Math.max(0, rect.width - 2), Math.max(0, rect.height - 2));

        if (label && rect.width >= 46 && rect.height >= 22) {
            const labelText = label.length > 24 ? `${label.slice(0, 21)}...` : label;
            const labelWidth = Math.min(rect.width - 4, ctx.measureText(labelText).width + 8);
            ctx.setLineDash([]);
            ctx.fillStyle = color.labelBackground;
            ctx.fillRect(rect.x + 2, rect.y + 2, labelWidth, 16);
            ctx.fillStyle = color.labelText;
            ctx.fillText(labelText, rect.x + 6, rect.y + 4, labelWidth - 8);
        }
    }

    ctx.restore();
}

function getDefinitionRect(state, view, definition) {
    const x = Math.round(view.x + definition.x * state.tileSize * view.scale);
    const y = Math.round(view.y + definition.y * state.tileSize * view.scale);
    const width = Math.round(definition.width * state.tileSize * view.scale);
    const height = Math.round(definition.height * state.tileSize * view.scale);

    return { x, y, width, height };
}

function getDefinitionColor(definition) {
    if (definition.hasTerrain) {
        return {
            stroke: "#69e6a3",
            fill: "rgba(105, 230, 163, 0.13)",
            labelBackground: "rgba(34, 87, 58, 0.88)",
            labelText: "#eafff2"
        };
    }

    if (definition.kind === "Sprite") {
        return {
            stroke: "#65d7ff",
            fill: "rgba(101, 215, 255, 0.12)",
            labelBackground: "rgba(32, 75, 94, 0.88)",
            labelText: "#ecfbff"
        };
    }

    return {
        stroke: "#a8b7ff",
        fill: "rgba(168, 183, 255, 0.11)",
        labelBackground: "rgba(50, 59, 112, 0.88)",
        labelText: "#f0f3ff"
    };
}

function drawSelection(state, view) {
    const { ctx, selection } = state;
    const x = view.x + selection.x * state.tileSize * view.scale;
    const y = view.y + selection.y * state.tileSize * view.scale;
    const width = selection.width * state.tileSize * view.scale;
    const height = selection.height * state.tileSize * view.scale;

    ctx.save();
    ctx.fillStyle = "rgba(255, 211, 91, 0.22)";
    ctx.strokeStyle = "#ffd35b";
    ctx.lineWidth = 2;
    ctx.fillRect(Math.round(x), Math.round(y), Math.round(width), Math.round(height));
    ctx.strokeRect(Math.round(x) + 1, Math.round(y) + 1, Math.round(width) - 2, Math.round(height) - 2);
    ctx.restore();
}

function getSheetPoint(state, event) {
    if (!state.imageLoaded) {
        return null;
    }

    const world = getWorldPoint(state, event);
    if (world.x < 0 || world.y < 0 || world.x >= state.image.width || world.y >= state.image.height) {
        return null;
    }

    return {
        x: clamp(Math.floor(world.x / state.tileSize), 0, Math.floor(state.image.width / state.tileSize) - 1),
        y: clamp(Math.floor(world.y / state.tileSize), 0, Math.floor(state.image.height / state.tileSize) - 1)
    };
}

function getWorldPoint(state, event) {
    const rect = state.canvas.getBoundingClientRect();
    const view = getView(state);
    return {
        x: (event.clientX - rect.left - view.x) / view.scale,
        y: (event.clientY - rect.top - view.y) / view.scale
    };
}

function getView(state) {
    return {
        x: Math.round(state.offsetX),
        y: Math.round(state.offsetY),
        scale: state.scale,
        width: state.image.width * state.scale,
        height: state.image.height * state.scale
    };
}

function updateSelectionFromPoints(state, start, end) {
    const x = Math.min(start.x, end.x);
    const y = Math.min(start.y, end.y);
    state.selection = {
        x,
        y,
        width: Math.abs(end.x - start.x) + 1,
        height: Math.abs(end.y - start.y) + 1
    };
}

async function notifySelection(state) {
    await state.dotNetRef.invokeMethodAsync(
        "OnSelectionChanged",
        state.selection.x,
        state.selection.y,
        state.selection.width,
        state.selection.height);
}

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}
