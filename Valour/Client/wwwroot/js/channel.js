function OnChannelLoad() {
    $('.textbox-inner').each(function () {
        
    })
    .keydown(function (e) {
        OnChatboxKeydown(e, this);
    })
    .keyup(function (e) {
        OnChatboxKeyup(e, this);
    })
    .keypress(function (e) {
        
    })
    .on("paste", function (e) {
        OnChatboxPaste(e, this);
    })
    .on("input", function (e) {
        OnChatboxUpdate(e, this);
    })
    .on("click", function (e) {
        OnCaretMove(this);
    });

    console.log("Loaded channel.");
}

components = [];

var currentWord = "";

function SetComponent(id, comp) {
    components[id] = comp;
}

function OnChatboxKeydown(e, box) {

    var id = box.id.substring(box.id.length - 1, box.id.length);

    GetCurrentWord(0);

    // Down arrow
    if (e.keyCode == 40) {
        // Prevent up and down arrow from moving caret while
        // the mention menu is open; instead send an event to
        // the mention menu
        if (currentWord[0] == '@') {
            e.preventDefault();
            components[id].invokeMethodAsync('MoveMentionSelect', 1);
        }
        else {
            OnCaretMove(box);
        }
    }
    // Up arrow
    else if (e.keyCode == 38) {
        if (currentWord[0] == '@') {
            e.preventDefault();
            components[id].invokeMethodAsync('MoveMentionSelect', -1);
        }
        else {
            OnCaretMove(box);
        }
    }
    // Left and right arrows
    else if (e.keyCode == 37 || e.keyCode == 38) {

        OnCaretMove(box);
    }
    // Enter was pressed without shift key
    else if (e.keyCode == 13 && !e.shiftKey) {

        // If the mention menu is open this sends off an event to select it rather
        // than submitting the message!
        if (currentWord[0] == '@') {
            e.preventDefault();
            components[id].invokeMethodAsync('MentionSubmit');
        }
        else {

            // prevent default behavior
            e.preventDefault();
            box.innerHTML = "";
            //ResizeTextArea(box);
            components[id].invokeMethodAsync('OnChatboxSubmit');
            components[id].invokeMethodAsync('OnCaretUpdate', "");
        }
    }
    else if (e.keyCode == 9) {
        // If the mention menu is open this sends off an event to select it rather
        // than adding a tab!
        if (currentWord[0] == '@') {
            e.preventDefault();
            components[id].invokeMethodAsync('MentionSubmit');
        }
    }
}

function InjectText(text, id) {
    var box = $("#text-input-" + id)[0];

    insertTextAtCaret(text);
    components[id].invokeMethodAsync('OnChatboxUpdate', box.innerText, "");
}

function OnChatboxKeyup(e, box) {


}

function OnCaretMove(box) {
    var id = box.id.substring(box.id.length - 1, box.id.length);
    components[id].invokeMethodAsync('OnCaretUpdate', GetCurrentWord(1));
}

function OnChatboxUpdate(e, box) {
    var id = box.id.substring(box.id.length - 1, box.id.length);

    var rep = box.innerHTML;

    //console.log(rep);
    //console.log(box.textContent);

    //rep = rep.replace(/&gt;/g, '>');
    //rep = rep.replace(/<br>/g, '\n');

    components[id].invokeMethodAsync('OnChatboxUpdate', box.innerText, GetCurrentWord(0));
}

function OnChatboxPaste(e, box) {
    e.preventDefault();
    var text = (e.originalEvent || e).clipboardData.getData('text/plain');
    document.execCommand("insertHTML", false, text);
}

function GetCurrentWord(off) {
    var range = window.getSelection().getRangeAt(0);
    if (range.collapsed) {
        if (range.endContainer.lastChild != null) {
            text = range.endContainer.lastChild.textContent.substring(0, range.startOffset + 1 - off);
        }
        else {
            text = range.startContainer.textContent.substring(0, range.startOffset + 1 - off);
        }

        currentWord = text.split(/\s+/g).pop();

        return currentWord
    }

    currentWord = '';
    return '';
}

function insertTextAtCaret(text) {
    var sel, range;
    if (window.getSelection) {
        sel = window.getSelection();
        if (sel.getRangeAt && sel.rangeCount) {
            range = sel.getRangeAt(0);

            range.setStart(range.endContainer, range.startOffset - (currentWord.length));

            range.deleteContents();

            var node = document.createTextNode(text + '\u00A0');
            
            range.insertNode(node);

            var nrange = document.createRange();
            nrange.selectNodeContents(node);
            nrange.collapse();

            sel.removeAllRanges();
            sel.addRange(nrange);

            //console.log("Hello");
        }
    } else if (document.selection && document.selection.createRange) {
        document.selection.createRange().text = text;
    }
}