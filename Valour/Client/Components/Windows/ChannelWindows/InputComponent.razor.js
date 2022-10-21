export const inputs = {};

export function setup(id, ref) {
    let input = {
        dotnet: ref,
        currentWord: '',
        element: document.getElementById('text-input-' + id)
    };

    inputs[id] = input;

    input.element.addEventListener('keydown', (e) => inputKeyDownHandler(e, input));
    input.element.addEventListener('click', () => inputCaretMoveHandler(input));
    input.element.addEventListener('paste', (e) => inputPasteHandler(e, input));
    input.element.addEventListener('input', () => inputInputHandler(input));
}

// Handles content being input into the input (not confusing at all)
export function inputInputHandler(input) {
    input.currentWord = getCurrentWord(0);
    input.dotnet.invokeMethodAsync('OnChatboxUpdate', input.element.innerText, input.currentWord);
}

// Handles content being pasted into the input
export function inputPasteHandler(e, input) {
    e.preventDefault();

    // Get plain text representation
    let text = (e.originalEvent || e).clipboardData.getData('text/plain');

    // We need to put the pasted text in a span to keep the newlines
    text = `<span style="white-space: pre;">`+text+`</span>`
    document.execCommand("insertHTML", false, text);

    input.currentWord = getCurrentWord(0);
}

// Handles keys being pressed when the input is selected
export function inputKeyDownHandler(e, input) {
    input.currentWord = getCurrentWord(0);

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
                // prevent default behavior
                e.preventDefault();
                input.element.innerHTML = '';
                
                // Handle submission of message
                input.dotnet.invokeMethodAsync('OnChatboxSubmit');
                input.dotnet.invokeMethodAsync('OnCaretUpdate', '');
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
    }
}

// Handles the caret moving 
export function inputCaretMoveHandler(input, off = 0) {
    input.currentWord = getCurrentWord(off);
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
        text = range.endContainer.lastChild.textContent.substring(0, range.startOffset + 1 - off);
    }
    else {
        text = range.startContainer.textContent.substring(0, range.startOffset + 1 - off);
    }

    return text.split(/\s+/g).pop();
}

export function injectElement(text, covertext, classlist, stylelist, id) {
    const input = inputs[id];

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
    
    input.dotnet.invokeMethodAsync('OnChatboxUpdate', input.element.innerText, '');
}