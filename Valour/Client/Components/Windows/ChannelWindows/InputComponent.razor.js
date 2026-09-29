const blockLevelElements = new Set([
    'p', 'div', 'section', 'article', 'header', 'footer',
    'blockquote', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
]);
const excludedElements = new Set(['script', 'style']);
/**
 * Removes all unpaired surrogates from a string.
 */
function safeForInterop(str) {
    let result = '';
    for (let i = 0; i < str.length; i++) {
        const code = str.charCodeAt(i);
        if (code >= 0xD800 && code <= 0xDBFF) { // High surrogate
            if (i + 1 < str.length) {
                const next = str.charCodeAt(i + 1);
                if (next >= 0xDC00 && next <= 0xDFFF) {
                    result += str[i] + str[i + 1];
                    i++;
                }
            }
        }
        else if (code >= 0xDC00 && code <= 0xDFFF) {
            // Unpaired low surrogate, skip
        }
        else {
            result += str[i];
        }
    }
    return result;
}
/**
 * Debounce utility to limit function calls.
 */
function debounce(fn, delay) {
    let timer = null;
    return function (...args) {
        if (timer !== null)
            clearTimeout(timer);
        timer = window.setTimeout(() => fn.apply(this, args), delay);
    };
}
/**
 * Recursively extracts text from a DOM node, handling custom emoji images and block elements.
 */
function getElementText(el) {
    let text = '';
    switch (el.nodeType) {
        case Node.TEXT_NODE:
        case Node.CDATA_SECTION_NODE:
            // Replace non-breaking spaces (U+00A0) with regular spaces
            // Browsers insert &nbsp; in contenteditable to prevent whitespace collapsing
            return (el.nodeValue || '').replace(/\u00A0/g, ' ');
        case Node.ELEMENT_NODE: {
            const element = el;
            const tagName = element.tagName.toLowerCase();
            if (tagName === 'img') {
                return element.dataset.text || element.getAttribute('data-text') || '';
            }
            if (tagName === 'br') {
                return '\n';
            }
            if (blockLevelElements.has(tagName)) {
                text += '\n';
            }
            if (!excludedElements.has(tagName)) {
                for (const child of el.childNodes) {
                    text += getElementText(child);
                }
            }
            if (blockLevelElements.has(tagName)) {
                text += '\n';
            }
            break;
        }
    }
    return text;
}
function isMentionWord(word) {
    if (!word) {
        return false;
    }
    if (word[0] === ':') {
        return word.length > 1;
    }
    return word[0] === '@' || word[0] === '#';
}
function insertTextAtCursor(text) {
    const selection = window.getSelection();
    if (!selection || !selection.rangeCount)
        return;
    const range = selection.getRangeAt(0);
    const span = document.createElement("span");
    span.textContent = text.replace(/\n/g, "\n");
    range.deleteContents();
    range.insertNode(span);
    range.setStartAfter(span);
    range.setEndAfter(span);
    selection.removeAllRanges();
    selection.addRange(range);
}
function lastTextNode(node) {
    if (node.nodeType === Node.TEXT_NODE)
        return node;
    for (let i = node.childNodes.length - 1; i >= 0; i--) {
        const found = lastTextNode(node.childNodes[i]);
        if (found)
            return found;
    }
    return null;
}
function firstTextNode(node) {
    if (node.nodeType === Node.TEXT_NODE)
        return node;
    for (const child of node.childNodes) {
        const found = firstTextNode(child);
        if (found)
            return found;
    }
    return null;
}
/**
 * Resolves a caret position to a text node and character offset. A caret on an
 * element sits between its children, so it resolves to the end of the text just
 * before it, or to the start of the text after it when nothing precedes it. The
 * search only descends into the element's children, so it always finishes.
 */
function findTextNodeAndOffset(node, offset) {
    if (node.nodeType === Node.TEXT_NODE) {
        const text = node;
        return { node: text, offset: Math.min(Math.max(offset, 0), text.length) };
    }
    const children = node.childNodes;
    const index = Math.min(Math.max(offset, 0), children.length);
    for (let i = index - 1; i >= 0; i--) {
        const text = lastTextNode(children[i]);
        if (text)
            return { node: text, offset: text.length };
    }
    for (let i = index; i < children.length; i++) {
        const text = firstTextNode(children[i]);
        if (text)
            return { node: text, offset: 0 };
    }
    return null;
}
export function init(dotnet, inputEl) {
    if (!inputEl)
        return null;
    let disposed = false;
    const callback = {
        invokeMethodAsync: async (method, ...args) => {
            if (disposed)
                return;
            try {
                return await dotnet.invokeMethodAsync(method, ...args);
            }
            catch (error) {
                if (!disposed)
                    throw error;
            }
        }
    };
    const ctx = {
        dotnet: callback,
        inputEl,
        currentWord: '',
        currentIndex: 0,
        lastRange: null,
        selectionChangeHandler: () => {
            const selection = window.getSelection();
            if (!selection || selection.rangeCount === 0)
                return;
            const range = selection.getRangeAt(0);
            if (range.collapsed && ctx.inputEl.contains(range.commonAncestorContainer)) {
                ctx.lastRange = range.cloneRange();
            }
        },
        getCursorPos: () => {
            const sel = window.getSelection();
            if (sel && sel.rangeCount) {
                return sel.getRangeAt(0).startOffset;
            }
            return 0;
        },
        getCurrentWord: (offset) => {
            const sel = window.getSelection();
            if (!sel || !sel.rangeCount)
                return '';
            const range = sel.getRangeAt(0);
            if (!range.collapsed)
                return '';
            const target = range.endContainer ?? range.startContainer;
            if (target && target.textContent) {
                return target.textContent.substring(0, range.startOffset - offset).split(/\s+/g).pop() || '';
            }
            return '';
        },
        caretMoveHandler: async (offset = 0) => {
            ctx.selectionChangeHandler();
            ctx.currentWord = ctx.getCurrentWord(offset);
            ctx.currentIndex = ctx.getCursorPos();
            await ctx.dotnet.invokeMethodAsync('OnCaretUpdate', safeForInterop(ctx.currentWord));
        },
        focus: () => {
            ctx.inputEl.focus();
            setTimeout(() => ctx.inputEl.focus(), 0);
        },
        setInputContent: (content) => {
            ctx.inputEl.innerText = content;
        },
        submitMessage: async (keepOpen = false) => {
            ctx.inputEl.innerHTML = '';
            await ctx.dotnet.invokeMethodAsync('OnChatboxSubmit');
            await ctx.dotnet.invokeMethodAsync('OnCaretUpdate', '');
            if (keepOpen)
                ctx.focus();
        },
        moveCursorToEnd() {
            this.focus();
            setTimeout(() => {
                const range = document.createRange();
                const selection = window.getSelection();
                range.selectNodeContents(this.inputEl);
                range.collapse(false);
                selection?.removeAllRanges();
                selection?.addRange(range);
                this.focus();
            }, 50);
        },
        injectElement: (text, coverText, classList, styleList, deleteCurrentWord = true) => {
            if (document.activeElement !== ctx.inputEl)
                ctx.focus();
            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
                let caretRange = sel.getRangeAt(0);
                let endContainer = caretRange.endContainer;
                let endOffset = caretRange.endOffset;
                if (endContainer.nodeType !== Node.TEXT_NODE) {
                    const textNodeData = findTextNodeAndOffset(endContainer, endOffset);
                    if (textNodeData) {
                        endContainer = textNodeData.node;
                        endOffset = textNodeData.offset;
                    }
                    else {
                        console.error('No text node found at caret position');
                        return;
                    }
                }
                // Read the word from the same text node the range is built on.
                // The selection may sit on an element, whose text and offsets
                // do not line up with the resolved text node.
                ctx.currentWord = (endContainer.textContent ?? '').substring(0, endOffset).split(/\s+/g).pop() || '';
                const range = document.createRange();
                const wordLength = ctx.currentWord.length;
                const startOffset = endOffset - wordLength;
                range.setStart(endContainer, startOffset);
                range.setEnd(endContainer, endOffset);
                if (deleteCurrentWord) {
                    range.deleteContents();
                    endOffset = startOffset;
                }
                const node = document.createTextNode(text);
                const cont = document.createElement('span');
                cont.appendChild(node);
                const classes = classList ? classList.split(' ') : [];
                classes.push('input-magic');
                cont.classList.add(...classes);
                if (styleList)
                    cont.setAttribute('style', styleList);
                cont.contentEditable = 'false';
                cont.setAttribute('data-before', coverText);
                range.insertNode(cont);
                range.setStartAfter(cont);
                range.collapse(true);
                sel.removeAllRanges();
                sel.addRange(range);
            }
            else {
                console.error("No selection available");
            }
            ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        },
        injectEmoji: async (text, native, shortcodes, deleteCurrentWord = false, appendSpace = false, isCustom = false, customToken = '', customSrc = '') => {
            let sel = window.getSelection();
            let range;
            if (!ctx.inputEl.contains(sel?.anchorNode)) {
                if (ctx.lastRange) {
                    sel?.removeAllRanges();
                    range = ctx.lastRange.cloneRange();
                    sel?.addRange(range);
                }
                else {
                    ctx.inputEl.focus();
                    sel = window.getSelection();
                    range = document.createRange();
                    range.selectNodeContents(ctx.inputEl);
                    range.collapse(false);
                    sel?.removeAllRanges();
                    sel?.addRange(range);
                }
            }
            else {
                if (sel && sel.rangeCount > 0) {
                    range = sel.getRangeAt(0);
                }
                else {
                    console.error("No selection available");
                    return;
                }
            }
            if (deleteCurrentWord) {
                let endContainer = range.endContainer;
                let endOffset = range.endOffset;
                if (endContainer.nodeType !== Node.TEXT_NODE) {
                    const textNodeData = findTextNodeAndOffset(endContainer, endOffset);
                    if (textNodeData) {
                        endContainer = textNodeData.node;
                        endOffset = textNodeData.offset;
                    }
                }
                if (endContainer.nodeType === Node.TEXT_NODE) {
                    const currentWord = ctx.getCurrentWord(0);
                    const startOffset = Math.max(0, endOffset - currentWord.length);
                    const wordRange = document.createRange();
                    wordRange.setStart(endContainer, startOffset);
                    wordRange.setEnd(endContainer, endOffset);
                    wordRange.deleteContents();
                    range = wordRange;
                }
                else {
                    range.deleteContents();
                }
            }
            else {
                range.deleteContents();
            }
            // Unicode emoji are plain text drawn by the system font. Custom
            // planet emoji are images that carry their token for getElementText.
            let inserted;
            if (isCustom) {
                const token = customToken || (text ? `:${text}:` : '');
                const tooltip = shortcodes || token;
                if (!customSrc) {
                    return;
                }
                const img = document.createElement('img');
                img.src = customSrc;
                img.setAttribute('data-text', token);
                img.alt = tooltip;
                img.title = tooltip;
                img.setAttribute('aria-label', tooltip);
                img.classList.add('emoji', 'custom-emoji');
                img.style.width = '1em';
                inserted = img;
            }
            else {
                if (!native) {
                    return;
                }
                inserted = document.createTextNode(native);
            }
            range.insertNode(inserted);
            if (appendSpace) {
                const spacer = document.createTextNode(' ');
                inserted.parentNode?.insertBefore(spacer, inserted.nextSibling);
                range.setStartAfter(spacer);
            }
            else {
                range.setStartAfter(inserted);
            }
            range.collapse(true);
            sel?.removeAllRanges();
            sel?.addRange(range);
            ctx.lastRange = range.cloneRange();
            // The caret now follows the emoji, so no word is being typed.
            // Reporting the replaced ":shortcode" would reopen its suggestions.
            ctx.currentWord = '';
            await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        },
        keyDownHandler: async (e) => {
            ctx.currentWord = ctx.getCurrentWord(0);
            ctx.currentIndex = ctx.getCursorPos();
            switch (e.code) {
                case "ArrowDown":
                case "ArrowUp":
                    if (isMentionWord(ctx.currentWord)) {
                        e.preventDefault();
                        await ctx.dotnet.invokeMethodAsync('MoveMentionSelect', e.code === "ArrowDown" ? 1 : -1);
                    }
                    else {
                        if (e.code === "ArrowUp") {
                            await ctx.dotnet.invokeMethodAsync('OnUpArrowNonMention');
                        }
                        await ctx.caretMoveHandler();
                    }
                    break;
                case "ArrowLeft":
                case "ArrowRight":
                    await ctx.caretMoveHandler(e.code === "ArrowLeft" ? -1 : 1);
                    break;
                case "Enter":
                    if (e.shiftKey)
                        break;
                    if (isMentionWord(ctx.currentWord)) {
                        e.preventDefault();
                        const handled = await ctx.dotnet.invokeMethodAsync('MentionSubmit');
                        if (!handled) {
                            if (window["mobile"] && !window["embedded"])
                                break;
                            await ctx.submitMessage();
                        }
                    }
                    else {
                        if (window["mobile"] && !window["embedded"])
                            break;
                        e.preventDefault();
                        await ctx.submitMessage();
                    }
                    break;
                case "Tab":
                    if (isMentionWord(ctx.currentWord)) {
                        e.preventDefault();
                        await ctx.dotnet.invokeMethodAsync('MentionSubmit');
                    }
                    break;
                case "Escape":
                    await ctx.dotnet.invokeMethodAsync('OnEscape');
                    break;
            }
        },
        // Debounced input handler for performance
        inputHandler: debounce(async (e) => {
            if (disposed)
                return;
            ctx.selectionChangeHandler();
            ctx.currentWord = ctx.getCurrentWord(0);
            await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        }, 50),
        pasteHandler: async (e) => {
            e.preventDefault();
            const text = e.clipboardData?.getData('text/plain') ?? '';
            insertTextAtCursor(text);
            ctx.currentWord = ctx.getCurrentWord(0);
            await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        },
        clickHandler: () => {
            ctx.inputEl.focus();
            ctx.caretMoveHandler();
        },
        hookEvents: () => {
            ctx.inputEl.addEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.addEventListener('click', ctx.clickHandler);
            ctx.inputEl.addEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.addEventListener('input', ctx.inputHandler);
            document.addEventListener('selectionchange', ctx.selectionChangeHandler);
        },
        cleanup: () => {
            disposed = true;
            ctx.inputEl.removeEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.removeEventListener('click', ctx.clickHandler);
            ctx.inputEl.removeEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.removeEventListener('input', ctx.inputHandler);
            document.removeEventListener('selectionchange', ctx.selectionChangeHandler);
        }
    };
    ctx.hookEvents();
    return ctx;
}
//# sourceMappingURL=InputComponent.razor.js.map