const PICKER_RENDER_RETRY_MS = 75;
const MAX_PICKER_RENDER_ATTEMPTS = 80;

let searchInitialized = false;
const pickerStates = new Map();

function ensureSearchInitialized() {
    if (searchInitialized) {
        return true;
    }

    if (typeof EmojiMart?.init !== 'function') {
        return false;
    }

    EmojiMart.init({});
    searchInitialized = true;
    return true;
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

function renderPicker(state) {
    const wrapper = document.getElementById(state.id);
    if (!wrapper) {
        return;
    }

    if (typeof EmojiMart?.Picker !== 'function') {
        if (state.renderAttempts < MAX_PICKER_RENDER_ATTEMPTS) {
            state.renderAttempts += 1;
            scheduleRenderPicker(state);
        }
        return;
    }

    state.renderAttempts = 0;
    ensureSearchInitialized();
    rebuildCustomLookup(state);

    const pickerOptions = {
        onEmojiSelect: e => onEmojiSelect(state.id, state.ref, e),
        onClickOutside: e => onClickOutside(state.ref, e),
        set: state.emojiSet,
        theme: 'dark',
    };

    const custom = state.normalizedCustom
        .map(toPickerCustom)
        .filter(x => x !== null);

    if (custom.length > 0) {
        const categoryId = 'custom';
        const categoryName = asNonEmptyString(state.customCategoryName, 'Planet');

        pickerOptions.custom = [{
            id: categoryId,
            name: categoryName,
            emojis: custom,
        }];

        pickerOptions.categories = [
            'frequent',
            'people',
            'nature',
            'foods',
            'activity',
            'places',
            'objects',
            'symbols',
            'flags',
            'custom',
        ];

        if (state.customCategoryIcon) {
            pickerOptions.categoryIcons = {
                custom: {
                    src: state.customCategoryIcon,
                },
            };
        }
    }

    const picker = new EmojiMart.Picker(pickerOptions);
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
    if (!ensureSearchInitialized()) {
        return [];
    }

    const cleanQuery = (query ?? '').trim().replace(/^:/, '');
    if (!cleanQuery || !EmojiMart?.SearchIndex?.search) {
        return [];
    }

    try {
        let results = [];

        try {
            results = await EmojiMart.SearchIndex.search(cleanQuery, { maxResults });
        } catch (_) {
            results = await EmojiMart.SearchIndex.search(cleanQuery);
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
