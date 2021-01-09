
// Code for resizeable main windows

function SizeEnable() {

    var man = $('#window-man');

    if (man.children().length > 1) {
        var split = Split(
            man.children(),
            {
                minSize: [300, 300],
                gutterAlign: 'center',
                gutterSize: 3,
            }
        );

        var col1 = $('#window-col1');

        if (col1.children().length > 1) {
            split = Split(
                col1.children(),
                {
                    minSize: [300, 300],
                    direction: 'vertical',
                    gutterAlign: 'center',
                    gutterSize: 3,
                }
            );
        }

        var col2 = $('#window-col2');

        if (col2.children().length > 1) {
            split = Split(
                col2.children(),
                {
                    minSize: [300, 300],
                    direction: 'vertical',
                    gutterAlign: 'center',
                    gutterSize: 3,
                }
            );
        }
    }
}

var scrollStates = [1, 1, 1, 1];

function SetScrollState(index) {
    var window = $("#innerwindow-" + index);
    if (Math.abs(Math.abs(window.prop("scrollHeight") - window.scrollTop()) -
                 Math.abs(window.outerHeight()))
                 < 25) {

        scrollStates[index] = 1;
    }
    else {
        scrollStates[index] = 0;
    }
}

// Automagically scroll windows down
function ScrollWindowBottom(index) {
    var window = $("#innerwindow-" + index);

    if (scrollStates[index] === 1) {
        window.scrollTop(window.prop("scrollHeight"));
    }
}