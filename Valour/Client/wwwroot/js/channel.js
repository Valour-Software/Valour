function OnChannelLoad() {
    $('textarea').each(function () {
        ResizeTextArea(this);
    })
    .keydown(function (e) {
        OnChatboxKeypress(e, this);
    })
    .on("input", function (e) {
        ResizeTextArea(this);
    });

    console.log("Loaded channel.");
}


function ResizeTextArea(box) {

    box.style.height = 'auto';

    var sh = box.scrollHeight;

    box.style.height = (sh) + 'px';
}

function ResizeTextAreaById(id) {

    var t = $('#' + id)[0];

    ResizeTextArea(t);
}

function OnChatboxKeypress(e, box) {
    // Enter was pressed without shift key
    if (e.keyCode == 13 && !e.shiftKey) {
        // prevent default behavior
        e.preventDefault();
        box.value = "";
        ResizeTextArea(box);
    }
}