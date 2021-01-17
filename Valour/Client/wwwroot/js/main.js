
// Code for resizeable main windows

document.addEventListener('contextmenu', event => event.preventDefault());

var splitStates = [null, null, null];

function SizeEnable() {

    var man = $('#window-man');

    if (man.children().length > 1) {

        if (splitStates[0] != null) {
            splitStates[0].destroy();
        }

        var split = Split(
            man.children(),
            {
                minSize: [300, 300],
                gutterAlign: 'center',
                gutterSize: 3,
            }
        );

        splitStates[0] = split;

        var col1 = $('#window-col1');

        if (col1.children().length > 1) {

            if (splitStates[1] != null) {
                splitStates[1].destroy();
            }

            split = Split(
                col1.children(),
                {
                    minSize: [300, 300],
                    direction: 'vertical',
                    gutterAlign: 'center',
                    gutterSize: 3,
                }
            );

            splitStates[1] = split;
        }

        var col2 = $('#window-col2');

        if (col2.children().length > 1) {

            if (splitStates[2] != null) {
                splitStates[2].destroy();
            }

            split = Split(
                col2.children(),
                {
                    minSize: [300, 300],
                    direction: 'vertical',
                    gutterAlign: 'center',
                    gutterSize: 3,
                }
            );

            splitStates[2] = split;
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
    

    if (scrollStates[index] === 1) {
        window.scrollTop(window.prop("scrollHeight"));
    }
}

function SetupWindow(index) {
    console.log("Setting up window " + index);
    var window = $("#innerwindow-" + index);

    window.scroll(function () {
        //console.log("Height: " + window.scrollTop());

        // User has reached top of scroll
        if (window.scrollTop() == 0) {

        }
    });
}

// When the user clicks the button, open the modal 
function AddPlanetButtonFunction(element) {
    var modal = document.getElementById("AddPlanetModel");
    modal.style.display = "block";
}

// When the user clicks on <span> (x), close the modal
function AddPlanetModelCloseFunction(element)
{
    var modal = document.getElementById("AddPlanetModel");
    modal.style.display = "none";
}

// When the user clicks anywhere outside of the modal, close it
window.onclick = function(event) {
    var modal = document.getElementById("AddPlanetModel");
    if (event.target == modal) {
        modal.style.display = "none";
    }

    var modal = document.getElementById("AddChannelModel");
    var x = document.getElementsByClassName("channelmodel")
    for (id in x) {
        item = x[id]
        if (event.target == item) {
            item.style.display = "none";
        }
    }
    var modal = document.getElementById("AddCategoryModel");
    if (event.target.id == modal.id) {
        modal.style.display = "none";
    }
    var modal = document.getElementsByClassName("add-channel-button");
    if (event.target.className != "add-channel-button") {
        var modal = document.getElementsByClassName("AddChannelCategoryContextMenu")[0];
        modal.style.display = "none";
    }
}

function AddChannelButtonFunction(element) {
    menu = document.getElementsByClassName("AddChannelCategoryContextMenu")[0]
    var x = document.getElementsByClassName("channelmodel")
    for (id in x) {
        item = x[id]
        if (menu.id == item.id) {
            item.style.display = "block";
        }
    }
}

function AddCategoryButtonFunction() {
    var modal = document.getElementById("AddCategoryModel");
    modal.style.display = "block";
}

function HideContextMenuForChannelCategory(){
    var modal = document.getElementsByClassName("AddChannelCategoryContextMenu")[0];
    modal.style.display = "none";
}

// When the user clicks the button, open the modal 
function AddChannelCategoryContextMenu(event, element) {
    var modal = document.getElementsByClassName("AddChannelCategoryContextMenu")[0];
    modal.id = element.id
    modal.style.display = "block";
    x = event.clientX;
    y = event.clientY;
    modal.style.left = `${x}px`;
    modal.style.top = `${y}px`;
}
