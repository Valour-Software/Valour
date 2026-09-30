const PICKER_RENDER_RETRY_MS = 75;
const MAX_PICKER_RENDER_ATTEMPTS = 80;

const pickerStates = new Map();

let dataInitPromise = null;
let libraryLoadPromise = null;
let stylesheetPromise = null;

// Valour's look for the picker. emoji-mart renders into a shadow root, so page styles
// can't reach it; the sheet is adopted into each picker's shadow root instead. It uses
// the page's design tokens, which inherit through the shadow boundary, so it follows
// the active theme.
const STYLESHEET_URL = new URL('../../../css/emoji-picker.css', import.meta.url);

// Bootstrap Icons (MIT) paths for emoji-mart's built-in categories. 'custom' is the
// planet category, shown when the planet has no icon of its own.
const CATEGORY_ICON_PATHS = {
    frequent: ['M8.515 1.019A7 7 0 0 0 8 1V0a8 8 0 0 1 .589.022l-.074.997zm2.004.45a7.003 7.003 0 0 0-.985-.299l.219-.976c.383.086.76.2 1.126.342l-.36.933zm1.37.71a7.01 7.01 0 0 0-.439-.27l.493-.87a8.025 8.025 0 0 1 .979.654l-.615.789a6.996 6.996 0 0 0-.418-.302zm1.834 1.79a6.99 6.99 0 0 0-.653-.796l.724-.69c.27.285.52.59.747.91l-.818.576zm.744 1.352a7.08 7.08 0 0 0-.214-.468l.893-.45a7.976 7.976 0 0 1 .45 1.088l-.95.313a7.023 7.023 0 0 0-.179-.483zm.53 2.507a6.991 6.991 0 0 0-.1-1.025l.985-.17c.067.386.106.778.116 1.17l-1 .025zm-.131 1.538c.033-.17.06-.339.081-.51l.993.123a7.957 7.957 0 0 1-.23 1.155l-.964-.267c.046-.165.086-.332.12-.501zm-.952 2.379c.184-.29.346-.594.486-.908l.914.405c-.16.36-.345.706-.555 1.038l-.845-.535zm-.964 1.205c.122-.122.239-.248.35-.378l.758.653a8.073 8.073 0 0 1-.401.432l-.707-.707z', 'M8 1a7 7 0 1 0 4.95 11.95l.707.707A8.001 8.001 0 1 1 8 0v1z', 'M7.5 3a.5.5 0 0 1 .5.5v5.21l3.248 1.856a.5.5 0 0 1-.496.868l-3.5-2A.5.5 0 0 1 7 9V3.5a.5.5 0 0 1 .5-.5z'],
    people: ['M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z', 'M4.285 9.567a.5.5 0 0 1 .683.183A3.498 3.498 0 0 0 8 11.5a3.498 3.498 0 0 0 3.032-1.75.5.5 0 1 1 .866.5A4.498 4.498 0 0 1 8 12.5a4.498 4.498 0 0 1-3.898-2.25.5.5 0 0 1 .183-.683zM7 6.5C7 7.328 6.552 8 6 8s-1-.672-1-1.5S5.448 5 6 5s1 .672 1 1.5zm4 0c0 .828-.448 1.5-1 1.5s-1-.672-1-1.5S9.448 5 10 5s1 .672 1 1.5z'],
    nature: ['M8.416.223a.5.5 0 0 0-.832 0l-3 4.5A.5.5 0 0 0 5 5.5h.098L3.076 8.735A.5.5 0 0 0 3.5 9.5h.191l-1.638 3.276a.5.5 0 0 0 .447.724H7V16h2v-2.5h4.5a.5.5 0 0 0 .447-.724L12.31 9.5h.191a.5.5 0 0 0 .424-.765L10.902 5.5H11a.5.5 0 0 0 .416-.777l-3-4.5zM6.437 4.758A.5.5 0 0 0 6 4.5h-.066L8 1.401 10.066 4.5H10a.5.5 0 0 0-.424.765L11.598 8.5H11.5a.5.5 0 0 0-.447.724L12.69 12.5H3.309l1.638-3.276A.5.5 0 0 0 4.5 8.5h-.098l2.022-3.235a.5.5 0 0 0 .013-.507z'],
    foods: ['M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6z', 'M13.997 5.17a5 5 0 0 0-8.101-4.09A5 5 0 0 0 1.28 9.342a5 5 0 0 0 8.336 5.109 3.5 3.5 0 0 0 5.201-4.065 3.001 3.001 0 0 0-.822-5.216zm-1-.034a1 1 0 0 0 .668.977 2.001 2.001 0 0 1 .547 3.478 1 1 0 0 0-.341 1.113 2.5 2.5 0 0 1-3.715 2.905 1 1 0 0 0-1.262.152 4 4 0 0 1-6.67-4.087 1 1 0 0 0-.2-1 4 4 0 0 1 3.693-6.61 1 1 0 0 0 .8-.2 4 4 0 0 1 6.48 3.273z'],
    activity: ['M2.5.5A.5.5 0 0 1 3 0h10a.5.5 0 0 1 .5.5c0 .538-.012 1.05-.034 1.536a3 3 0 1 1-1.133 5.89c-.79 1.865-1.878 2.777-2.833 3.011v2.173l1.425.356c.194.048.377.135.537.255L13.3 15.1a.5.5 0 0 1-.3.9H3a.5.5 0 0 1-.3-.9l1.838-1.379c.16-.12.343-.207.537-.255L6.5 13.11v-2.173c-.955-.234-2.043-1.146-2.833-3.012a3 3 0 1 1-1.132-5.89A33.076 33.076 0 0 1 2.5.5zm.099 2.54a2 2 0 0 0 .72 3.935c-.333-1.05-.588-2.346-.72-3.935zm10.083 3.935a2 2 0 0 0 .72-3.935c-.133 1.59-.388 2.885-.72 3.935zM3.504 1c.007.517.026 1.006.056 1.469.13 2.028.457 3.546.87 4.667C5.294 9.48 6.484 10 7 10a.5.5 0 0 1 .5.5v2.61a1 1 0 0 1-.757.97l-1.426.356a.5.5 0 0 0-.179.085L4.5 15h7l-.638-.479a.501.501 0 0 0-.18-.085l-1.425-.356a1 1 0 0 1-.757-.97V10.5A.5.5 0 0 1 9 10c.516 0 1.706-.52 2.57-2.864.413-1.12.74-2.64.87-4.667.03-.463.049-.952.056-1.469H3.504z'],
    places: ['M6.428 1.151C6.708.591 7.213 0 8 0s1.292.592 1.572 1.151C9.861 1.73 10 2.431 10 3v3.691l5.17 2.585a1.5 1.5 0 0 1 .83 1.342V12a.5.5 0 0 1-.582.493l-5.507-.918-.375 2.253 1.318 1.318A.5.5 0 0 1 10.5 16h-5a.5.5 0 0 1-.354-.854l1.319-1.318-.376-2.253-5.507.918A.5.5 0 0 1 0 12v-1.382a1.5 1.5 0 0 1 .83-1.342L6 6.691V3c0-.568.14-1.271.428-1.849Zm.894.448C7.111 2.02 7 2.569 7 3v4a.5.5 0 0 1-.276.447l-5.448 2.724a.5.5 0 0 0-.276.447v.792l5.418-.903a.5.5 0 0 1 .575.41l.5 3a.5.5 0 0 1-.14.437L6.708 15h2.586l-.647-.646a.5.5 0 0 1-.14-.436l.5-3a.5.5 0 0 1 .576-.411L15 11.41v-.792a.5.5 0 0 0-.276-.447L9.276 7.447A.5.5 0 0 1 9 7V3c0-.432-.11-.979-.322-1.401C8.458 1.159 8.213 1 8 1c-.213 0-.458.158-.678.599Z'],
    objects: ['M2 6a6 6 0 1 1 10.174 4.31c-.203.196-.359.4-.453.619l-.762 1.769A.5.5 0 0 1 10.5 13a.5.5 0 0 1 0 1 .5.5 0 0 1 0 1l-.224.447a1 1 0 0 1-.894.553H6.618a1 1 0 0 1-.894-.553L5.5 15a.5.5 0 0 1 0-1 .5.5 0 0 1 0-1 .5.5 0 0 1-.46-.302l-.761-1.77a1.964 1.964 0 0 0-.453-.618A5.984 5.984 0 0 1 2 6zm6-5a5 5 0 0 0-3.479 8.592c.263.254.514.564.676.941L5.83 12h4.342l.632-1.467c.162-.377.413-.687.676-.941A5 5 0 0 0 8 1z'],
    symbols: ['m8 2.748-.717-.737C5.6.281 2.514.878 1.4 3.053c-.523 1.023-.641 2.5.314 4.385.92 1.815 2.834 3.989 6.286 6.357 3.452-2.368 5.365-4.542 6.286-6.357.955-1.886.838-3.362.314-4.385C13.486.878 10.4.28 8.717 2.01L8 2.748zM8 15C-7.333 4.868 3.279-3.04 7.824 1.143c.06.055.119.112.176.171a3.12 3.12 0 0 1 .176-.17C12.72-3.042 23.333 4.867 8 15z'],
    flags: ['M14.778.085A.5.5 0 0 1 15 .5V8a.5.5 0 0 1-.314.464L14.5 8l.186.464-.003.001-.006.003-.023.009a12.435 12.435 0 0 1-.397.15c-.264.095-.631.223-1.047.35-.816.252-1.879.523-2.71.523-.847 0-1.548-.28-2.158-.525l-.028-.01C7.68 8.71 7.14 8.5 6.5 8.5c-.7 0-1.638.23-2.437.477A19.626 19.626 0 0 0 3 9.342V15.5a.5.5 0 0 1-1 0V.5a.5.5 0 0 1 1 0v.282c.226-.079.496-.17.79-.26C4.606.272 5.67 0 6.5 0c.84 0 1.524.277 2.121.519l.043.018C9.286.788 9.828 1 10.5 1c.7 0 1.638-.23 2.437-.477a19.587 19.587 0 0 0 1.349-.476l.019-.007.004-.002h.001M14 1.221c-.22.078-.48.167-.766.255-.81.252-1.872.523-2.734.523-.886 0-1.592-.286-2.203-.534l-.008-.003C7.662 1.21 7.139 1 6.5 1c-.669 0-1.606.229-2.415.478A21.294 21.294 0 0 0 3 1.845v6.433c.22-.078.48-.167.766-.255C4.576 7.77 5.638 7.5 6.5 7.5c.847 0 1.548.28 2.158.525l.028.01C9.32 8.29 9.86 8.5 10.5 8.5c.668 0 1.606-.229 2.415-.478A21.317 21.317 0 0 0 14 7.655V1.222z'],
    custom: ['M7.657 6.247c.11-.33.576-.33.686 0l.645 1.937a2.89 2.89 0 0 0 1.829 1.828l1.936.645c.33.11.33.576 0 .686l-1.937.645a2.89 2.89 0 0 0-1.828 1.829l-.645 1.936a.361.361 0 0 1-.686 0l-.645-1.937a2.89 2.89 0 0 0-1.828-1.828l-1.937-.645a.361.361 0 0 1 0-.686l1.937-.645a2.89 2.89 0 0 0 1.828-1.828l.645-1.937zM3.794 1.148a.217.217 0 0 1 .412 0l.387 1.162c.173.518.579.924 1.097 1.097l1.162.387a.217.217 0 0 1 0 .412l-1.162.387A1.734 1.734 0 0 0 4.593 5.69l-.387 1.162a.217.217 0 0 1-.412 0L3.407 5.69A1.734 1.734 0 0 0 2.31 4.593l-1.162-.387a.217.217 0 0 1 0-.412l1.162-.387A1.734 1.734 0 0 0 3.407 2.31l.387-1.162zM10.863.099a.145.145 0 0 1 .274 0l.258.774c.115.346.386.617.732.732l.774.258a.145.145 0 0 1 0 .274l-.774.258a1.156 1.156 0 0 0-.732.732l-.258.774a.145.145 0 0 1-.274 0l-.258-.774a1.156 1.156 0 0 0-.732-.732L9.1 2.137a.145.145 0 0 1 0-.274l.774-.258c.346-.115.617-.386.732-.732L10.863.1z'],
};

const CATEGORY_ICONS = Object.fromEntries(Object.entries(CATEGORY_ICON_PATHS).map(([id, paths]) => [id, {
    svg: `<svg xmlns="http://www.w3.org/2000/svg" fill="currentColor" viewBox="0 0 16 16">${paths.map(d => `<path d="${d}"/>`).join('')}</svg>`,
}]));

function loadStylesheet() {
    stylesheetPromise ??= fetch(STYLESHEET_URL)
        .then(response => response.ok ? response.text() : Promise.reject(new Error(`Failed to load ${STYLESHEET_URL}`)))
        .then(css => {
            const sheet = new CSSStyleSheet();
            sheet.replaceSync(css);
            return sheet;
        })
        .catch(() => {
            // The picker still works with its default look; allow a later render to retry
            stylesheetPromise = null;
            return null;
        });

    return stylesheetPromise;
}

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

// Emoji are drawn by the system's emoji font, so pickers use emoji-mart's
// 'native' set.
const EMOJI_SET = 'native';

// emoji-mart keeps a single global dataset shared by every picker and the search
// index, and it must be initialized exactly once. Calling globalThis.EmojiMart.init
// concurrently with picker construction races two fetches against each other and
// whichever resolves last wins, so callers must await this before constructing a
// picker.
async function ensureDataInitialized() {
    await ensureLibraryLoaded();

    if (dataInitPromise === null) {
        dataInitPromise = globalThis.EmojiMart.init({ set: EMOJI_SET });
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
    await ensureDataInitialized();

    const stylesheet = await loadStylesheet();

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
        set: EMOJI_SET,
        theme: 'dark',
        categoryIcons: { ...CATEGORY_ICONS },
        emojiButtonRadius: 'var(--radius-md)',
    };

    const mobile = wrapper.closest('.mobile') !== null;

    // On mobile the input picker should span the full screen width. The width
    // is set inline on #root inside the shadow DOM, so it can't be overridden
    // from outside CSS — dynamicWidth makes emoji-mart use width: 100% and
    // derive perLine from the host's size instead (host is sized in CSS).
    // Scoped to the input wrapper so the reaction picker keeps its fixed size.
    if (mobile && wrapper.classList.contains('emoji-mart-wrapper-custom')) {
        pickerOptions.dynamicWidth = true;
    }

    // Touch has no hover, so the preview bar would only ever say "Pick an emoji".
    // Give its space to the grid and move the skin tone button next to search.
    if (mobile) {
        pickerOptions.previewPosition = 'none';
        pickerOptions.skinTonePosition = 'search';
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
            pickerOptions.categoryIcons.custom = {
                src: state.customCategoryIcon,
            };
        }
    }

    const picker = new globalThis.EmojiMart.Picker(pickerOptions);
    if (stylesheet !== null) {
        picker.shadowRoot.adoptedStyleSheets = [...picker.shadowRoot.adoptedStyleSheets, stylesheet];
    }
    wrapper.innerHTML = '';
    wrapper.appendChild(picker);
    state.picker = picker;
}

export function init(id, ref, custom = [], customCategoryIcon = '', customCategoryName = 'Planet') {
    const state = {
        id,
        ref,
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
