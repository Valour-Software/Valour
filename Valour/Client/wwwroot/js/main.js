
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
    if (event.target != modal) {
        modal.style.display = "none";
    }

    var modal = document.getElementsByClassName("UserContextMenu")[0];
    if (event.target != modal) {
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