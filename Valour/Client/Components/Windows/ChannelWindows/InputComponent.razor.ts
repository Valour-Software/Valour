import DotnetObject = DotNet.DotnetObject;

type InputContext = {
    dotnet: DotnetObject;
    inputEl: HTMLElement;
    caretMoveHandler: (offset?: number) => Promise<void>;
    getCurrentWord: (off: number) => string;
    getCursorPos: () => number;
    setInputContent: (content: string) => void;
    focus: () => void;
    submitMessage: (keepOpen?: boolean) => Promise<void>;
    moveCursorToEnd: () => void;
    injectElement: (text: string, coverText: string, classList: string, styleList: string) => void;
    injectEmoji: (text: string, native: string, unified: string, shortcodes: string) => Promise<void>;
    openUploadFile: (uploadEl: HTMLElement) => void;
    pasteHandler: (e: ClipboardEvent) => void;
    keyDownHandler: (e: KeyboardEvent) => void;
    inputHandler: (e: InputEvent) => void;
    clickHandler: () => void;
    hookEvents: () => void;
    cleanup: () => void;
    currentWord: string;
    currentIndex: number;
    lastRange: Range | null;
};

const blockLevelElements = new Set([
    'p', 'div', 'section', 'article', 'header', 'footer',
    'blockquote', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
]);
const excludedElements = new Set(['script', 'style']);

/**
 * Removes all unpaired surrogates from a string.
 */
function safeForInterop(str: string): string {
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
        } else if (code >= 0xDC00 && code <= 0xDFFF) {
            // Unpaired low surrogate, skip
        } else {
            result += str[i];
        }
    }
    return result;
}

/**
 * Debounce utility to limit function calls.
 */
function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): T {
    let timer: number | null = null;
    return function (this: any, ...args: any[]) {
        if (timer !== null) clearTimeout(timer);
        timer = window.setTimeout(() => fn.apply(this, args), delay);
    } as T;
}

/**
 * Recursively extracts text from a DOM node, handling emoji images and block elements.
 */
function getElementText(el: Node): string {
    let text = '';
    switch (el.nodeType) {
        case Node.TEXT_NODE:
        case Node.CDATA_SECTION_NODE:
            // Replace non-breaking spaces (U+00A0) with regular spaces
            // Browsers insert &nbsp; in contenteditable to prevent whitespace collapsing
            return (el.nodeValue || '').replace(/\u00A0/g, ' ');
        case Node.ELEMENT_NODE: {
            const element = el as HTMLElement;
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

function isMentionWord(word: string): boolean {
    return !!word && (word[0] === '@' || word[0] === '#');
}

function insertTextAtCursor(text: string) {
    const selection = window.getSelection();
    if (!selection || !selection.rangeCount) return;
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

function findTextNodeAndOffset(node: Node, offset: number): { node: Text, offset: number } | null {
    if (node.nodeType === Node.TEXT_NODE) {
        return { node: node as Text, offset };
    }
    if (node.childNodes.length > 0) {
        let childNode: Node | null = null;
        if (offset < node.childNodes.length) {
            childNode = node.childNodes[offset];
        } else if (node.childNodes.length > 0) {
            childNode = node.childNodes[node.childNodes.length - 1];
            offset = childNode.textContent ? childNode.textContent.length : 0;
        }
        if (childNode) {
            return findTextNodeAndOffset(childNode, offset);
        }
    }
    let sibling: Node | null = node.previousSibling;
    while (sibling) {
        if (sibling.nodeType === Node.TEXT_NODE) {
            const textLength = sibling.textContent ? sibling.textContent.length : 0;
            return { node: sibling as Text, offset: textLength };
        }
        sibling = sibling.previousSibling;
    }
    if (node.parentNode) {
        return findTextNodeAndOffset(node.parentNode, offset);
    }
    return null;
}

export function init(dotnet: DotnetObject, inputEl: HTMLElement): InputContext {
    const ctx: InputContext = {
        dotnet,
        inputEl,
        currentWord: '',
        currentIndex: 0,
        lastRange: null,

        getCursorPos: () => {
            const sel = window.getSelection();
            if (sel && sel.rangeCount) {
                return sel.getRangeAt(0).startOffset;
            }
            return 0;
        },

        getCurrentWord: (offset: number) => {
            const sel = window.getSelection();
            if (!sel || !sel.rangeCount) return '';
            const range = sel.getRangeAt(0);
            if (!range.collapsed) return '';
            const target = range.endContainer ?? range.startContainer;
            if (target && target.textContent) {
                return target.textContent.substring(0, range.startOffset - offset).split(/\s+/g).pop() || '';
            }
            return '';
        },

        caretMoveHandler: async (offset = 0) => {
            ctx.currentWord = ctx.getCurrentWord(offset);
            ctx.currentIndex = ctx.getCursorPos();
            await ctx.dotnet.invokeMethodAsync('OnCaretUpdate', safeForInterop(ctx.currentWord));
            if (document.activeElement !== ctx.inputEl) {
                const sel = window.getSelection();
                if (sel && sel.rangeCount > 0) {
                    ctx.lastRange = sel.getRangeAt(0).cloneRange();
                }
            }
        },

        openUploadFile: (uploadEl: HTMLElement) => uploadEl.click(),

        focus: () => {
            ctx.inputEl.focus();
            setTimeout(() => ctx.inputEl.focus(), 0);
        },

        setInputContent: (content: string) => {
            ctx.inputEl.innerText = content;
        },

        submitMessage: async (keepOpen = false) => {
            if (keepOpen) ctx.focus();
            ctx.inputEl.innerHTML = '';
            await ctx.dotnet.invokeMethodAsync('OnChatboxSubmit');
            await ctx.dotnet.invokeMethodAsync('OnCaretUpdate', '');
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

        injectElement: (
            text: string,
            coverText: string,
            classList: string,
            styleList: string,
            deleteCurrentWord = true
        ) => {
            if (document.activeElement !== ctx.inputEl) ctx.focus();
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
                    } else {
                        console.error('No text node found at caret position');
                        return;
                    }
                }
                ctx.currentWord = ctx.getCurrentWord(0);
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
                if (styleList) cont.setAttribute('style', styleList);
                cont.contentEditable = 'false';
                cont.setAttribute('data-before', coverText);
                range.insertNode(cont);
                range.setStartAfter(cont);
                range.collapse(true);
                sel.removeAllRanges();
                sel.addRange(range);
            } else {
                console.error("No selection available");
            }
            ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        },

        injectEmoji: async (
            text: string,
            native: string,
            unified: string,
            shortcodes: string
        ) => {
            let sel = window.getSelection();
            let range: Range;
            if (!ctx.inputEl.contains(sel?.anchorNode)) {
                if (ctx.lastRange) {
                    sel?.removeAllRanges();
                    range = ctx.lastRange.cloneRange();
                    sel?.addRange(range);
                } else {
                    ctx.inputEl.focus();
                    ctx.moveCursorToEnd();
                    sel = window.getSelection();
                    range = sel!.getRangeAt(0);
                }
            } else {
                if (sel && sel.rangeCount > 0) {
                    range = sel.getRangeAt(0);
                } else {
                    console.error("No selection available");
                    return;
                }
            }
            range.deleteContents();
            const img = document.createElement('img');
            img.src = `https://cdn.jsdelivr.net/npm/emoji-datasource-twitter@14.0.0/img/twitter/64/${unified}.png`;
            img.setAttribute('data-text', native);
            img.alt = native;
            img.classList.add('emoji');
            img.style.width = '1em';
            range.insertNode(img);
            range.setStartAfter(img);
            range.collapse(true);
            sel?.removeAllRanges();
            sel?.addRange(range);
            await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(ctx.currentWord));
        },

        keyDownHandler: async (e: KeyboardEvent) => {
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
                        await this.caretMoveHandler();
                    }
                    break;
                case "ArrowLeft":
                case "ArrowRight":
                    await ctx.caretMoveHandler(e.code === "ArrowLeft" ? -1 : 1);
                    break;
                case "Enter":
                    if (e.shiftKey) break;
                    if (isMentionWord(ctx.currentWord)) {
                        e.preventDefault();
                        await ctx.dotnet.invokeMethodAsync('MentionSubmit');
                    } else {
                        if (window["mobile"] && !window["embedded"]) break;
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
        inputHandler: debounce(async (e: InputEvent) => {
            ctx.currentWord = ctx.getCurrentWord(0);
            await ctx.dotnet.invokeMethodAsync(
                'OnChatboxUpdate',
                safeForInterop(getElementText(ctx.inputEl)),
                safeForInterop(ctx.currentWord)
            );
        }, 50),

        pasteHandler: async (e: ClipboardEvent) => {
            e.preventDefault();
            const text = e.clipboardData?.getData('text/plain') ?? '';
            insertTextAtCursor(text);
            ctx.currentWord = ctx.getCurrentWord(0);
            await ctx.dotnet.invokeMethodAsync(
                'OnChatboxUpdate',
                safeForInterop(getElementText(ctx.inputEl)),
                safeForInterop(ctx.currentWord)
            );
        },

        clickHandler: () => {
            ctx.caretMoveHandler();
        },

        hookEvents: () => {
            ctx.inputEl.addEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.addEventListener('click', ctx.clickHandler);
            ctx.inputEl.addEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.addEventListener('input', ctx.inputHandler);
        },

        cleanup: () => {
            ctx.inputEl.removeEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.removeEventListener('click', ctx.clickHandler);
            ctx.inputEl.removeEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.removeEventListener('input', ctx.inputHandler);
        }
    };

    ctx.hookEvents();
    return ctx;
}
