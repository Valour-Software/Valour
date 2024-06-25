//EmojiMart.init({});

export const inputs = {};

export function setup(id, ref) {
    let input = {
        id: id,
        dotnet: ref,
        currentWord: '',
        currentIndex: 0,
        element: document.getElementById('text-input-' + id)
    };

    inputs[id] = input;

    input.element.addEventListener('keydown', (e) => inputKeyDownHandler(e, input));
    input.element.addEventListener('click', () => inputCaretMoveHandler(input));
    input.element.addEventListener('paste', (e) => inputPasteHandler(e, input));
    input.element.addEventListener('input', (e) => inputInputHandler(input, e));
}

// Thank you to https://www.456bereastreet.com/archive/201105/get_element_text_including_alt_text_for_images_with_javascript/
var getElementText = function(el) {
    var text = '';

    // Text node (3) or CDATA node (4) - return its text
    if ( (el.nodeType === 3) || (el.nodeType === 4) ) {
        text = el.nodeValue;

        // If node is an element (1) and an img
    } else if ((el.nodeType === 1) && el.tagName.toLowerCase() == 'img') {
        text = el.dataset.text || el.getAttribute('data-text') || '';

        // If node is a <br> element, add a line break
    } else if ((el.nodeType === 1) && el.tagName.toLowerCase() === 'br') {
        text = '\n';

        // If node is a block-level element, add a line break before and after its content
    } else if ((el.nodeType === 1) && el.tagName.match(/^(p|div|section|article|header|footer|blockquote|pre|h[1-6])$/i)) {
        text = '\n';
        var children = el.childNodes;
        for (var i = 0, l = children.length; i < l; i++) {
            text += getElementText(children[i]);
        }
        text += '\n';

        // Traverse children unless this is a script or style element
    } else if ((el.nodeType === 1) && !el.tagName.match(/^(script|style)$/i)) {
        var children = el.childNodes;
        for (var i = 0, l = children.length; i < l; i++) {
            text += getElementText(children[i]);
        }
    }

    return text;
};


function getCurrentValue(input) {
    return getElementText(input.element);
}

const emojiRegex = /^(\p{Emoji_Presentation}|\p{Emoji}\uFE0F)(\p{Emoji_Modifier_Base}\p{Emoji_Modifier}|\u200D\p{Emoji_Component}|\ufe0f)*\uFE0F?$/u;
function isEmoji(str) {
    return emojiRegex.test(str);
}

// Handles content being input into the input (not confusing at all)
export function inputInputHandler(input, e) {
    input.currentWord = getCurrentWord(0);
    input.dotnet.invokeMethodAsync('OnChatboxUpdate', getCurrentValue(input), input.currentWord);
}

// Handles content being pasted into the input
export function inputPasteHandler(e, input) {
    e.preventDefault();

    // Get plain text representation
    let text = (e.originalEvent || e).clipboardData.getData('text/plain');

    // We need to put the pasted text in a span to keep the newlines
    document.execCommand("insertText", false, text);

    input.currentWord = getCurrentWord(0);
}

// Handles keys being pressed when the input is selected
export function inputKeyDownHandler(e, input) {
    input.currentWord = getCurrentWord(0);
    input.currentIndex = getCursorPos();

    switch (e.keyCode){
        // Down arrow
        case 40: {
            // Prevent up and down arrow from moving caret while
            // the mention menu is open; instead send an event to
            // the mention menu
            if (isMentionWord(input.currentWord)) {
                e.preventDefault();
                input.dotnet.invokeMethodAsync('MoveMentionSelect', 1);
            }
            else {
                inputCaretMoveHandler(input);
            }

            break;
        }
        // Up arrow
        case 38: {
            if (isMentionWord(input.currentWord)) {
                e.preventDefault();
                input.dotnet.invokeMethodAsync('MoveMentionSelect', -1);
            }
            else {
                input.dotnet.invokeMethodAsync('OnUpArrowNonMention');
                inputCaretMoveHandler(input);
            }

            break;
        }
        // Left and right arrows
        case 37:
            inputCaretMoveHandler(input, 2);
            break;
        case 39:
            inputCaretMoveHandler(input);
            break;
        // Enter
        case 13: {
            // If shift key is down, do not submit on enter
            if (e.shiftKey) {
                break;
            }

            // If the mention menu is open this sends off an event to select it rather
            // than submitting the message!
            if (isMentionWord(input.currentWord)) {
                e.preventDefault();
                input.dotnet.invokeMethodAsync('MentionSubmit');
            }
            else {

                // Mobile uses submit button
                if (mobile && !embedded) {
                    break;
                }

                // prevent default behavior
                e.preventDefault();
                submitMessage(input.id);
            }

            break;
        }
        // Tab
        case 9: {
            // If the mention menu is open this sends off an event to select it rather
            // than adding a tab!
            if (isMentionWord(input.currentWord)) {
                e.preventDefault();
                input.dotnet.invokeMethodAsync('MentionSubmit');
            }
        }
        // Escape
        case 27: {
            input.dotnet.invokeMethodAsync('OnEscape');
        }
    }
}

export function setInputContent(inputId, content) {
    const input = inputs[inputId];

    input.element.innerText = content;
}

export function focusInput(id) {
    const input = inputs[id];
    setTimeout(function() {
        input.element.focus();
    }, 0);
}

export function submitMessage(inputId, keepOpen = false) {
    const input = inputs[inputId];

    if (keepOpen) {
        input.element.focus();
    }

    input.element.innerHTML = '';

    // Handle submission of message
    input.dotnet.invokeMethodAsync('OnChatboxSubmit');
    input.dotnet.invokeMethodAsync('OnCaretUpdate', '');
}

// Handles the caret moving 
export function inputCaretMoveHandler(input, off = 0) {
    input.currentWord = getCurrentWord(off);
    input.currentIndex = getCursorPos();
    input.dotnet.invokeMethodAsync('OnCaretUpdate', input.currentWord);
}

export function isMentionWord(word) {
    if (word == null || word.length == 0) return false;
    return (word[0] == '@' || word[0] == '#');
}

export function getCurrentWord(off) {
    let range = window.getSelection().getRangeAt(0);

    // If it's a 'wide' selection (multiple characters)
    if (!range.collapsed){
        return '';
    }

    let text = '';

    if (range.endContainer.lastChild != null) {
        text = range.endContainer.lastChild.textContent.substring(0, range.startOffset - off);
    }
    else {
        text = range.startContainer.textContent.substring(0, range.startOffset - off);
    }

    return text.split(/\s+/g).pop();
}

export function getCursorPos() {
    var sel, range;
    if (window.getSelection) {
        sel = window.getSelection();
        if (sel.getRangeAt && sel.rangeCount) {
            range = sel.getRangeAt(0);
            return range.startOffset;
        }
    }

    return 0;
}

export function selectEnd(id){
    setTimeout(() => {
        inputs[id].element.focus();
        var sel, range;
        if (window.getSelection) {
            sel = window.getSelection();
            if (sel.getRangeAt && sel.rangeCount) {
                range = sel.getRangeAt(0);
                range.selectNodeContents(inputs[id].element);
                range.collapse(false);
            }
        }  
    }, 100);
}

export function injectElement(text, covertext, classlist, stylelist, id) {

    const input = inputs[id];

    if (document.activeElement != input.element) {
        input.element.focus();

        var sel, range;
        if (window.getSelection) {
            sel = window.getSelection();
            if (sel.getRangeAt && sel.rangeCount) {
                console.log(sel);
                console.log(input.currentIndex);
                range = sel.getRangeAt(0);
                range.setStart(range.endContainer, input.currentIndex + 1);
                range.setEnd(range.endContainer, input.currentIndex + 1);
            }
        }
    }

    input.currentWord = getCurrentWord(0);

    var sel, range;
    if (window.getSelection) {
        sel = window.getSelection();
        if (sel.getRangeAt && sel.rangeCount) {
            range = sel.getRangeAt(0);

            range.setStart(range.endContainer, range.startOffset - (input.currentWord.length));

            range.deleteContents();

            var node = document.createTextNode(text);
            var cont = document.createElement('p');
            var empty = document.createTextNode('\u00A0');

            cont.appendChild(node);

            cont.classList = classlist + ' input-magic';
            cont.style = stylelist;

            cont.contentEditable = 'false';

            //cont.style.display = 'none';

            cont.appendChild(node);

            $(cont).attr('data-before', covertext);

            range.insertNode(empty);
            range.insertNode(cont);


            var nrange = document.createRange();
            nrange.selectNodeContents(empty);

            nrange.collapse();

            sel.removeAllRanges();
            sel.addRange(nrange);

            //console.log("Hello");
        }
    } else if (document.selection && document.selection.createRange) {
        document.selection.createRange().text = text;
    }
    
    input.dotnet.invokeMethodAsync('OnChatboxUpdate', getCurrentValue(input), '');
}

export function injectEmoji(text, native, unified, shortcodes, id) {
    const input = inputs[id];
    
    if (document.activeElement != input.element) {
        input.element.focus();
    }
    
    var sel, range;
    if (window.getSelection) {
        sel = window.getSelection();
        if (sel.getRangeAt && sel.rangeCount) {
            range = sel.getRangeAt(0);
            range.deleteContents();
            
            const img = document.createElement('img');
            img.src = 'https://cdn.jsdelivr.net/npm/emoji-datasource-twitter@14.0.0/img/twitter/64/' + unified + '.png';
            
            img.setAttribute('data-text', native);
            img.alt = native;
            img.classList.add('emoji');
            img.style.width = '1em';
            
            range.insertNode(img);
            range.setStartAfter(img);
        }
    } else if (document.selection && document.selection.createRange) {
        document.selection.createRange().text = text;
    }

    input.dotnet.invokeMethodAsync('OnChatboxUpdate', getCurrentValue(input), '');
}

export function OpenUploadFile(windowId){
    document.getElementById(`upload-core-${windowId}`).click();
}