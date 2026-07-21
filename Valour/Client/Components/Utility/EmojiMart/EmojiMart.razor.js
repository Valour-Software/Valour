const PICKER_RENDER_RETRY_MS = 75;
const MAX_PICKER_RENDER_ATTEMPTS = 80;

const pickerStates = new Map();

let dataInitPromise = null;
let libraryLoadPromise = null;

function ensureLibraryLoaded() {
    if (typeof globalThis.EmojiMart?.init === 'function') {
        return Promise.resolve();
    }

    libraryLoadPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/emoji-mart@5.6.0/dist/browser.js?v=5.6.0';
        script.async = true;
        script.onload = resolve;
        script.onerror = () => reject(new Error('Failed to load emoji-mart'));
        document.head.appendChild(script);
    });

    return libraryLoadPromise;
}

// emoji-mart keeps a single global dataset shared by every picker and the search
// index. It must be initialized exactly once, with the correct emoji set: spritesheet
// sets (e.g. 'twitter') need the x/y coordinates that the 'native' dataset lacks.
// Calling globalThis.EmojiMart.init concurrently with picker construction races two fetches
// against each other and whichever resolves last wins, so callers must await this
// before constructing a picker.
async function ensureDataInitialized(set = null) {
    await ensureLibraryLoaded();

    if (dataInitPromise === null) {
        dataInitPromise = globalThis.EmojiMart.init({ set: set ?? 'twitter' });
    }

    await dataInitPromise;
}

function asNonEmptyString(...values) {
    for (const value of values) {
        if (typeof value === 'string' && value.trim().length > 0) {
            return value;
        }
    }

    return '';
}

function asStringArray(value) {
    if (!Array.isArray(value)) {
        return [];
    }

    return value.filter(x => typeof x === 'string' && x.length > 0);
}

function normalizeNativeResult(entry) {
    const emoji = entry?.emoji ?? entry?.Emoji ?? entry;
    const skin = emoji?.skins?.[0] ?? emoji?.Skins?.[0] ?? {};

    const aliases = asStringArray(emoji?.aliases ?? emoji?.Aliases);
    const keywords = asStringArray(emoji?.keywords ?? emoji?.Keywords);

    const id = asNonEmptyString(emoji?.id, emoji?.Id);
    const shortcodes = asNonEmptyString(emoji?.shortcodes, emoji?.Shortcodes, (id ? `:${id}:` : ''));

    const native = asNonEmptyString(skin?.native, skin?.Native, emoji?.native, emoji?.Native);
    const unified = asNonEmptyString(skin?.unified, skin?.Unified, emoji?.unified, emoji?.Unified);

    if (!native || !unified) {
        return null;
    }

    return {
        aliases,
        id,
        keywords,
        name: asNonEmptyString(emoji?.name, emoji?.Name, id),
        native,
        unified,
        shortcodes,
        isCustom: false,
        customId: null,
        token: null,
        src: null,
    };
}

function normalizeCustomId(rawCustomId) {
    if (typeof rawCustomId === 'number' && Number.isFinite(rawCustomId)) {
        return rawCustomId;
    }

    if (typeof rawCustomId === 'string') {
        const parsed = Number.parseInt(rawCustomId, 10);
        if (Number.isFinite(parsed)) {
            return parsed;
        }
    }

    return null;
}

function toCustomMetadata(entry) {
    const id = asNonEmptyString(entry?.id, entry?.Id, entry?.name, entry?.Name);
    const src = asNonEmptyString(entry?.src, entry?.Src);

    if (!id || !src) {
        return null;
    }

    return {
        aliases: asStringArray(entry?.aliases ?? entry?.Aliases),
        id,
        keywords: asStringArray(entry?.keywords ?? entry?.Keywords),
        name: asNonEmptyString(entry?.name, entry?.Name, id),
        shortcodes: asNonEmptyString(entry?.shortcodes, entry?.Shortcodes, `:${id}:`),
        customId: normalizeCustomId(entry?.customId ?? entry?.CustomId),
        token: asNonEmptyString(entry?.token, entry?.Token),
        src,
    };
}

function rebuildCustomLookup(state) {
    state.normalizedCustom = [];
    state.customByPickerId.clear();
    state.customById.clear();
    state.customBySrc.clear();

    const entries = Array.isArray(state.custom) ? state.custom : [];
    let fallbackIndex = 0;
    for (const entry of entries) {
        const metadata = toCustomMetadata(entry);
        if (metadata === null) {
            continue;
        }

        const pickerId = metadata.customId !== null
            ? `planet-${metadata.customId}`
            : `planet-custom-${fallbackIndex++}-${metadata.id}`;

        metadata.pickerId = pickerId;
        state.normalizedCustom.push(metadata);
        state.customByPickerId.set(metadata.pickerId, metadata);
        state.customById.set(metadata.id, metadata);
        state.customBySrc.set(metadata.src, metadata);
    }
}

function normalizeCustomResult(entry, state) {
    const emoji = entry?.emoji ?? entry?.Emoji ?? entry;
    const skin = emoji?.skins?.[0] ?? emoji?.Skins?.[0] ?? {};

    const id = asNonEmptyString(emoji?.id, emoji?.Id, entry?.id, entry?.Id);
    const src = asNonEmptyString(skin?.src, skin?.Src, emoji?.src, emoji?.Src, entry?.src, entry?.Src);

    let mapped = null;
    if (state !== null) {
        if (id) {
            mapped = state.customByPickerId.get(id) ?? state.customById.get(id) ?? null;
        }

        if (mapped === null && src) {
            mapped = state.customBySrc.get(src) ?? null;
        }
    }

    const resolvedId = asNonEmptyString(mapped?.id, id);
    const resolvedSrc = asNonEmptyString(src, mapped?.src);
    if (!resolvedId || !resolvedSrc) {
        return null;
    }

    const aliases = asStringArray(emoji?.aliases ?? emoji?.Aliases ?? entry?.aliases ?? entry?.Aliases);
    const keywords = asStringArray(emoji?.keywords ?? emoji?.Keywords ?? entry?.keywords ?? entry?.Keywords);
    const shortcodes = asNonEmptyString(
        emoji?.shortcodes,
        emoji?.Shortcodes,
        entry?.shortcodes,
        entry?.Shortcodes,
        mapped?.shortcodes,
        `:${resolvedId}:`);
    const token = asNonEmptyString(emoji?.token, emoji?.Token, entry?.token, entry?.Token, mapped?.token);
    const customId = normalizeCustomId(emoji?.customId ?? emoji?.CustomId ?? entry?.customId ?? entry?.CustomId ?? mapped?.customId);

    return {
        aliases: aliases.length > 0 ? aliases : (mapped?.aliases ?? []),
        id: resolvedId,
        keywords: keywords.length > 0 ? keywords : (mapped?.keywords ?? []),
        name: asNonEmptyString(emoji?.name, emoji?.Name, entry?.name, entry?.Name, mapped?.name, resolvedId),
        native: token || shortcodes,
        unified: '',
        shortcodes,
        isCustom: true,
        customId,
        token,
        src: resolvedSrc,
    };
}

function normalizeEmojiResult(entry, state = null) {
    return normalizeNativeResult(entry) ?? normalizeCustomResult(entry, state);
}

function toPickerCustom(entry) {
    return {
        id: entry.pickerId ?? entry.id,
        name: entry.name,
        keywords: entry.keywords,
        shortcodes: entry.shortcodes,
        customId: entry.customId,
        token: entry.token,
        skins: [{ src: entry.src }],
    };
}

function scheduleRenderPicker(state) {
    if (state.renderTimer !== null) {
        return;
    }

    state.renderTimer = window.setTimeout(() => {
        state.renderTimer = null;
        renderPicker(state);
    }, PICKER_RENDER_RETRY_MS);
}

async function renderPicker(state) {
    try {
        await ensureLibraryLoaded();
    } catch (_) {
        return;
    }

    if (typeof globalThis.EmojiMart?.Picker !== 'function') {
        if (state.renderAttempts < MAX_PICKER_RENDER_ATTEMPTS) {
            state.renderAttempts += 1;
            scheduleRenderPicker(state);
        }
        return;
    }

    state.renderAttempts = 0;

    // The picker's own connectedCallback also calls globalThis.EmojiMart.init with its props;
    // awaiting here guarantees the dataset already exists by then, so that call takes
    // the cheap "already initialized" path instead of racing a second data fetch.
    await ensureDataInitialized(state.emojiSet);

    // The picker may have been destroyed while waiting for the dataset
    if (!pickerStates.has(state.id)) {
        return;
    }

    const wrapper = document.getElementById(state.id);
    if (!wrapper) {
        return;
    }

    rebuildCustomLookup(state);

    const pickerOptions = {
        onEmojiSelect: e => onEmojiSelect(state.id, state.ref, e),
        onClickOutside: e => onClickOutside(state.ref, e),
        set: state.emojiSet,
        theme: 'dark',
    };

    // On mobile the input picker should span the full screen width. The width
    // is set inline on #root inside the shadow DOM, so it can't be overridden
    // from outside CSS — dynamicWidth makes emoji-mart use width: 100% and
    // derive perLine from the host's size instead (host is sized in CSS).
    // Scoped to the input wrapper so the reaction picker keeps its fixed size.
    if (wrapper.classList.contains('emoji-mart-wrapper-custom') && wrapper.closest('.mobile') !== null) {
        pickerOptions.dynamicWidth = true;
    }

    const custom = state.normalizedCustom
        .map(toPickerCustom)
        .filter(x => x !== null);

    if (custom.length > 0) {
        const categoryId = 'custom';
        const categoryName = asNonEmptyString(state.customCategoryName, 'Planet');

        // Note: do NOT pass pickerOptions.categories here. emoji-mart rebuilds the
        // category list from its original native-only categories when that option is
        // set, which silently strips any custom categories. Custom categories are
        // appended at the end by default, which is what we want anyway.
        pickerOptions.custom = [{
            id: categoryId,
            name: categoryName,
            emojis: custom,
        }];

        if (state.customCategoryIcon) {
            pickerOptions.categoryIcons = {
                custom: {
                    src: state.customCategoryIcon,
                },
            };
        }
    }

    const picker = new globalThis.EmojiMart.Picker(pickerOptions);
    wrapper.innerHTML = '';
    wrapper.appendChild(picker);
    state.picker = picker;
}

export function init(id, ref, emojiSet, custom = [], customCategoryIcon = '', customCategoryName = 'Planet') {
    const state = {
        id,
        ref,
        emojiSet,
        custom: Array.isArray(custom) ? custom : [],
        customCategoryId: 'custom',
        customCategoryIcon: asNonEmptyString(customCategoryIcon),
        customCategoryName: asNonEmptyString(customCategoryName, 'Planet'),
        normalizedCustom: [],
        customByPickerId: new Map(),
        customById: new Map(),
        customBySrc: new Map(),
        picker: null,
        renderAttempts: 0,
        renderTimer: null,
    };

    pickerStates.set(id, state);
    rebuildCustomLookup(state);
    renderPicker(state);
}

export function setCustom(id, custom = [], customCategoryIcon = '', customCategoryName = 'Planet') {
    const state = pickerStates.get(id);
    if (!state) {
        return;
    }

    state.custom = Array.isArray(custom) ? custom : [];
    state.customCategoryIcon = asNonEmptyString(customCategoryIcon);
    state.customCategoryName = asNonEmptyString(customCategoryName, 'Planet');
    rebuildCustomLookup(state);
    renderPicker(state);
}

export async function search(query, maxResults = 10) {
    const ready = ensureDataInitialized();
    try { await ready; } catch (_) { return []; }

    const cleanQuery = (query ?? '').trim().replace(/^:/, '');
    if (!cleanQuery || !globalThis.EmojiMart?.SearchIndex?.search) {
        return [];
    }

    try {
        let results = [];

        try {
            results = await globalThis.EmojiMart.SearchIndex.search(cleanQuery, { maxResults });
        } catch (_) {
            results = await globalThis.EmojiMart.SearchIndex.search(cleanQuery);
        }

        if (!Array.isArray(results)) {
            return [];
        }

        return results
            .slice(0, maxResults)
            .map(normalizeNativeResult)
            .filter(x => x !== null);
    } catch (_) {
        return [];
    }
}

const FREQUENTLY_STORAGE_KEY = 'emoji-mart.frequently';
const DEFAULT_FREQUENT_IDS = ['+1', 'heart', 'joy', 'open_mouth', 'cry', 'fire'];

function readFrequentlyStore() {
    try {
        const raw = window.localStorage.getItem(FREQUENTLY_STORAGE_KEY);
        const parsed = raw ? JSON.parse(raw) : null;
        return (parsed && typeof parsed === 'object') ? parsed : {};
    } catch (_) {
        return {};
    }
}

// Returns the user's most frequently used emojis from emoji-mart's own store,
// padded with defaults when there isn't enough history.
export async function getFrequent(maxResults = 6) {
    const ready = ensureDataInitialized();
    try { await ready; } catch (_) { return []; }
    if (typeof globalThis.EmojiMart?.SearchIndex?.get !== 'function') return [];

    const frequently = readFrequentlyStore();

    const ids = Object.entries(frequently)
        .filter(([, count]) => typeof count === 'number')
        .sort((a, b) => b[1] - a[1])
        .map(([id]) => id);

    for (const fallback of DEFAULT_FREQUENT_IDS) {
        if (ids.length >= maxResults) {
            break;
        }
        if (!ids.includes(fallback)) {
            ids.push(fallback);
        }
    }

    const results = [];
    for (const id of ids) {
        if (results.length >= maxResults) {
            break;
        }

        try {
            const emoji = await globalThis.EmojiMart.SearchIndex.get(id);
            const normalized = normalizeNativeResult(emoji);
            if (normalized !== null) {
                results.push(normalized);
            }
        } catch (_) {
            // Skip ids that can't be resolved (e.g. custom planet emojis)
        }
    }

    return results;
}

// Mirrors emoji-mart's own frequency tracking for reactions added outside the picker
export function recordFrequent(id) {
    if (typeof id !== 'string' || id.length === 0) {
        return;
    }

    try {
        const frequently = readFrequentlyStore();
        frequently[id] = (typeof frequently[id] === 'number' ? frequently[id] : 0) + 1;
        window.localStorage.setItem(FREQUENTLY_STORAGE_KEY, JSON.stringify(frequently));
        window.localStorage.setItem('emoji-mart.last', JSON.stringify(id));
    } catch (_) {
        // Storage unavailable - non-critical
    }
}

export function onEmojiSelect(id, ref, e) {
    const state = pickerStates.get(id) ?? null;
    const normalized = normalizeEmojiResult(e, state);
    if (normalized !== null) {
        ref.invokeMethodAsync('EmojiClick', normalized);
    }
}

export function onClickOutside(ref, e) {
    ref.invokeMethodAsync('ClickOutside', { target: e.target?.id });
}

export function destroy(id) {
    const state = pickerStates.get(id);
    if (!state) {
        return;
    }

    if (state.renderTimer !== null) {
        window.clearTimeout(state.renderTimer);
        state.renderTimer = null;
    }

    pickerStates.delete(id);
}
