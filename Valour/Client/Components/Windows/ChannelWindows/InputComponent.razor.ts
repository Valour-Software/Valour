import DotnetObject = DotNet.DotnetObject;

type InputContext = {
    dotnet: DotnetObject;
    inputEl: HTMLInputElement;
    
    // Helper functions
    caretMoveHandler: (offset?: number) => Promise<void>;
    getCurrentWord: (off: number) => string;
    getCursorPos: () => number;
    
    // Interop functions
    setInputContent: (content: string) => void;
    focus: () => void;
    submitMessage: (keepOpen?: boolean) => Promise<void>;
    moveCursorToEnd: () => void;
    injectElement: (text: string, coverText: string, classList: string, styleList: string) => void;
    injectEmoji: (text: string, native: string, unified: string, shortcodes: string) => Promise<void>;
    openUploadFile: (uploadEl: HTMLElement) => void;
    
    // Event handlers
    pasteHandler: (e: ClipboardEvent) => void;
    keyDownHandler: (e: KeyboardEvent) => void;
    inputHandler: (e: InputEvent) => void;
    clickHandler: () => void;
    
    hookEvents: () => void;
    cleanup: () => void;
    
    // State tracking
    currentWord: string;
    currentIndex: number;
    lastRange: Range | null;
};

export function init(dotnet: DotnetObject, inputEl: HTMLInputElement): InputContext {
    const ctx: InputContext = {
        ////////////////
        // Properties //
        ////////////////
        
        dotnet,
        inputEl,
        currentWord: '',
        currentIndex: 0,
        lastRange: null,
        
        //////////////////////
        // Helper Functions //
        //////////////////////

        getCursorPos() {
            if (window.getSelection) {
                const sel = window.getSelection();
                if (sel.getRangeAt && sel.rangeCount) {
                    const range = sel.getRangeAt(0);
                    return range.startOffset;
                }
            }

            return 0;
        },

        getCurrentWord(offset: number): string {
            let range = window.getSelection().getRangeAt(0);

            // If it's a 'wide' selection (multiple characters)
            if (!range.collapsed){
                return '';
            }
            
            const target = range.endContainer ?? range.startContainer ?? range.endContainer.lastChild ?? range.startContainer.lastChild;
            
            if (target) {
                return target.textContent.substring(0, range.startOffset - offset).split(/\s+/g).pop();
            }
            
            return '';
        },

        async caretMoveHandler(offset = 0) {
            this.currentWord = this.getCurrentWord(offset);
            this.currentIndex = this.getCursorPos();
            await this.dotnet.invokeMethodAsync('OnCaretUpdate', this.currentWord ?? '');
            if (document.activeElement !== this.inputEl) {
                const sel = window.getSelection();
                if (sel && sel.rangeCount > 0) {
                    // Clone the range to save the caret position
                    this.lastRange = sel.getRangeAt(0).cloneRange();
                }
            }
        },
        
        ///////////////////////
        // Interop Functions //
        ///////////////////////

        openUploadFile(uploadEl: HTMLElement){
            uploadEl.click();
        },

        focus() {
            const el = this.inputEl;
            el.focus();
            setTimeout(function() {
                el.focus();
            }, 0);
        },
        
        setInputContent(content: string) {
            this.inputEl.innerText = content;
        },
        
        async submitMessage(keepOpen = false) {
            if (keepOpen) {
                this.focus();
            }

            this.inputEl.innerHTML = '';

            // Handle submission of message
            await this.dotnet.invokeMethodAsync('OnChatboxSubmit');
            await this.dotnet.invokeMethodAsync('OnCaretUpdate', '');
        },

        moveCursorToEnd() {
            const range = document.createRange();
            const selection = window.getSelection();

            range.selectNodeContents(this.inputEl); // Select the entire content of the element
            range.collapse(false); // Collapse the range to the end

            selection?.removeAllRanges(); // Clear any existing selections
            selection?.addRange(range); // Add the new range

            this.focus(); // Focus the element
        },

        injectElement(
            text: string,
            coverText: string,
            classList: string,
            styleList: string,
            deleteCurrentWord = true
        ): void {
            // Ensure the input element is focused
            if (document.activeElement !== this.inputEl) {
                this.focus();
            }

            // Get the current selection and range
            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
                const caretRange = sel.getRangeAt(0);

                // Ensure that the endContainer is a Text node
                let endContainer = caretRange.endContainer;
                let endOffset = caretRange.endOffset;

                if (endContainer.nodeType !== Node.TEXT_NODE) {
                    // Find a Text node within endContainer or its descendants
                    const textNodeData = this.findTextNodeAndOffset(endContainer, endOffset);
                    if (textNodeData) {
                        endContainer = textNodeData.node;
                        endOffset = textNodeData.offset;
                    } else {
                        console.error('No text node found at caret position');
                        return;
                    }
                }

                // Use your existing getCurrentWord function
                this.currentWord = this.getCurrentWord(0);

                // Create a new range for the current word
                const range = document.createRange();
                const wordLength = this.currentWord.length;
                const startOffset = endOffset - wordLength;

                range.setStart(endContainer, startOffset);
                range.setEnd(endContainer, endOffset);

                // Delete the current word
                if (deleteCurrentWord) {
                    range.deleteContents();
                    // After deleting, the caret may have moved, adjust endOffset
                    endOffset = startOffset;
                }

                // Create the new content
                const node = document.createTextNode(text);
                const cont = document.createElement('span'); // Use 'span' instead of 'p' for inline elements
                cont.appendChild(node);

                // Add classes
                const classes = classList ? classList.split(' ') : [];
                classes.push('input-magic');
                cont.classList.add(...classes);

                // Set styles
                if (styleList) {
                    cont.setAttribute('style', styleList);
                }

                // Make the container non-editable
                cont.contentEditable = 'false';

                // Set data-before attribute (if needed)
                cont.setAttribute('data-before', coverText);

                // Insert the new content at the caret position
                range.insertNode(cont);

                // Move the caret after the inserted content
                range.setStartAfter(cont);
                range.collapse(true);

                // Update the selection
                sel.removeAllRanges();
                sel.addRange(range);
            } else {
                console.error("No selection available");
            }

            this.dotnet.invokeMethodAsync('OnChatboxUpdate', getElementText(this.inputEl) ?? '', '');
        },

        async injectEmoji(
            text: string,
            native: string,
            unified: string,
            shortcodes: string
        ): Promise<void> {

            let sel = window.getSelection();
            let range: Range;

            // Check if the input is focused and the selection is within the input
            if (!this.inputEl.contains(sel?.anchorNode)) {
                if (this.lastRange) {
                    // Restore the saved caret position
                    sel.removeAllRanges();
                    range = this.lastRange.cloneRange();
                    sel.addRange(range);
                } else {
                    // If no saved range, move cursor to the end
                    this.inputEl.focus();
                    this.moveCursorToEnd();
                    sel = window.getSelection();
                    range = sel.getRangeAt(0);
                }
            } else {
                // Use the current selection
                if (sel && sel.rangeCount > 0) {
                    range = sel.getRangeAt(0);
                } else {
                    console.error("No selection available");
                    return;
                }
            }

            // Delete any selected content
            range.deleteContents();

            // Create the emoji image element
            const img = document.createElement('img');
            img.src = `https://cdn.jsdelivr.net/npm/emoji-datasource-twitter@14.0.0/img/twitter/64/${unified}.png`;
            img.setAttribute('data-text', native);
            img.alt = native;
            img.classList.add('emoji');
            img.style.width = '1em';

            // Insert the emoji image at the caret position
            range.insertNode(img);

            // Move the caret after the inserted image
            range.setStartAfter(img);
            range.collapse(true);

            // Update the selection
            sel.removeAllRanges();
            sel.addRange(range);
            
            await this.dotnet.invokeMethodAsync('OnChatboxUpdate', getElementText(this.inputEl) ?? '', '');
        },
        
        ////////////
        // Events //
        ////////////

        // Handles keys being pressed when the input is selected
        async keyDownHandler(e: KeyboardEvent) {
            this.currentWord = this.getCurrentWord(0);
            this.currentIndex = this.getCursorPos();
    
            switch (e.code){
                // Down arrow
                case "ArrowDown":
                case "ArrowUp": {
                    // Prevent up and down arrow from moving caret while
                    // the mention menu is open; instead send an event to
                    // the mention menu
                    if (isMentionWord(ctx.currentWord)) {
                        e.preventDefault();
                        await ctx.dotnet.invokeMethodAsync('MoveMentionSelect', e.code === "ArrowDown" ? 1 : -1);
                    }
                    else {
                        await this.caretMoveHandler();
                    }
    
                    break;
                }
                // Left and right arrows
                case "ArrowLeft":
                case "ArrowRight":
                    await this.caretMoveHandler(e.code === "ArrowLeft" ? -1 : 1);
                    break;
                // Enter
                case "Enter": {
                    // If shift key is down, do not submit on enter
                    if (e.shiftKey) {
                        break;
                    }
    
                    // If the mention menu is open this sends off an event to select it rather
                    // than submitting the message!
                    if (isMentionWord(this.currentWord)) {
                        e.preventDefault();
                        await ctx.dotnet.invokeMethodAsync('MentionSubmit');
                    }
                    else {
    
                        // Mobile uses submit button
                        if (window["mobile"] && !window["embedded"]) {
                            break;
                        }
    
                        // prevent default behavior
                        e.preventDefault();
                        await this.submitMessage();
                    }
    
                    break;
                }
                // Tab
                case "Tab": {
                    // If the mention menu is open this sends off an event to select it rather
                    // than adding a tab!
                    if (isMentionWord(this.currentWord)) {
                        e.preventDefault();
                        await this.dotnet.invokeMethodAsync('MentionSubmit');
                    }
                    
                    break;
                }
                // Escape
                case "Escape": {
                    await this.dotnet.invokeMethodAsync('OnEscape');
                    break;
                }
            }
        },
        
        // Handles input being input into the input (not confusing at all)
        async inputHandler(e: InputEvent) {
            this.currentWord = this.getCurrentWord(0);
            await this.dotnet.invokeMethodAsync('OnChatboxUpdate', getElementText(inputEl), ctx.currentWord ?? '');
        },

        // Handles content being pasted into the input
        async pasteHandler(e: ClipboardEvent) {
            e.preventDefault();

            // Get plain text representation
            let text = e.clipboardData.getData('text/plain');

            // We need to put the pasted text in a span to keep the newlines
            insertTextAtCursor(text);

            this.currentWord = this.getCurrentWord(0);

            await this.dotnet.invokeMethodAsync('OnChatboxUpdate', getElementText(inputEl), ctx.currentWord ?? '');
        },
        
        clickHandler() {
            this.caretMoveHandler();
        },
        
        // Hooks events to the input element
        hookEvents() {
            this.inputEl.addEventListener('keydown', (e: KeyboardEvent) => this.keyDownHandler(e));
            this.inputEl.addEventListener('click', (e: MouseEvent) => this.clickHandler());
            this.inputEl.addEventListener('paste', (e: ClipboardEvent) => this.pasteHandler(e));
            this.inputEl.addEventListener('input', (e: InputEvent) => this.inputHandler(ctx, e));
        },
        
        // Frees events and resources
        cleanup: () => {
            
        }
    }
    
    // Hook events before returning reference
    ctx.hookEvents();
    
    return ctx;
};

const blockLevelElements = new Set([
    'p', 'div', 'section', 'article', 'header', 'footer',
    'blockquote', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
]);

const excludedElements = new Set(['script', 'style']);

function insertTextAtCursor(text: string) {
    const selection = window.getSelection();
    if (!selection.rangeCount) return;

    // Get the current range
    const range = selection.getRangeAt(0);

    // Create a span element to wrap the text
    const span = document.createElement("span");
    span.textContent = text.replace(/\n/g, "\n"); // Replace newlines if necessary

    // Insert the span element at the current cursor position
    range.deleteContents(); // Remove any selected text
    range.insertNode(span);

    // Move the cursor after the inserted content
    range.setStartAfter(span);
    range.setEndAfter(span);
    selection.removeAllRanges();
    selection.addRange(range);
}

function getElementText(el: Node): string {
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
}

function isMentionWord(word: string) {
    if (word == null || word.length == 0) return false;
    return (word[0] == '@' || word[0] == '#');
}

function findTextNodeAndOffset(node: Node, offset: number): { node: Text, offset: number } | null {
    if (node.nodeType === Node.TEXT_NODE) {
        return { node: node as Text, offset };
    }

    // If the node has child nodes, attempt to find a text node among them
    if (node.childNodes.length > 0) {
        let childNode: Node | null = null;

        if (offset < node.childNodes.length) {
            childNode = node.childNodes[offset];
        } else if (node.childNodes.length > 0) {
            childNode = node.childNodes[node.childNodes.length - 1];
            offset = childNode.textContent ? childNode.textContent.length : 0;
        }

        if (childNode) {
            return this.findTextNodeAndOffset(childNode, offset);
        }
    }

    // If the node has siblings, try to find a text node among them
    let sibling: Node | null = node.previousSibling;
    while (sibling) {
        if (sibling.nodeType === Node.TEXT_NODE) {
            const textLength = sibling.textContent ? sibling.textContent.length : 0;
            return { node: sibling as Text, offset: textLength };
        }
        sibling = sibling.previousSibling;
    }

    // If all else fails, traverse up to the parent node
    if (node.parentNode) {
        return this.findTextNodeAndOffset(node.parentNode, offset);
    }

    // No text node found
    return null;
}