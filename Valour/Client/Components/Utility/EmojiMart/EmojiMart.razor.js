let searchInitialized = false;

function ensureSearchInitialized() {
    if (searchInitialized) {
        return;
    }

    if (typeof EmojiMart?.init === 'function') {
        EmojiMart.init({});
    }

    searchInitialized = true;
}

function normalizeEmojiResult(entry) {
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
    };
}

export function init(id, ref, emojiSet) {
    ensureSearchInitialized();

    const pickerOptions = {
        onEmojiSelect: e => onEmojiSelect(ref, e),
        onClickOutside: e => onClickOutside(ref, e),
        set: emojiSet,
        theme: 'dark',
    };

    const picker = new EmojiMart.Picker(pickerOptions);
    const wrapper = document.getElementById(id);
    wrapper.appendChild(picker);
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
            .map(normalizeEmojiResult)
            .filter(x => x !== null);
    } catch (_) {
        return [];
    }
}

export function onEmojiSelect(ref, e) {
    ref.invokeMethodAsync('EmojiClick', e);
}

export function onClickOutside(ref, e) {
    ref.invokeMethodAsync('ClickOutside', { target: e.target?.id });
}
