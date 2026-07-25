import { clamp, isTextInput, loadStandaloneImage } from "../../../ts/VillageTileRendering.js";

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
        // Set while an edge or corner handle of the selection is being
        // dragged: which sides follow the cursor, and the tile coordinates of
        // the opposite (anchored) edges.
        resizing: null,
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

        // Grabbing a selection handle resizes the existing bounds instead of
        // starting a new rubber-band selection.
        const handle = getResizeHandleAt(state, event);
        if (handle) {
            state.resizing = {
                handle,
                anchorLeft: state.selection.x,
                anchorTop: state.selection.y,
                anchorRight: state.selection.x + state.selection.width - 1,
                anchorBottom: state.selection.y + state.selection.height - 1
            };
            return;
        }

        const point = getSheetPoint(state, event);
        if (!point) {
            return;
        }

        state.dragging = true;
        state.dragStart = point;
        updateSelectionFromPoints(state, point, point);
        await notifySelection(state, true);
        draw(state);
    };

    state.onMouseMove = async (event) => {
        if (state.panning && state.panStart) {
            state.offsetX = state.panStart.offsetX + event.clientX - state.panStart.x;
            state.offsetY = state.panStart.offsetY + event.clientY - state.panStart.y;
            draw(state);
            return;
        }

        if (state.resizing) {
            applyResize(state, event);
            await notifySelection(state, true);
            draw(state);
            return;
        }

        if (!state.dragging || !state.dragStart) {
            updateHoverCursor(state, event);
            return;
        }

        const point = getSheetPoint(state, event);
        if (!point) {
            return;
        }

        updateSelectionFromPoints(state, state.dragStart, point);
        await notifySelection(state, true);
        draw(state);
    };

    state.onMouseUp = async () => {
        const gestureEnded = state.dragging || state.resizing;
        state.dragging = false;
        state.panning = false;
        state.dragStart = null;
        state.panStart = null;
        state.resizing = null;

        // The final report is what runs definition matching on the Blazor
        // side; the live reports during the gesture deliberately do not.
        if (gestureEnded) {
            await notifySelection(state, false);
        }
    };

    state.onWheel = (event) => {
        event.preventDefault();

        // Design-tool convention: a plain wheel or two-finger scroll pans in
        // both axes; a trackpad pinch (delivered as a ctrl-wheel) or an
        // explicit ctrl/cmd wheel zooms at the cursor.
        if (!event.ctrlKey && !event.metaKey) {
            state.offsetX -= event.deltaX;
            state.offsetY -= event.deltaY;
            draw(state);
            return;
        }

        const before = getWorldPoint(state, event);
        // Pinch events arrive as many tiny deltas, mouse wheels as one large
        // one; the per-event clamp keeps a single wheel tick from leaping.
        const factor = clamp(Math.exp(-event.deltaY * 0.005), 0.5, 2);
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
        loadStandaloneImage(url)
            .then(image => {
                state.image = image;
                state.imageLoaded = true;
                state.dotNetRef
                    .invokeMethodAsync("OnImageLoaded", image.width, image.height)
                    .catch(error => console.warn("Failed to report tilesheet size.", error));
                fitImage(state);
                draw(state);
                resolve();
            })
            .catch(() => reject(new Error(`Unable to load tilesheet image: ${url}`)));
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
    const x = Math.round(view.x + selection.x * state.tileSize * view.scale);
    const y = Math.round(view.y + selection.y * state.tileSize * view.scale);
    const width = Math.round(selection.width * state.tileSize * view.scale);
    const height = Math.round(selection.height * state.tileSize * view.scale);

    ctx.save();
    ctx.fillStyle = "rgba(255, 211, 91, 0.22)";
    ctx.strokeStyle = "#ffd35b";
    ctx.lineWidth = 2;
    ctx.fillRect(x, y, width, height);
    ctx.strokeRect(x + 1, y + 1, width - 2, height - 2);

    // Grab handles at the corners and edge midpoints signal that the bounds
    // are resizable in place.
    const half = 3;
    ctx.fillStyle = "#ffd35b";
    ctx.strokeStyle = "rgba(21, 25, 34, 0.9)";
    ctx.lineWidth = 1;
    for (const hx of [x, x + width / 2, x + width]) {
        for (const hy of [y, y + height / 2, y + height]) {
            if (hx === x + width / 2 && hy === y + height / 2) {
                continue;
            }

            ctx.fillRect(Math.round(hx) - half, Math.round(hy) - half, half * 2, half * 2);
            ctx.strokeRect(Math.round(hx) - half + 0.5, Math.round(hy) - half + 0.5, half * 2 - 1, half * 2 - 1);
        }
    }

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

/**
 * Like getSheetPoint, but clamps positions outside the sheet to the nearest
 * tile instead of returning null. A resize drag that wanders past the sheet
 * edge should pin the bound to the edge, not freeze.
 */
function getClampedSheetPoint(state, event) {
    if (!state.imageLoaded) {
        return null;
    }

    const world = getWorldPoint(state, event);
    return {
        x: clamp(Math.floor(world.x / state.tileSize), 0, Math.floor(state.image.width / state.tileSize) - 1),
        y: clamp(Math.floor(world.y / state.tileSize), 0, Math.floor(state.image.height / state.tileSize) - 1)
    };
}

// Screen-pixel reach of a selection handle. Generous enough to grab at a
// glance, small enough that the selection interior still starts a new
// rubber-band selection.
const HANDLE_GRAB_MARGIN = 7;

/**
 * Which edges of the current selection the cursor is gripping, or null when it
 * is not on a handle. Corners return two flags. Measured in screen pixels so
 * the grab area is constant at every zoom level.
 */
function getResizeHandleAt(state, event) {
    if (!state.imageLoaded) {
        return null;
    }

    const rect = state.canvas.getBoundingClientRect();
    const px = event.clientX - rect.left;
    const py = event.clientY - rect.top;

    const view = getView(state);
    const left = view.x + state.selection.x * state.tileSize * view.scale;
    const top = view.y + state.selection.y * state.tileSize * view.scale;
    const right = left + state.selection.width * state.tileSize * view.scale;
    const bottom = top + state.selection.height * state.tileSize * view.scale;

    const withinX = px >= left - HANDLE_GRAB_MARGIN && px <= right + HANDLE_GRAB_MARGIN;
    const withinY = py >= top - HANDLE_GRAB_MARGIN && py <= bottom + HANDLE_GRAB_MARGIN;
    if (!withinX || !withinY) {
        return null;
    }

    const handle = {
        w: Math.abs(px - left) <= HANDLE_GRAB_MARGIN,
        e: Math.abs(px - right) <= HANDLE_GRAB_MARGIN,
        n: Math.abs(py - top) <= HANDLE_GRAB_MARGIN,
        s: Math.abs(py - bottom) <= HANDLE_GRAB_MARGIN
    };

    return handle.n || handle.e || handle.s || handle.w ? handle : null;
}

/**
 * Moves the gripped edges to the tile under the cursor while the opposite
 * edges stay anchored. Dragging an edge across its anchor flips the rectangle
 * rather than jamming at one tile, matching how the rubber-band select feels.
 */
function applyResize(state, event) {
    const point = getClampedSheetPoint(state, event);
    if (!point) {
        return;
    }

    const { handle, anchorLeft, anchorTop, anchorRight, anchorBottom } = state.resizing;

    let left = anchorLeft;
    let right = anchorRight;
    if (handle.w) {
        left = Math.min(point.x, anchorRight);
        right = Math.max(point.x, anchorRight);
    } else if (handle.e) {
        left = Math.min(anchorLeft, point.x);
        right = Math.max(anchorLeft, point.x);
    }

    let top = anchorTop;
    let bottom = anchorBottom;
    if (handle.n) {
        top = Math.min(point.y, anchorBottom);
        bottom = Math.max(point.y, anchorBottom);
    } else if (handle.s) {
        top = Math.min(anchorTop, point.y);
        bottom = Math.max(anchorTop, point.y);
    }

    state.selection = {
        x: left,
        y: top,
        width: right - left + 1,
        height: bottom - top + 1
    };
}

/**
 * Resize affordance: the cursor telegraphs the grab before the user commits.
 */
function updateHoverCursor(state, event) {
    const handle = getResizeHandleAt(state, event);
    let cursor = "";
    if (handle) {
        if ((handle.n && handle.w) || (handle.s && handle.e)) {
            cursor = "nwse-resize";
        } else if ((handle.n && handle.e) || (handle.s && handle.w)) {
            cursor = "nesw-resize";
        } else if (handle.n || handle.s) {
            cursor = "ns-resize";
        } else {
            cursor = "ew-resize";
        }
    }

    if (state.canvas.style.cursor !== cursor) {
        state.canvas.style.cursor = cursor;
    }
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

/**
 * Reports the selection to Blazor. Live reports keep the side panel's numbers
 * current during a gesture; the final report (live = false, sent on release)
 * is the one allowed to match and load a saved definition.
 */
async function notifySelection(state, live) {
    await state.dotNetRef.invokeMethodAsync(
        "OnSelectionChanged",
        state.selection.x,
        state.selection.y,
        state.selection.width,
        state.selection.height,
        !!live);
}
