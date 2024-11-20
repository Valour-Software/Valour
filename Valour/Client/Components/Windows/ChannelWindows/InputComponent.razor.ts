import DotnetObject = DotNet.DotnetObject;

type InputContext = {
    dotnet: DotnetObject;
    inputEl: HTMLInputElement;
    hookEvents: () => void;
    
    // State tracking
    currentWord: string;
    currentIndex: number;
};

export const init = (dotnet: DotnetObject, inputEl: HTMLInputElement): InputContext => {
    const ctx: InputContext = {
        dotnet,
        inputEl,
        currentWord: '',
        currentIndex: 0,
        hookEvents: () => hookEvents(ctx),
    }
    
    // Hook events before returning reference
    ctx.hookEvents();
    
    return ctx;
};

const hookEvents = (ctx: InputContext) => {
    ctx.inputEl.addEventListener('keydown', (e) => inputKeyDownHandler(e, input));
    ctx.inputEl.addEventListener('click', () => inputCaretMoveHandler(input));
    ctx.inputEl.addEventListener('paste', (e) => inputPasteHandler(e, input));
    ctx.inputEl.addEventListener('input', (e: InputEvent) => inputHandler(ctx, e));
};

// Handles input being input into the input (not confusing at all)
const inputHandler = async (ctx: InputContext , e: InputEvent) => {
    ctx.currentWord = getCurrentWord(0);
    await ctx.dotnet.invokeMethodAsync('OnChatboxUpdate', getInputText(ctx), ctx.currentWord ?? '');
}

// Returns the word at the current caret position, offset by the given amount
const getCurrentWord = (charOffset: number): string => {
    const selection = window.getSelection();

    // Ensure there is a valid selection range
    if (!selection || selection.rangeCount === 0) return '';

    const range = selection.getRangeAt(0);

    // If selection spans multiple characters, return empty string
    if (!range.collapsed) return '';

    const container = range.endContainer;

    // Ensure the container is a text node
    if (container.nodeType !== Node.TEXT_NODE) return '';

    const textContent = container.textContent || '';

    // Calculate the effective range to extract the substring
    const endOffset = Math.max(0, range.startOffset - charOffset);

    // Use regex to find the last word efficiently
    const match = textContent.slice(0, endOffset).match(/\b\w+$/);

    return match ? match[0] : '';
};

const getInputText = (ctx: InputContext): string => {
    return getElementText(ctx.inputEl);
};

const blockLevelElements = new Set([
    'p', 'div', 'section', 'article', 'header', 'footer',
    'blockquote', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
]);

const excludedElements = new Set(['script', 'style']);

const getElementText = (el: Node): string => {
    let text = '';

    switch (el.nodeType) {
        // Text node or CDATA section
        case Node.TEXT_NODE:
        case Node.CDATA_SECTION_NODE:
            return el.nodeValue || '';

        // Element node
        case Node.ELEMENT_NODE:
            const element = el as HTMLElement;
            const tagName = element.tagName.toLowerCase();

            // Handle <img> elements with data-text attributes
            if (tagName === 'img') {
                return element.dataset.text || element.getAttribute('data-text') || '';
            }

            // Handle <br> elements as line breaks
            if (tagName === 'br') {
                return '\n';
            }

            // Add line breaks for block-level elements
            if (blockLevelElements.has(tagName)) {
                text += '\n';
            }

            // Traverse child nodes (skip excluded elements like <script> and <style>)
            if (!excludedElements.has(tagName)) {
                for (const child of el.childNodes) {
                    text += getElementText(child);
                }
            }

            // Add closing line break for block-level elements
            if (blockLevelElements.has(tagName)) {
                text += '\n';
            }
            break;
    }

    return text;
};
