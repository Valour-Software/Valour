function OnChannelLoad() {
    $('.textbox-inner').each(function () {
        
    })
    .keydown(function (e) {
        OnChatboxKeydown(e, this);
    })
    .keypress(function (e) {
        
    })
    .on("paste", function (e) {
        OnChatboxPaste(e, this);
    })
    .on("input", function (e) {
        OnChatboxUpdate(e, this);
    });

    console.log("Loaded channel.");
}

components = [];

function SetComponent(id, comp) {
    components[id] = comp;
}

function OnChatboxKeydown(e, box) {
    // Enter was pressed without shift key
    if (e.keyCode == 13 && !e.shiftKey) {
        // prevent default behavior
        e.preventDefault();
        box.innerHTML = "";
        //ResizeTextArea(box);

        var id = box.id.substring(box.id.length - 1, box.id.length);
        components[id].invokeMethodAsync('OnChatboxSubmit');
    }
}

function OnChatboxUpdate(e, box) {
    var id = box.id.substring(box.id.length - 1, box.id.length);

    var s = box.innerHTML;
    var rep = s.replace(/<br>/g, '\n');
    rep = rep.replace(/&gt;/g, '>');

    components[id].invokeMethodAsync('OnChatboxUpdate', rep);
}

function OnChatboxPaste(e, box) {
    e.preventDefault();
    var text = (e.originalEvent || e).clipboardData.getData('text/plain');
    document.execCommand("insertHTML", false, text);
}