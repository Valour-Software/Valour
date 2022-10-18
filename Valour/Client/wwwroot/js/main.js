document.addEventListener('contextmenu', event => event.preventDefault());

window.clipboardCopy = {
    copyText: function (text) {
        navigator.clipboard.writeText(text).then(function () {
            // alert("Copied to clipboard!");
        })
        .catch(function (error) {
            alert(error);
        });
    }
};

var splitStates = [null, null, null];

var mobile = false;
var embedded = window.location.href.includes('embedded=true');

function GetIsEmbedded() {
    return embedded;
}

function FixClip() {
    $("html").addClass("full-screen");
    $("body").addClass("full-screen");
}

var swipeState = 0;

function FitMobile() {
    var sidebar1 = $(".sidebar");
    var sidebar2 = $(".sidebar-2");
    var sidebarMenu = $(".sidebar-menu");

    var channel = $(".channel-and-topbar");
    var topbar = $(".topbar");

    (function (a) { if (/(android|bb\d+|meego).+mobile|avantgo|bada\/|blackberry|blazer|compal|elaine|fennec|hiptop|iemobile|ip(hone|od)|iris|kindle|lge |maemo|midp|mmp|mobile.+firefox|netfront|opera m(ob|in)i|palm( os)?|phone|p(ixi|re)\/|plucker|pocket|psp|series(4|6)0|symbian|treo|up\.(browser|link)|vodafone|wap|windows ce|xda|xiino/i.test(a) || /1207|6310|6590|3gso|4thp|50[1-6]i|770s|802s|a wa|abac|ac(er|oo|s\-)|ai(ko|rn)|al(av|ca|co)|amoi|an(ex|ny|yw)|aptu|ar(ch|go)|as(te|us)|attw|au(di|\-m|r |s )|avan|be(ck|ll|nq)|bi(lb|rd)|bl(ac|az)|br(e|v)w|bumb|bw\-(n|u)|c55\/|capi|ccwa|cdm\-|cell|chtm|cldc|cmd\-|co(mp|nd)|craw|da(it|ll|ng)|dbte|dc\-s|devi|dica|dmob|do(c|p)o|ds(12|\-d)|el(49|ai)|em(l2|ul)|er(ic|k0)|esl8|ez([4-7]0|os|wa|ze)|fetc|fly(\-|_)|g1 u|g560|gene|gf\-5|g\-mo|go(\.w|od)|gr(ad|un)|haie|hcit|hd\-(m|p|t)|hei\-|hi(pt|ta)|hp( i|ip)|hs\-c|ht(c(\-| |_|a|g|p|s|t)|tp)|hu(aw|tc)|i\-(20|go|ma)|i230|iac( |\-|\/)|ibro|idea|ig01|ikom|im1k|inno|ipaq|iris|ja(t|v)a|jbro|jemu|jigs|kddi|keji|kgt( |\/)|klon|kpt |kwc\-|kyo(c|k)|le(no|xi)|lg( g|\/(k|l|u)|50|54|\-[a-w])|libw|lynx|m1\-w|m3ga|m50\/|ma(te|ui|xo)|mc(01|21|ca)|m\-cr|me(rc|ri)|mi(o8|oa|ts)|mmef|mo(01|02|bi|de|do|t(\-| |o|v)|zz)|mt(50|p1|v )|mwbp|mywa|n10[0-2]|n20[2-3]|n30(0|2)|n50(0|2|5)|n7(0(0|1)|10)|ne((c|m)\-|on|tf|wf|wg|wt)|nok(6|i)|nzph|o2im|op(ti|wv)|oran|owg1|p800|pan(a|d|t)|pdxg|pg(13|\-([1-8]|c))|phil|pire|pl(ay|uc)|pn\-2|po(ck|rt|se)|prox|psio|pt\-g|qa\-a|qc(07|12|21|32|60|\-[2-7]|i\-)|qtek|r380|r600|raks|rim9|ro(ve|zo)|s55\/|sa(ge|ma|mm|ms|ny|va)|sc(01|h\-|oo|p\-)|sdk\/|se(c(\-|0|1)|47|mc|nd|ri)|sgh\-|shar|sie(\-|m)|sk\-0|sl(45|id)|sm(al|ar|b3|it|t5)|so(ft|ny)|sp(01|h\-|v\-|v )|sy(01|mb)|t2(18|50)|t6(00|10|18)|ta(gt|lk)|tcl\-|tdg\-|tel(i|m)|tim\-|t\-mo|to(pl|sh)|ts(70|m\-|m3|m5)|tx\-9|up(\.b|g1|si)|utst|v400|v750|veri|vi(rg|te)|vk(40|5[0-3]|\-v)|vm40|voda|vulc|vx(52|53|60|61|70|80|81|83|85|98)|w3c(\-| )|webc|whit|wi(g |nc|nw)|wmlb|wonu|x700|yas\-|your|zeto|zte\-/i.test(a.substr(0, 4))) mobile = true; })(navigator.userAgent || navigator.vendor || window.opera);

    // Special embedded check
    if (embedded) {
        mobile = true;
    }

    if (!mobile) {
        return;
    }

    console.log("Detected mobile.");

    //sidebar1.toggle(false);

    $("body").addClass('mobile')

    $(".add-window-button").toggle(false);

    sidebarMenu.removeClass("sidebar-menu");
    sidebarMenu.addClass("sidebar-menu-mobile");

    sidebar1.removeClass("sidebar");
    sidebar1.addClass("sidebar-mobile");

    if (embedded) {
        sidebar1.addClass("sidebar-embedded");
    }

    channel.addClass("channel-and-topbar-mobile");

    //sidebar2.toggle(false);
    topbar.toggle(false);
}

function IsMobile() {
    return mobile;
}

function IsEmbedded() {
    return embedded;
}

function OpenSidebar() {
    swipeState = 1;
    HandleSwipeState();
}

function CloseSidebar() {
    swipeState = 0;
    HandleSwipeState();
}

function HandleSwipeState() {

    var sidebar1 = $(".sidebar-mobile");
    var sidebarMenu = $(".sidebar-menu-mobile");

    console.log("Swipe state is now " + swipeState);

    if (swipeState === 0) {
        sidebarMenu.removeClass("sidebar-menu-mobile-active");
    }
    else if (swipeState === 1) {
        sidebar1.removeClass("sidebar-mobile-expanded");
        sidebarMenu.addClass("sidebar-menu-mobile-active");

    }
    else if (swipeState === 2) {
        sidebar1.addClass("sidebar-mobile-expanded");
    }
}

function OnRightSwipe() {
    if (mobile) {

        swipeState++;
        if (swipeState > 2) {
            swipeState = 2;
        }

        HandleSwipeState();
    }
}

function OnLeftSwipe() {

    swipeState--;
    if (swipeState < 0) {
        swipeState = 0;
    }

    if (mobile) {
        HandleSwipeState();
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

// Visbility change handler
if (document.addEventListener) {
    document.addEventListener("visibilitychange", function () {
        if (document.visibilityState == 'hidden') {
            // page is hidden
        } else {
            DotNet.invokeMethodAsync('Valour.Client', 'OnRefocus');
        }
    });
}

// Web lock
// The idea here is to *force* the tab to stay active

// Capture promise control functions:
let resolve, reject;
const p = new Promise((res, rej) => { resolve = res; reject = rej; });

// Request the lock:
navigator.locks.request('valour_lock', lock => {
    // Lock is acquired.

    return p;
    // Now lock will be held until either resolve() or reject() is called.
});


var oldScrollHeight = {};
var oldScrollTop = {};
var stickyStates = {};
var lastTopLoadPos = {};

// Automagically scroll windows down
function UpdateScrollPosition(id) {
    var window = document.getElementById('innerwindow-' + id);
    oldScrollHeight[id] = window.scrollHeight;
    oldScrollTop[id] = window.scrollTop;
}

function ScaleScrollPosition(id) {
    var window = document.getElementById('innerwindow-' + id);
    window.scrollTop = oldScrollTop[id] + (window.scrollHeight - oldScrollHeight[id]);
}

function IsAtBottom(id) {
    var window = document.getElementById('innerwindow-' + id);
    var scrollUp = window.scrollHeight - (window.scrollTop + window.getBoundingClientRect().height);
    return scrollUp < 75;
}

function OnScrollChannel(id) {
    if (IsAtBottom(id)) {
        stickyStates[id] = true;
    }
    else {
        stickyStates[id] = false;
    }
}

// Automagically scroll windows down
function ScrollWindowBottom(id, force) {
    var window = document.getElementById('innerwindow-' + id);

    if (force || stickyStates[id]) {
        window.scrollTop = window.scrollHeight;
        stickyStates[id] = true;
    }
}

function ScrollWindowBottomAnim(id) {
    var window = $('#innerwindow-' + id);
    window.animate({ scrollTop: window.prop("scrollHeight") }, "fast");
}

var scrollUpTimer = Date.now();

function SetupWindow(id) {
    var window = $("#innerwindow-" + id); 
    window.scroll(function () {

        // Scrollbar is actually visible
        if (window.prop('scrollHeight') > window.height()) {

            // User has reached top of scroll
            if (window.scrollTop() == 0 && scrollUpTimer < (Date.now() - 50)) {
                console.log("hello");
                scrollUpTimer = new Date();
                DotNet.invokeMethodAsync('Valour.Client', 'OnScrollTopInvoke', id);
            }
        }
    });
}

async function postData(url = '', data = {}) {
    // Default options are marked with *
    const response = await fetch(url, {
        method: 'POST', // *GET, POST, PUT, DELETE, etc.
        headers: {
            'Accept': 'application/json, text/plain',
            'Content-Type': 'application/json;charset=UTF-8'
        },
        body: JSON.stringify(data) // body data type must match "Content-Type" header
    });
    return response.json(); // parses JSON response into native JavaScript objects
}

function httpGet(theUrl) {
    var xmlHttp = new XMLHttpRequest();
    xmlHttp.open("GET", theUrl, false); // false for synchronous request
    xmlHttp.send(null);
    return JSON.parse(xmlHttp.responseText);
}

function SetDate() {
    if (document.getElementById('ageVeriInput')) document.getElementById('ageVeriInput').valueAsDate = new Date()
}

window.blazorFuncs = {
    registerClient: function (caller) {
        window['updateAvailable']
            .then(isAvailable => {
                if (isAvailable) {
                    caller.invokeMethodAsync("OnServiceUpdateAvailable").then(r => console.log(r));
                }
                else {
                    caller.invokeMethodAsync("OnServiceUpdateUnvailable");
                }
            });
    }
};


var chosenColor = [255, 255, 255, 1];
var setColorHex = '#ffffff';

function SetColorPickerColor(hex) {
    setColorHex = hex;

    var rgb = hexToRGB(hex, 1);

    chosenColor[0] = rgb[0];
    chosenColor[1] = rgb[1];
    chosenColor[2] = rgb[2];
    chosenColor[3] = rgb[3];
}

// Turns hex to rgb
function hexToRGB(hex, alpha) {
    var r = parseInt(hex.slice(1, 3), 16),
        g = parseInt(hex.slice(3, 5), 16),
        b = parseInt(hex.slice(5, 7), 16);

    return [r, g, b, alpha];
}

pickr = null;

function Log(message, color) {
    console.log("%c" + message, 'color: ' + color);
}

function SetupColorPicker() {

    console.log("Setting up color picker...");

    pickr = Pickr.create({
        el: '.color-picker',
        theme: 'nano', // or 'monolith', or 'nano'

        default: setColorHex,

        swatches: [
            'rgba(255, 255, 255, 1)',
            'rgba(244, 67, 54, 1)',
            'rgba(233, 30, 99, 1)',
            'rgba(156, 39, 176, 1)',
            'rgba(103, 58, 183, 1)',
            'rgba(63, 81, 181, 1)',
            'rgba(33, 150, 243, 0.75)',
            'rgba(3, 169, 244, 0.7)',
            'rgba(0, 188, 212, 0.7)',
            'rgba(0, 150, 136, 0.75)',
            'rgba(76, 175, 80, 0.8)',
            'rgba(139, 195, 74, 0.85)',
            'rgba(205, 220, 57, 0.9)',
            'rgba(255, 235, 59, 0.95)',
            'rgba(255, 193, 7, 1)'
        ],

        components: {

            // Main components
            preview: true,
            opacity: true,
            hue: true,

            // Input / output Options
            interaction: {
                hex: true,
                rgba: true,
                hsla: true,
                hsva: true,
                cmyk: true,
                input: true,
                clear: true,
                save: true
            }
        }
    });

    pickr.on('save', (color, instance) => {
        var rgba = color.toRGBA();
        chosenColor = rgba;
        console.log("Set color to " + chosenColor);
    });
}

function GetChosenColor() {
    return chosenColor;
}

var splits = {};

function SplitWindows(containerId) {

    if (containerId in splits)
        splits[containerId].destroy();

    if (document.getElementById(containerId) != null)
        splits[containerId] = Split(document.getElementById(containerId).children, {
     
        });
}

/* Window resize code */

window.onresize = function () {
    document.body.height = window.innerHeight;
}

window.onresize(); // called to initially set the height.

/* Sound Code */

// Hack for IOS stupidity

var playedDummy = false

document.addEventListener('pointerdown', function () {
    if (playedDummy)
        return;

    dummySound();
    playedDummy = true;
})

// Literally HAVE to do this so IOS works. Apple literally hates developers.
const audioSources = [new Audio(), new Audio(), new Audio(), new Audio(), new Audio(),
                      new Audio(), new Audio(), new Audio(), new Audio(), new Audio()]
var sourceIndex = 0;

function getAudioSource() {
    var source = audioSources[sourceIndex];
    sourceIndex++;
    if (sourceIndex > 9)
        sourceIndex = 0;
    return source;
}

function dummySound() {

    for (var i = 0; i < audioSources.length; i++) {
        var source = getAudioSource();
        source.src = "./_content/Valour.Client/media/sounds/Dummy.mp3";
        source.volume = 0;
        source.play();
    }
}

function playSound(name) {
    var source = getAudioSource();
    source.src = "./_content/Valour.Client/media/sounds/" + name;
    source.volume = 0.4;
    source.play();
}

function SetCardTitle(id, name) {
   document.getElementById('text-' + id).firstElementChild.firstElementChild.innerHTML = name;
}

/* Tooltips */

function enableTooltip(id) {
    $('#' + id).tooltip()
}

function disableTooltip(id) {
    $('#' + id).tooltip('hide')
}

function updateTooltip(id) {
    $('#' + id).tooltip('update')
}