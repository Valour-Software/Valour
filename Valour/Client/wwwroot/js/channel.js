function OnChannelLoad() {
    $('.textbox-inner').each(function () {
        
    })
    .keydown(function (e) {
        OnChatboxKeydown(e, this);
    })
    .keypress(function (e) {
        
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
    components[id].invokeMethodAsync('OnChatboxUpdate', box.innerHTML);
}