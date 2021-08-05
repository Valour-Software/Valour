function OnChannelLoad() {
    $('textarea').each(function () {
        ResizeTextArea(this);
    }).on("input", function () {
        ResizeTextArea(this);
    });

    function ResizeTextArea(box) {

        box.style.height = 'auto';

        var sh = box.scrollHeight;

        box.style.height = (sh) + 'px';
    }

    console.log("Loaded channel.");
}