import DotnetObject = DotNet.DotnetObject;

type InputContext = {
    dotnet: Pick<DotnetObject, "invokeMethodAsync">;
    inputEl: HTMLElement;
    caretMoveHandler: (offset?: number) => Promise<void>;
    getCurrentWord: (off: number) => string;
    getCursorPos: () => number;
    setInputSegments: (segments: InputSegment[]) => void;
    focus: () => void;
    submitMessage: (keepOpen?: boolean) => Promise<void>;
    moveCursorToEnd: () => void;
    injectMention: (token: string, label: string) => void;
    injectEmoji: (
        text: string,
        native: string,
        shortcodes: string,
        deleteCurrentWord?: boolean,
        appendSpace?: boolean,
        isCustom?: boolean,
        customToken?: string,
        customSrc?: string
    ) => Promise<void>;
    pasteHandler: (e: ClipboardEvent) => void;
    copyHandler: (e: ClipboardEvent) => void;
    keyDownHandler: (e: KeyboardEvent) => void;
    inputHandler: Debounced<(e: InputEvent) => void>;
    clickHandler: () => void;
    selectionChangeHandler: () => void;
    hookEvents: () => void;
    cleanup: () => void;
    currentWord: string;
    currentIndex: number;
    lastRange: Range | null;
};

/**
 * A piece of message text, matching MessageInputSegment in C#. Text is typed
 * text; mentions and custom emoji carry their token in text.
 */
type InputSegment = {
    kind: 'text' | 'mention' | 'emoji';
    text: string;
    label?: string;
    src?: string;
};

type BlazorRootComponent = { dispose: () => Promise<void> };
type BlazorRootComponents = {
    add: (element: Element, identifier: string, parameters: Record<string, unknown>) => Promise<BlazorRootComponent>;
};

// Matches InputMentionChip.JsIdentifier in C#.
const mentionChipComponent = 'valour-input-mention';
const mentionChipClass = 'input-mention';

// Copying inside the input keeps tokens under this type, so pasting a
// mention gives a chip. Other apps receive the visible text.
const messageClipboardType = 'application/x-valour-message';

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
type Debounced<T extends (...args: any[]) => void> = T & { cancel: () => void };

function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): Debounced<T> {
    let timer: number | null = null;
    const debounced = function (this: any, ...args: any[]) {
        if (timer !== null) clearTimeout(timer);
        timer = window.setTimeout(() => {
            timer = null;
            fn.apply(this, args);
        }, delay);
    } as Debounced<T>;
    debounced.cancel = () => {
        if (timer !== null) clearTimeout(timer);
        timer = null;
    };
    return debounced;
}

function isMentionChip(node: Node | null): boolean {
    return node?.nodeType === Node.ELEMENT_NODE && (node as HTMLElement).classList.contains(mentionChipClass);
}

/** True for nodes rendered by a chip's Blazor component, where the caret does not belong. */
function isInsideChip(node: Node | null): boolean {
    const element = node?.nodeType === Node.ELEMENT_NODE ? node as Element : node?.parentElement;
    return !!element?.closest(`.${mentionChipClass}`);
}

/**
 * Recursively extracts text from a DOM node, handling mention chips, custom
 * emoji images, and block elements. By default chips and custom emoji give
 * their message tokens. With display set, they give the text the user sees.
 */
function getElementText(el: Node, display = false): string {
    let text = '';
    switch (el.nodeType) {
        case Node.TEXT_NODE:
        case Node.CDATA_SECTION_NODE:
            // Replace non-breaking spaces (U+00A0) with regular spaces
            // Browsers insert &nbsp; in contenteditable to prevent whitespace collapsing
            return (el.nodeValue || '').replace(/\u00A0/g, ' ');
        case Node.DOCUMENT_FRAGMENT_NODE:
            for (const child of el.childNodes) {
                text += getElementText(child, display);
            }
            return text;
        case Node.ELEMENT_NODE: {
            const element = el as HTMLElement;
            if (isMentionChip(element)) {
                return display
                    ? (element.textContent?.trim() || element.dataset.label || '')
                    : (element.dataset.text || '');
            }
            const tagName = element.tagName.toLowerCase();
            if (tagName === 'img') {
                const token = element.dataset.text || element.getAttribute('data-text') || '';
                return display ? (element.getAttribute('alt') || token) : token;
            }
            if (tagName === 'br') {
                return '\n';
            }
            if (blockLevelElements.has(tagName)) {
                text += '\n';
            }
            if (!excludedElements.has(tagName)) {
                for (const child of el.childNodes) {
                    text += getElementText(child, display);
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
    if (!word) {
        return false;
    }

    if (word[0] === ':') {
        return word.length > 1;
    }

    return word[0] === '@' || word[0] === '#';
}

/**
 * Creates an empty chip for a mention. The input's observer adds the Blazor
 * mention component to it; until then, CSS shows the label.
 */
function createMentionChip(token: string, label: string): HTMLSpanElement {
    const chip = document.createElement('span');
    chip.classList.add(mentionChipClass);
    chip.contentEditable = 'false';
    chip.dataset.text = token;
    chip.dataset.label = label || '';
    return chip;
}

function createCustomEmoji(token: string, tooltip: string, src: string): HTMLImageElement {
    const img = document.createElement('img');
    img.src = src;
    img.setAttribute('data-text', token);
    img.alt = tooltip;
    img.title = tooltip;
    img.setAttribute('aria-label', tooltip);
    img.classList.add('emoji', 'custom-emoji');
    img.style.width = '1em';
    return img;
}

function buildSegmentNodes(segments: InputSegment[]): DocumentFragment {
    const fragment = document.createDocumentFragment();
    for (const segment of segments ?? []) {
        if (segment.kind === 'mention') {
            fragment.appendChild(createMentionChip(segment.text, segment.label ?? ''));
        } else if (segment.kind === 'emoji' && segment.src) {
            const tooltip = segment.label || segment.text;
            fragment.appendChild(createCustomEmoji(segment.text, tooltip, segment.src));
        } else {
            (segment.text ?? '').split('\n').forEach((line, i) => {
                if (i > 0) fragment.appendChild(document.createElement('br'));
                if (line) fragment.appendChild(document.createTextNode(line));
            });
        }
    }
    return fragment;
}

function lastTextNode(node: Node): Text | null {
    if (node.nodeType === Node.TEXT_NODE) return node as Text;
    if (isMentionChip(node)) return null;
    for (let i = node.childNodes.length - 1; i >= 0; i--) {
        const found = lastTextNode(node.childNodes[i]);
        if (found) return found;
    }
    return null;
}

function firstTextNode(node: Node): Text | null {
    if (node.nodeType === Node.TEXT_NODE) return node as Text;
    if (isMentionChip(node)) return null;
    for (const child of node.childNodes) {
        const found = firstTextNode(child);
        if (found) return found;
    }
    return null;
}

/**
 * Resolves a caret position to a text node and character offset. A caret on an
 * element sits between its children, so it resolves to the end of the text just
 * before it, or to the start of the text after it when nothing precedes it. The
 * search only descends into the element's children, so it always finishes. It
 * stops at a mention chip: the chip's text belongs to its Blazor component, and
 * text on the far side of a chip is not at the caret.
 */
function findTextNodeAndOffset(node: Node, offset: number): { node: Text, offset: number } | null {
    if (node.nodeType === Node.TEXT_NODE) {
        const text = node as Text;
        return { node: text, offset: Math.min(Math.max(offset, 0), text.length) };
    }
    const children = node.childNodes;
    const index = Math.min(Math.max(offset, 0), children.length);
    for (let i = index - 1; i >= 0; i--) {
        if (isMentionChip(children[i])) break;
        const text = lastTextNode(children[i]);
        if (text) return { node: text, offset: text.length };
    }
    for (let i = index; i < children.length; i++) {
        if (isMentionChip(children[i])) break;
        const text = firstTextNode(children[i]);
        if (text) return { node: text, offset: 0 };
    }
    return null;
}

export function init(dotnet: DotnetObject, inputEl: HTMLElement, chipContextId?: string): InputContext {
    if (!inputEl) return null;
    let disposed = false;
    const callback: Pick<DotnetObject, "invokeMethodAsync"> = {
        invokeMethodAsync: async <T>(method: string, ...args: any[]): Promise<T> => {
            if (disposed) return;
            try { return await dotnet.invokeMethodAsync<T>(method, ...args); }
            catch (error) { if (!disposed) throw error; }
        }
    };

    // Each mention chip holds its own Blazor root component. A chip can leave
    // the input (deleted, cut, cleared on send) and come back (undo), so the
    // observer below adds and disposes roots to match the chips present.
    const chipRoots = new Map<HTMLElement, Promise<BlazorRootComponent | null>>();
    const chipDisposals = new Map<HTMLElement, Promise<void>>();

    const mountChip = (chip: HTMLElement) => {
        if (disposed || chipRoots.has(chip)) return;
        const roots: BlazorRootComponents | undefined = (window as any).Blazor?.rootComponents;
        if (!roots) return;
        // A chip that returns while its previous root is still being disposed
        // waits for that disposal before it is rendered again.
        const mounting: Promise<BlazorRootComponent | null> = (chipDisposals.get(chip) ?? Promise.resolve())
            .then(() => {
                if (disposed || chipRoots.get(chip) !== mounting) return null;
                chip.replaceChildren();
                return roots.add(chip, mentionChipComponent, { token: chip.dataset.text ?? '', inputId: chipContextId ?? '' });
            })
            .catch(error => {
                console.error('Failed to render a mention in the message input', error);
                return null;
            });
        chipRoots.set(chip, mounting);
    };

    const unmountChip = (chip: HTMLElement) => {
        const mounting = chipRoots.get(chip);
        if (!mounting) return;
        chipRoots.delete(chip);
        const disposal: Promise<void> = mounting
            .then(root => root?.dispose())
            .catch(() => { })
            .finally(() => {
                if (chipDisposals.get(chip) === disposal) chipDisposals.delete(chip);
            });
        chipDisposals.set(chip, disposal);
    };

    const syncChips = () => {
        for (const chip of [...chipRoots.keys()]) {
            if (disposed || !inputEl.contains(chip)) unmountChip(chip);
        }
        if (disposed) return;
        inputEl.querySelectorAll<HTMLElement>(`.${mentionChipClass}`).forEach(mountChip);
    };

    const chipObserver = new MutationObserver(syncChips);

    const reportContent = (currentWord: string) =>
        ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), safeForInterop(currentWord));

    /**
     * Returns the caret's range in the input. When focus has moved elsewhere,
     * such as to a picker, the caret returns to where it last was in the
     * input, or to the end of the input.
     */
    const caretRange = (): Range | null => {
        const sel = window.getSelection();
        if (!sel) return null;
        if (sel.rangeCount > 0 && ctx.inputEl.contains(sel.anchorNode)) return sel.getRangeAt(0);

        let range: Range;
        if (ctx.lastRange) {
            range = ctx.lastRange.cloneRange();
        } else {
            ctx.inputEl.focus();
            range = document.createRange();
            range.selectNodeContents(ctx.inputEl);
            range.collapse(false);
        }
        sel.removeAllRanges();
        sel.addRange(range);
        return range;
    };

    const placeCaret = (range: Range) => {
        const sel = window.getSelection();
        sel?.removeAllRanges();
        sel?.addRange(range);
        ctx.lastRange = range.cloneRange();
    };

    const ctx: InputContext = {
        dotnet: callback,
        inputEl,
        currentWord: '',
        currentIndex: 0,
        lastRange: null,

        selectionChangeHandler: () => {
            const selection = window.getSelection();
            if (!selection || selection.rangeCount === 0) return;

            const range = selection.getRangeAt(0);
            if (range.collapsed && ctx.inputEl.contains(range.commonAncestorContainer) && !isInsideChip(range.startContainer)) {
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

        getCurrentWord: (offset: number) => {
            const sel = window.getSelection();
            if (!sel || !sel.rangeCount) return '';
            const range = sel.getRangeAt(0);
            if (!range.collapsed) return '';
            const caret = findTextNodeAndOffset(range.endContainer ?? range.startContainer, range.startOffset);
            if (!caret || !ctx.inputEl.contains(caret.node) || isInsideChip(caret.node)) return '';
            return (caret.node.textContent ?? '').substring(0, caret.offset - offset).split(/\s+/g).pop() || '';
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

        setInputSegments: (segments: InputSegment[]) => {
            ctx.inputEl.replaceChildren(buildSegmentNodes(segments));
            ctx.lastRange = null;
        },

        submitMessage: async (keepOpen = false) => {
            // Typing reaches the component through a short debounce, so the
            // text typed just before Enter may not have been sent yet. Send
            // it now, before clearing the box; otherwise the component would
            // submit the older text, or nothing, and the typed text is lost.
            ctx.inputHandler.cancel();
            await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', safeForInterop(getElementText(ctx.inputEl)), '');
            ctx.inputEl.innerHTML = '';
            ctx.lastRange = null;
            await ctx.dotnet.invokeMethodAsync('OnChatboxSubmit');
            await ctx.dotnet.invokeMethodAsync('OnCaretUpdate', '');
            if (keepOpen) ctx.focus();
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

        injectMention: (token: string, label: string) => {
            if (document.activeElement !== ctx.inputEl) ctx.focus();
            const range = caretRange();
            if (!range) {
                console.error("No selection available");
                return;
            }

            // Replace the word being typed, such as "@na", with the chip.
            // The word is read from the same text node the range is built on;
            // the selection may sit on an element, whose text and offsets do
            // not line up with the resolved text node.
            const caret = findTextNodeAndOffset(range.endContainer, range.endOffset);
            const insertAt = document.createRange();
            if (caret) {
                const word = (caret.node.textContent ?? '').substring(0, caret.offset).split(/\s+/g).pop() || '';
                insertAt.setStart(caret.node, caret.offset - word.length);
                insertAt.setEnd(caret.node, caret.offset);
                insertAt.deleteContents();
            } else {
                insertAt.setStart(range.endContainer, range.endOffset);
                insertAt.collapse(true);
            }

            // The space after the chip gives the caret a text node to sit in.
            const chip = createMentionChip(token, label);
            const spacer = document.createTextNode(' ');
            insertAt.insertNode(spacer);
            insertAt.insertNode(chip);

            const after = document.createRange();
            after.setStart(spacer, spacer.length);
            after.collapse(true);
            placeCaret(after);

            // The caret now follows the mention, so no word is being typed.
            ctx.currentWord = '';
            reportContent(ctx.currentWord);
        },

        injectEmoji: async (
            text: string,
            native: string,
            shortcodes: string,
            deleteCurrentWord = false,
            appendSpace = false,
            isCustom = false,
            customToken = '',
            customSrc = ''
        ) => {
            let range = caretRange();
            if (!range) {
                console.error("No selection available");
                return;
            }
            if (deleteCurrentWord) {
                const caret = findTextNodeAndOffset(range.endContainer, range.endOffset);
                if (caret) {
                    const currentWord = ctx.getCurrentWord(0);
                    const startOffset = Math.max(0, caret.offset - currentWord.length);
                    const wordRange = document.createRange();
                    wordRange.setStart(caret.node, startOffset);
                    wordRange.setEnd(caret.node, caret.offset);
                    wordRange.deleteContents();
                    range = wordRange;
                } else {
                    range.deleteContents();
                }
            } else {
                range.deleteContents();
            }

            // Unicode emoji are plain text drawn by the system font. Custom
            // planet emoji are images that carry their token for getElementText.
            let inserted: Node;
            if (isCustom) {
                const token = customToken || (text ? `:${text}:` : '');
                if (!customSrc) {
                    return;
                }

                inserted = createCustomEmoji(token, shortcodes || token, customSrc);
            } else {
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
            } else {
                range.setStartAfter(inserted);
            }

            range.collapse(true);
            placeCaret(range);
            // The caret now follows the emoji, so no word is being typed.
            // Reporting the replaced ":shortcode" would reopen its suggestions.
            ctx.currentWord = '';
            await reportContent(ctx.currentWord);
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
                        await ctx.caretMoveHandler();
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
                        const handled = await ctx.dotnet.invokeMethodAsync<boolean>('MentionSubmit');
                        if (!handled) {
                            if (window["mobile"] && !window["embedded"]) break;
                            await ctx.submitMessage();
                        }
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
            if (disposed) return;
            ctx.selectionChangeHandler();
            ctx.currentWord = ctx.getCurrentWord(0);
            await reportContent(ctx.currentWord);
        }, 50),

        pasteHandler: async (e: ClipboardEvent) => {
            e.preventDefault();
            const text = e.clipboardData?.getData(messageClipboardType)
                || e.clipboardData?.getData('text/plain')
                || '';
            const range = text ? caretRange()?.cloneRange() : null;
            if (!range) return;

            // Tokens in the text, such as «@m-123», become chips and images.
            let segments: InputSegment[] = [{ kind: 'text', text }];
            if (text.includes('«')) {
                segments = await ctx.dotnet.invokeMethodAsync<InputSegment[]>('GetInputSegments', safeForInterop(text)) ?? segments;
                if (disposed || !ctx.inputEl.contains(range.startContainer)) return;
            }

            const nodes = buildSegmentNodes(segments);
            const last = nodes.lastChild;
            range.deleteContents();
            range.insertNode(nodes);
            if (last) range.setStartAfter(last);
            range.collapse(true);
            placeCaret(range);

            ctx.currentWord = ctx.getCurrentWord(0);
            await reportContent(ctx.currentWord);
        },

        copyHandler: (e: ClipboardEvent) => {
            const sel = window.getSelection();
            if (!e.clipboardData || !sel || sel.rangeCount === 0 || sel.isCollapsed) return;
            const range = sel.getRangeAt(0);
            if (!ctx.inputEl.contains(range.commonAncestorContainer)) return;

            const copied = range.cloneContents();
            e.preventDefault();
            e.clipboardData.setData('text/plain', getElementText(copied, true));
            e.clipboardData.setData(messageClipboardType, getElementText(copied));
            // Deleting through the editing command keeps the cut undoable and
            // reports it through the input event.
            if (e.type === 'cut') document.execCommand('delete');
        },

        clickHandler: () => {
            ctx.inputEl.focus();
            ctx.caretMoveHandler();
        },

        hookEvents: () => {
            ctx.inputEl.addEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.addEventListener('click', ctx.clickHandler);
            ctx.inputEl.addEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.addEventListener('copy', ctx.copyHandler);
            ctx.inputEl.addEventListener('cut', ctx.copyHandler);
            ctx.inputEl.addEventListener('input', ctx.inputHandler);
            document.addEventListener('selectionchange', ctx.selectionChangeHandler);
            chipObserver.observe(ctx.inputEl, { childList: true, subtree: true });
        },

        cleanup: () => {
            disposed = true;
            chipObserver.disconnect();
            syncChips();
            ctx.inputEl.removeEventListener('keydown', ctx.keyDownHandler);
            ctx.inputEl.removeEventListener('click', ctx.clickHandler);
            ctx.inputEl.removeEventListener('paste', ctx.pasteHandler);
            ctx.inputEl.removeEventListener('copy', ctx.copyHandler);
            ctx.inputEl.removeEventListener('cut', ctx.copyHandler);
            ctx.inputEl.removeEventListener('input', ctx.inputHandler);
            document.removeEventListener('selectionchange', ctx.selectionChangeHandler);
        }
    };

    ctx.hookEvents();
    return ctx;
}
