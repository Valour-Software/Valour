let searchInitialized = false;
const pickerStates = new Map();

function ensureSearchInitialized() {
    if (searchInitialized) {
        return;
    }

    if (typeof EmojiMart?.init === 'function') {
        EmojiMart.init({});
    }

    searchInitialized = true;
}

function normalizeNativeResult(entry) {
    const emoji = entry?.emoji ?? entry;
    const skin = emoji?.skins?.[0] ?? {};

    const aliases = Array.isArray(emoji?.aliases) ? emoji.aliases : [];
    const keywords = Array.isArray(emoji?.keywords) ? emoji.keywords : [];

    const id = emoji?.id ?? '';
    const shortcodes = emoji?.shortcodes ?? (id ? `:${id}:` : null);

    const native = skin.native ?? emoji?.native ?? '';
    const unified = skin.unified ?? emoji?.unified ?? '';

    if (!native || !unified) {
        return null;
    }

    return {
        aliases,
        id,
        keywords,
        name: emoji?.name ?? id,
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

function normalizeCustomResult(entry) {
    const emoji = entry?.emoji ?? entry;
    const skin = emoji?.skins?.[0] ?? {};

    const src = skin.src ?? emoji?.src ?? '';
    if (!src) {
        return null;
    }

    const aliases = Array.isArray(emoji?.aliases) ? emoji.aliases : [];
    const keywords = Array.isArray(emoji?.keywords) ? emoji.keywords : [];
    const id = emoji?.id ?? '';
    const shortcodes = emoji?.shortcodes ?? (id ? `:${id}:` : '');
    const token = emoji?.token ?? '';
    const customId = normalizeCustomId(emoji?.customId);

    return {
        aliases,
        id,
        keywords,
        name: emoji?.name ?? id,
        native: token || shortcodes || (id ? `:${id}:` : ''),
        unified: '',
        shortcodes,
        isCustom: true,
        customId,
        token,
        src,
    };
}

function normalizeEmojiResult(entry) {
    return normalizeNativeResult(entry) ?? normalizeCustomResult(entry);
}

function toPickerCustom(entry) {
    const id = entry?.id ?? entry?.name ?? '';
    const src = entry?.src ?? '';
    if (!id || !src) {
        return null;
    }

    const keywords = Array.isArray(entry?.keywords) ? entry.keywords : [];
    const shortcodes = entry?.shortcodes ?? `:${id}:`;
    const token = entry?.token ?? '';
    const customId = normalizeCustomId(entry?.customId);

    return {
        id,
        name: entry?.name ?? id,
        keywords,
        shortcodes,
        customId,
        token,
        skins: [{ src }],
    };
}

function renderPicker(state) {
    const wrapper = document.getElementById(state.id);
    if (!wrapper) {
        return;
    }

    const pickerOptions = {
        onEmojiSelect: e => onEmojiSelect(state.ref, e),
        onClickOutside: e => onClickOutside(state.ref, e),
        set: state.emojiSet,
        theme: 'dark',
    };

    const custom = (state.custom ?? [])
        .map(toPickerCustom)
        .filter(x => x !== null);

    if (custom.length > 0) {
        pickerOptions.custom = custom;
    }

    const picker = new EmojiMart.Picker(pickerOptions);
    wrapper.innerHTML = '';
    wrapper.appendChild(picker);
    state.picker = picker;
}

export function init(id, ref, emojiSet, custom = []) {
    ensureSearchInitialized();

    const state = {
        id,
        ref,
        emojiSet,
        custom: Array.isArray(custom) ? custom : [],
        picker: null,
    };

    pickerStates.set(id, state);
    renderPicker(state);
}

export function setCustom(id, custom = []) {
    const state = pickerStates.get(id);
    if (!state) {
        return;
    }

    state.custom = Array.isArray(custom) ? custom : [];
    renderPicker(state);
}

export async function search(query, maxResults = 10) {
    ensureSearchInitialized();

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

export function onEmojiSelect(ref, e) {
    const normalized = normalizeEmojiResult(e);
    if (normalized !== null) {
        ref.invokeMethodAsync('EmojiClick', normalized);
    }
}

export function onClickOutside(ref, e) {
    ref.invokeMethodAsync('ClickOutside', { target: e.target?.id });
}
