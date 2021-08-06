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

function SetComponent(id, comp) {
    components[id] = comp;
}

function OnChatboxKeydown(e, box) {

    var id = box.id.substring(box.id.length - 1, box.id.length);

    // Enter was pressed without shift key
    if (e.keyCode == 13 && !e.shiftKey) {
        // prevent default behavior
        e.preventDefault();
        box.innerHTML = "";
        //ResizeTextArea(box);
        components[id].invokeMethodAsync('OnChatboxSubmit');
        components[id].invokeMethodAsync('OnCaretUpdate', "");
    }
}

function OnChatboxKeyup(e, box) {
    if (e.keyCode == 37 || e.keyCode == 38 ||
        e.keyCode == 39 || e.keyCode == 40) {

        OnCaretMove(box);
    }
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

    var a = '';

    console.log(range);

    if (range.collapsed) {
        if (range.endContainer.lastChild != null) {
            text = range.endContainer.lastChild.textContent.substring(0, range.startOffset + 1 - off);
        }
        else {
            text = range.startContainer.textContent.substring(0, range.startOffset + 1 - off);
        }
        
        return text.split(/\s+/g).pop();
    }
    return '';
}