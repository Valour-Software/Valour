
// Code for resizeable main windows

document.addEventListener('contextmenu', event => EventForContextMenu(event));

function EventForContextMenu(event) {
    if (event.target.className.includes("EnableRightCLick")) {
        return;
    }
    else {
        event.preventDefault()
    }
}

var splitStates = [null, null, null];

var mobile = false;

function FixClip() {
    $("html").addClass("full-screen");
    $("body").addClass("full-screen");
}

function FitMobile() {
    var sidebar1 = $(".sidebar");
    var sidebar2 = $(".sidebar-2");
    var channel = $(".channel-and-topbar");
    var topbar = $(".topbar");

    (function (a) { if (/(android|bb\d+|meego).+mobile|avantgo|bada\/|blackberry|blazer|compal|elaine|fennec|hiptop|iemobile|ip(hone|od)|iris|kindle|lge |maemo|midp|mmp|mobile.+firefox|netfront|opera m(ob|in)i|palm( os)?|phone|p(ixi|re)\/|plucker|pocket|psp|series(4|6)0|symbian|treo|up\.(browser|link)|vodafone|wap|windows ce|xda|xiino/i.test(a) || /1207|6310|6590|3gso|4thp|50[1-6]i|770s|802s|a wa|abac|ac(er|oo|s\-)|ai(ko|rn)|al(av|ca|co)|amoi|an(ex|ny|yw)|aptu|ar(ch|go)|as(te|us)|attw|au(di|\-m|r |s )|avan|be(ck|ll|nq)|bi(lb|rd)|bl(ac|az)|br(e|v)w|bumb|bw\-(n|u)|c55\/|capi|ccwa|cdm\-|cell|chtm|cldc|cmd\-|co(mp|nd)|craw|da(it|ll|ng)|dbte|dc\-s|devi|dica|dmob|do(c|p)o|ds(12|\-d)|el(49|ai)|em(l2|ul)|er(ic|k0)|esl8|ez([4-7]0|os|wa|ze)|fetc|fly(\-|_)|g1 u|g560|gene|gf\-5|g\-mo|go(\.w|od)|gr(ad|un)|haie|hcit|hd\-(m|p|t)|hei\-|hi(pt|ta)|hp( i|ip)|hs\-c|ht(c(\-| |_|a|g|p|s|t)|tp)|hu(aw|tc)|i\-(20|go|ma)|i230|iac( |\-|\/)|ibro|idea|ig01|ikom|im1k|inno|ipaq|iris|ja(t|v)a|jbro|jemu|jigs|kddi|keji|kgt( |\/)|klon|kpt |kwc\-|kyo(c|k)|le(no|xi)|lg( g|\/(k|l|u)|50|54|\-[a-w])|libw|lynx|m1\-w|m3ga|m50\/|ma(te|ui|xo)|mc(01|21|ca)|m\-cr|me(rc|ri)|mi(o8|oa|ts)|mmef|mo(01|02|bi|de|do|t(\-| |o|v)|zz)|mt(50|p1|v )|mwbp|mywa|n10[0-2]|n20[2-3]|n30(0|2)|n50(0|2|5)|n7(0(0|1)|10)|ne((c|m)\-|on|tf|wf|wg|wt)|nok(6|i)|nzph|o2im|op(ti|wv)|oran|owg1|p800|pan(a|d|t)|pdxg|pg(13|\-([1-8]|c))|phil|pire|pl(ay|uc)|pn\-2|po(ck|rt|se)|prox|psio|pt\-g|qa\-a|qc(07|12|21|32|60|\-[2-7]|i\-)|qtek|r380|r600|raks|rim9|ro(ve|zo)|s55\/|sa(ge|ma|mm|ms|ny|va)|sc(01|h\-|oo|p\-)|sdk\/|se(c(\-|0|1)|47|mc|nd|ri)|sgh\-|shar|sie(\-|m)|sk\-0|sl(45|id)|sm(al|ar|b3|it|t5)|so(ft|ny)|sp(01|h\-|v\-|v )|sy(01|mb)|t2(18|50)|t6(00|10|18)|ta(gt|lk)|tcl\-|tdg\-|tel(i|m)|tim\-|t\-mo|to(pl|sh)|ts(70|m\-|m3|m5)|tx\-9|up(\.b|g1|si)|utst|v400|v750|veri|vi(rg|te)|vk(40|5[0-3]|\-v)|vm40|voda|vulc|vx(52|53|60|61|70|80|81|83|85|98)|w3c(\-| )|webc|whit|wi(g |nc|nw)|wmlb|wonu|x700|yas\-|your|zeto|zte\-/i.test(a.substr(0, 4))) mobile = true; })(navigator.userAgent || navigator.vendor || window.opera);

    if (!mobile) {
        return;
    }

    console.log("Detected mobile.");

    //sidebar1.toggle(false);

    $(".add-window-button").toggle(false);

    sidebar1.addClass("sidebar-mobile");
    sidebar2.addClass("sidebar-2-mobile");
    channel.addClass("channel-and-topbar-mobile");

    channel.css("min-width", screen.width);

    //sidebar2.toggle(false);
    topbar.toggle(false);
}

function OnRightSwipe() {
    if (mobile) {

        var sidebar1 = $(".sidebar");
        var sidebar2 = $(".sidebar-2");

        sidebar1.addClass("sidebar-mobile-active");
        sidebar2.addClass("sidebar-2-mobile-active");

        //sidebar1.toggle(true);
    }
}

function OnLeftSwipe() {
    if (mobile) {

        var sidebar1 = $(".sidebar");
        var sidebar2 = $(".sidebar-2");

        var w1 = sidebar1.width();
        var w2 = sidebar2.width();

        //sidebar1.toggle(false);
        sidebar1.removeClass("sidebar-mobile-active");
        sidebar2.removeClass("sidebar-2-mobile-active");
    }
}

/* SWIPE HANDLER */
document.addEventListener('touchstart', handleTouchStart, false);
document.addEventListener('touchmove', handleTouchMove, false);

var xDown = null;
var yDown = null;

function getTouches(evt) {
    return evt.touches ||             // browser API
        evt.originalEvent.touches; // jQuery
}

function handleTouchStart(evt) {
    const firstTouch = getTouches(evt)[0];
    xDown = firstTouch.clientX;
    yDown = firstTouch.clientY;
};

function handleTouchMove(evt) {
    if (!xDown || !yDown) {
        return;
    }

    var xUp = evt.touches[0].clientX;
    var yUp = evt.touches[0].clientY;

    var xDiff = xDown - xUp;
    var yDiff = yDown - yUp;

    if (Math.abs(xDiff) > Math.abs(yDiff)) {/*most significant*/
        if (xDiff > 5) {
            /* left swipe */
            OnLeftSwipe();
        }
        else if (xDiff < -5) {
            /* right swipe */
            OnRightSwipe();
        }
    } else {
        if (yDiff > 0) {
            /* up swipe */
        } else {
            /* down swipe */
        }
    }
    /* reset values */
    xDown = null;
    yDown = null;
};

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

// Set the name of the hidden property and the change event for visibility
var hidden, visibilityChange;
if (typeof document.hidden !== "undefined") { // Opera 12.10 and Firefox 18 and later support
    hidden = "hidden";
    visibilityChange = "visibilitychange";
} else if (typeof document.msHidden !== "undefined") {
    hidden = "msHidden";
    visibilityChange = "msvisibilitychange";
} else if (typeof document.webkitHidden !== "undefined") {
    hidden = "webkitHidden";
    visibilityChange = "webkitvisibilitychange";
}

var videoElement = document.getElementById("videoElement");

// If the page is hidden, pause the video;
// if the page is shown, play the video
function handleVisibilityChange() {
    if (document[hidden]) {
        // Nothing yet
    } else {
        DotNet.invokeMethodAsync('Valour.Client', 'OnRefocus');
    }
}

// Warn if the browser doesn't support addEventListener or the Page Visibility API
if (typeof document.addEventListener === "undefined" || hidden === undefined) {
    console.log("This demo requires a browser, such as Google Chrome or Firefox, that supports the Page Visibility API.");
} else {
    // Handle page visibility change
    document.addEventListener(visibilityChange, handleVisibilityChange, false);

}

var scrollStates = [1, 1, 1, 1];
var oldScrollSize = [0, 0, 0, 0];

// Automagically scroll windows down
function UpdateScrollPosition(index) {
    var window = $("#innerwindow-" + index);

    oldScrollSize[index] = window.prop("scrollHeight");

    console.log("Saving scroll state " + oldScrollSize[index]);
}

function ScaleScrollPosition(index) {
    var window = $("#innerwindow-" + index);

    window.scrollTop(window.prop("scrollHeight") - oldScrollSize[index]);
}

function IsAtBottom(index) {
    return (scrollStates[index] === 1);
}

// Automagically scroll windows down
function ScrollWindowBottom(index) {
    var window = $("#innerwindow-" + index);

    if (scrollStates[index] === 1) {
        //console.log("hi there");
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
            DotNet.invokeMethodAsync('Valour.Client', 'OnScrollTopInvoke', index);
        }

        if (Math.abs(Math.abs(window.prop("scrollHeight") - window.scrollTop()) -
            Math.abs(window.outerHeight()))
            < 25) {

            scrollStates[index] = 1;
            console.log("Within snap range.");
        }
        else {
            scrollStates[index] = 0;
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

    var modal = document.getElementById("EditPlanetModal");
    if (event.target == modal) {
        modal.style.display = "none";
    }

    var x = document.getElementsByClassName("channelmodel")
    for (id in x) {
        item = x[id]
        if (event.target == item) {
            item.style.display = "none";
        }
    }
    var x = document.getElementsByClassName("categorymodel")
    for (id in x) {
        item = x[id]
        if (event.target == item) {
            item.style.display = "none";
        }

    }

    var modal = document.getElementsByClassName("add-channel-button");
    if (event.target.className != "add-channel-button") {
        var modal = document.getElementsByClassName("AddChannelCategoryContextMenu")[0];

        if (modal != null) {
            modal.style.display = "none";
        }
    }

    var modal = document.getElementsByClassName("ChannelListItemContextMenu")[0];
    if (modal && event.target != modal) {
        modal.style.display = "none";
    }

    var modal = document.getElementsByClassName("UserContextMenu")[0];
    if (modal && event.target != modal) {
        modal.style.display = "none";
    }

    var modal = document.getElementsByClassName("BanModel")[0];
    if (modal && event.target == modal) {
        modal.style.display = "none";
    }
}

function OpenEditPlanetModal() {

    console.log("Edit Planet Modal triggered.");

    var x = document.getElementsByClassName("edit-planet-modal")
    for (id in x) {
        item = x[id]

        if (item.style != null) {
            item.style.display = "block";
        }
    }
}

function AddChannelButtonFunction() {
    x = document.getElementById("CreateChannel")
    x.style.display = "block"
}

function AddCategoryButtonFunction() {
    x = document.getElementById("CreateCategory")
    x.style.display = "block"
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
    ParentIdForModel = element.id
}

function GetParentId() {
    return parseInt(ParentIdForModel)
}

function UserContextMenu(event, element) {
    var modal = document.getElementsByClassName("UserContextMenu")[0];
    modal.style.display = "block";
    x = event.clientX;
    y = event.clientY;
    modal.style.left = `${x}px`;
    modal.style.top = `${y}px`;
    data = element.id.split(",")
    SelectedUserId = parseInt(data[0])
    PlanetId = parseInt(data[1])
}

function HideUserContextMenu(){
    var modal = document.getElementsByClassName("UserContextMenu")[0];
    modal.style.display = "none";
}

function KickUser() {
    fetch(`/User/KickUser?token=${SecretKey}&Planet_Id=${PlanetId}&UserId=${UserId}&id=${parseInt(SelectedUserId)}`)
        .then(data => {
            console.log(data)
        })
}

function BanUser() {
    x = document.getElementById("BanModel")
    x.style.display = "block"
}

function GetSelectedUserId() {
    return parseInt(SelectedUserId)
}

function ChannelListItemContextMenu(event, element) {
    var modal = document.getElementsByClassName("ChannelListItemContextMenu")[0];
    modal.id = element.id
    modal.style.display = "block";
    x = event.clientX;
    y = event.clientY;
    modal.style.left = `${x}px`;
    modal.style.top = `${y}px`;
    ChannelListItemId = element.id
    while (true) {
        if (element.className.includes("channel") == true | element.className.includes("category") == true) {
            break
        }
        element = element.parentNode
        if (element == null) {
            return null;
        }
    }
    if (element.className.includes("channel")) {
        IsCategory = false
    }
    if (element.className.includes("category")){
        IsCategory = true
    }
}

function HideContextMenuForChanneListItem(){
    var modal = document.getElementsByClassName("ChannelListItemContextMenu")[0];
    modal.style.display = "none";
}

function DeleteChannelListItem() {
    console.log(`Id: ${ChannelListItemId} IsCategory: ${IsCategory}`)

    if (IsCategory === false) {
        fetch(`/Channel/Delete?token=${SecretKey}&UserId=${UserId}&id=${parseInt(ChannelListItemId)}`)
            .then(data => {
                console.log(data)
            })
        
    }
    else {
        fetch(`/Category/Delete?token=${SecretKey}&UserId=${UserId}&id=${parseInt(ChannelListItemId)}`)
            .then(data => {
                console.log(data)
            })
            
    }

}


// Code for Reordering categories and channels
const setDraggedOver = (e) => {
      e.preventDefault();
      draggedOver = e.target
}
    
const setDragging = (e) =>{
    dragging = e.target
    while (true) {
        if (dragging.className.includes("channel") == true | dragging.className.includes("category") == true) {
            break
        }
        dragging = dragging.parentNode
        if (dragging == null) {
            return null;
        }
    }
}

async function postData(url = '', data = {}) {
    // Default options are marked with *
    const response = await fetch(url, {
      method: 'POST', // *GET, POST, PUT, DELETE, etc.
      headers: {
        'Accept': 'application/json, text/plain',
        'Content-Type': 'application/json;charset=UTF-8'
        },
      body:  JSON.stringify(data) // body data type must match "Content-Type" header
    });
    return response.json(); // parses JSON response into native JavaScript objects
  }

const Drop = (e) =>{
    e.preventDefault();
    if (dragging == null) {
        return null
    }
    target = e.target
    beforeelement = null
    while (target.className.includes("channel-list") == false && target.className.includes("category-list") == false ) {
        if (target.className.includes("channel")) {
            beforeelement = target
        }
        if (target.className.includes("category-list") == false && dragging.className.includes("category") == true) {
            beforeelement = target
        }
        target = target.parentNode
        if (target == null) {
            return null;
        }
    }
    if (target.className.includes("channel-list") == false && target.className.includes("category-list") == false) {
        beforeelement = null;
    }
    node = target.parentNode
    if (target.className.includes("category") == true && dragging.className.includes("category") == false) {
        return null;
    }
    TopLevel = false
    if (target.className.includes("category-list") == true && dragging.className.includes("category") == true) {
        id = dragging.id 
        fetch(`/Category/SetParentId?token=${SecretKey}&UserId=${UserId}&id=${parseInt(dragging.id)}&parentId=0`)
        .then(data => {
                console.log(data)
        })
        TopLevel = true
    }
    else {
        if (target == null) {
            return null;
        }
        if (beforeelement == dragging) {
            return null;
        }
        parentid = dragging.parentNode
        parentid = parentid.parentNode.id
        categoryid = target.parentNode.id
        if (categoryid != parentid) {
            if (dragging.className.includes("channel")) {
                fetch(`/Channel/SetParentId?token=${SecretKey}&UserId=${UserId}&id=${parseInt(dragging.id)}&parentId=${parseInt(categoryid)}`)
                .then(data => {
                    console.log(data)
                })
            }
            else {
                fetch(`/Category/SetParentId?token=${SecretKey}&UserId=${UserId}&id=${parseInt(dragging.id)}&parentId=${parseInt(categoryid)}`)
                .then(data => {
                    console.log(data)
                })
            }
        }
    }
    if (target == null) {
        return null;
    }
    if (beforeelement == dragging) {
        return null;
    }
   // dragging.parentNode.removeChild(dragging);
    parentid = dragging.parentNode
    parentid = parentid.parentNode.id
    categoryid = target.parentNode.id
    if (categoryid == parentid || TopLevel) {
        list = Array.prototype.slice.call( target.children )
        var index1 = list.indexOf(dragging);
        var index2 = list.indexOf(beforeelement);
        list.splice(index1, 1)
        list.splice(index2, 0, dragging)
        target.innerHTML = ""
        for (i in list) {
            item = list[i]
            target.append(item)
        }
    }
    else {
        dragging.parentNode.removeChild(dragging);
        list = Array.prototype.slice.call( target.children )
        var index2 = list.indexOf(beforeelement);
        list.splice(index2, 0, dragging)
        target.innerHTML = ""
        for (i in list) {
            item = list[i]
            target.append(item)
        }
    }
    index = 0
    var data = {}
    for (i in target.children) {
        item = target.children[i]
        if (item.className == null) {
            continue
        }
        if (item.className.includes("channel")) {
            data[index] = [parseInt(item.id), 0]
            index += 1
        }
        if (item.className.includes("category")) {
            data[index] = [parseInt(item.id), 1]
            index += 1
        }
    }
    postData(`/Planet/UpdateOrder?token=${SecretKey}&UserId=${UserId}`, data)
        .then(out => {
            console.log(out)
        })
    dragging = null
    return null;
}

function SetSecretKey(key, id) {
    SecretKey = key
    UserId = id
}

function SetDate() {
    if (document.getElementById('ageVeriInput')) document.getElementById('ageVeriInput').valueAsDate = new Date()
}
