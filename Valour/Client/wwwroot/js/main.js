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

let mobile = false;
(function (a) { if (/(android|bb\d+|meego).+mobile|\avantgo|bada\/|blackberry|blazer|compal|elaine|fennec|hiptop|iemobile|ip(hone|od)|iris|kindle|lge |maemo|midp|mmp|mobile.+firefox|netfront|opera m(ob|in)i|palm( os)?|phone|p(ixi|re)\/|plucker|pocket|psp|series(4|6)0|symbian|treo|up\.(browser|link)|vodafone|wap|windows ce|xda|xiino/i.test(a) || /1207|6310|6590|3gso|4thp|50[1-6]i|770s|802s|a wa|abac|ac(er|oo|s\-)|ai(ko|rn)|al(av|ca|co)|amoi|an(ex|ny|yw)|aptu|ar(ch|go)|as(te|us)|attw|au(di|\-m|r |s )|avan|be(ck|ll|nq)|bi(lb|rd)|bl(ac|az)|br(e|v)w|bumb|bw\-(n|u)|c55\/|capi|ccwa|cdm\-|cell|chtm|cldc|cmd\-|co(mp|nd)|craw|da(it|ll|ng)|dbte|dc\-s|devi|dica|dmob|do(c|p)o|ds(12|\-d)|el(49|ai)|em(l2|ul)|er(ic|k0)|esl8|ez([4-7]0|os|wa|ze)|fetc|fly(\-|_)|g1 u|g560|gene|gf\-5|g\-mo|go(\.w|od)|gr(ad|un)|haie|hcit|hd\-(m|p|t)|hei\-|hi(pt|ta)|hp( i|ip)|hs\-c|ht(c(\-| |_|a|g|p|s|t)|tp)|hu(aw|tc)|i\-(20|go|ma)|i230|iac( |\-|\/)|ibro|idea|ig01|ikom|im1k|inno|ipaq|iris|ja(t|v)a|jbro|jemu|jigs|kddi|keji|kgt( |\/)|klon|kpt |kwc\-|kyo(c|k)|le(no|xi)|lg( g|\/(k|l|u)|50|54|\-[a-w])|libw|lynx|m1\-w|m3ga|m50\/|ma(te|ui|xo)|mc(01|21|ca)|m\-cr|me(rc|ri)|mi(o8|oa|ts)|mmef|mo(01|02|bi|de|do|t(\-| |o|v)|zz)|mt(50|p1|v )|mwbp|mywa|n10[0-2]|n20[2-3]|n30(0|2)|n50(0|2|5)|n7(0(0|1)|10)|ne((c|m)\-|on|tf|wf|wg|wt)|nok(6|i)|nzph|o2im|op(ti|wv)|oran|owg1|p800|pan(a|d|t)|pdxg|pg(13|\-([1-8]|c))|phil|pire|pl(ay|uc)|pn\-2|po(ck|rt|se)|prox|psio|pt\-g|qa\-a|qc(07|12|21|32|60|\-[2-7]|i\-)|qtek|r380|r600|raks|rim9|ro(ve|zo)|s55\/|sa(ge|ma|mm|ms|ny|va)|sc(01|h\-|oo|p\-)|sdk\/|se(c(\-|0|1)|47|mc|nd|ri)|sgh\-|shar|sie(\-|m)|sk\-0|sl(45|id)|sm(al|ar|b3|it|t5)|so(ft|ny)|sp(01|h\-|v\-|v )|sy(01|mb)|t2(18|50)|t6(00|10|18)|ta(gt|lk)|tcl\-|tdg\-|tel(i|m)|tim\-|t\-mo|to(pl|sh)|ts(70|m\-|m3|m5)|tx\-9|up(\.b|g1|si)|utst|v400|v750|veri|vi(rg|te)|vk(40|5[0-3]|\-v)|vm40|voda|vulc|vx(52|53|60|61|70|80|81|83|85|98)|w3c(\-| )|webc|whit|wi(g |nc|nw)|wmlb|wonu|x700|yas\-|your|zeto|zte\-/i.test(a.substr(0, 4))) mobile = true; })(navigator.userAgent || navigator.vendor || window.opera);

const embedded = window.location.href.includes('embedded=true');
// Special embedded check
if (embedded) {
    console.log("Enabling embedded mode.");
    mobile = true;
}

window["mobile"] = mobile;
window["embedded"] = embedded;

function IsMobile() {
    return mobile;
}

function IsEmbedded() {
    return embedded;
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

function SetDate() {
    if (document.getElementById('ageVeriInput')) document.getElementById('ageVeriInput').valueAsDate = new Date()
}

// TODO: Cleanup SW stuff
// const registerClient = async () => {
// }

window.blazorFuncs = {
    registerClient: function (caller) {
        window['updateAvailable']
            .then(isAvailable => {
                if (isAvailable) {
                    DotNet.invokeMethodAsync("Valour.Client", "OnServiceUpdateAvailable").then(r => console.log(r));
                }
                else {
                    DotNet.invokeMethodAsync("Valour.Client", "OnServiceUpdateUnavailable").then(r => console.log(r));
                }
            });
    }
};

window.getBrowserOrigin = function() {
    return window.location.origin;
};

function Log(message, color) {
    console.log("%c" + message, 'color: ' + color);
}

/* Sound Code */

// Hack for IOS stupidity

let playedDummy = false

document.addEventListener('pointerdown', function () {
    if (playedDummy)
        return;

    dummySound();
    playedDummy = true;
})

// Literally HAVE to do this so IOS works. Apple literally hates developers.
const audioSources = [new Audio(), new Audio(), new Audio(), new Audio(), new Audio(),
                      new Audio(), new Audio(), new Audio(), new Audio(), new Audio()]

let sourceIndex = 0;

function getAudioSource() {
    const source = audioSources[sourceIndex];
    sourceIndex++;
    if (sourceIndex > 9)
        sourceIndex = 0;
    return source;
}

function dummySound() {
    for (let i = 0; i < audioSources.length; i++) {
        let source = getAudioSource();
        source.src = "data:audio/mpeg;base64,SUQzBAAAAAABEVRYWFgAAAAtAAADY29tbWVudABCaWdTb3VuZEJhbmsuY29tIC8gTGFTb25vdGhlcXVlLm9yZwBURU5DAAAAHQAAA1N3aXRjaCBQbHVzIMKpIE5DSCBTb2Z0d2FyZQBUSVQyAAAABgAAAzIyMzUAVFNTRQAAAA8AAANMYXZmNTcuODMuMTAwAAAAAAAAAAAAAAD/80DEAAAAA0gAAAAATEFNRTMuMTAwVVVVVVVVVVVVVUxBTUUzLjEwMFVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVf/zQsRbAAADSAAAAABVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVf/zQMSkAAADSAAAAABVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV";
        source.play();
    }
}

function playSound(name) {
    const source = getAudioSource();
    source.loop = false;
    source.volume = 0.4;
    source.src = "./_content/Valour.Client/media/sounds/" + name;
    source.play();
}

function SetCardTitle(id, name) {
   document.getElementById('text-' + id).firstElementChild.firstElementChild.innerHTML = name;
}

/* Content upload handling */

// Creates a blob and returns the location
function createBlob(buffer, contentType) {
    const blob = new Blob([buffer], { type: contentType });
    return window.URL.createObjectURL(blob);
}

function getImageSize(blobUrl, ref) {
    const image = new Image();
    image.onload = function () {
        ref.invokeMethodAsync('SetImageSize', this.width, this.height);
    }
    image.src = blobUrl;
}

/* Useful functions for layout items */
function determineFlip(elementId, safeWidth){
    const element = document.getElementById(elementId);
    if (!element)
        return;

    const parentWidth = element.parentElement.offsetWidth;
    const selfPosition = element.offsetLeft;
    
    if (parentWidth - selfPosition < safeWidth) {
        element.classList.add('flip');
    } else {
        element.classList.remove('flip');
    }
}

/* GDPR Compliance */

const EU_TIMEZONES = [
    "Europe/Vienna",
    "Europe/Brussels",
    "Europe/Sofia",
    "Europe/Zagreb",
    "Asia/Famagusta",
    "Asia/Nicosia",
    "Europe/Prague",
    "Europe/Copenhagen",
    "Europe/Tallinn",
    "Europe/Helsinki",
    "Europe/Paris",
    "Europe/Berlin",
    "Europe/Busingen",
    "Europe/Athens",
    "Europe/Budapest",
    "Europe/Dublin",
    "Europe/Rome",
    "Europe/Riga",
    "Europe/Vilnius",
    "Europe/Luxembourg",
    "Europe/Malta",
    "Europe/Amsterdam",
    "Europe/Warsaw",
    "Atlantic/Azores",
    "Atlantic/Madeira",
    "Europe/Lisbon",
    "Europe/Bucharest",
    "Europe/Bratislava",
    "Europe/Ljubljana",
    "Africa/Ceuta",
    "Atlantic/Canary",
    "Europe/Madrid",
    "Europe/Stockholm",
];

function isEuropeanUnion() {
    try {
        const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return EU_TIMEZONES.includes(timeZone);
    } catch (e) {
        // Fallback if Intl API is not supported
        return false;
    }
}


function positionRelativeTo(id, x, y, corner) {
    const element = document.getElementById(id);
    if (!element)
        return;
    
    const viewRect = element.getBoundingClientRect();
    const width = viewRect.width;
    const height = viewRect.height;
    
    if (corner === "bottomLeft") {
        element.style.top = `${y - height}px`;
        element.style.left = `${x}px`;
    }
    else if (corner === "bottomRight") {
        element.style.top = `${y - height}px`;
        element.style.left = `${x - width}px`;
    }
    
    // Prevent escaping screen
    const rect = element.getBoundingClientRect();
    if (rect.left < 16) {
        element.style.left = `16px`;
    }
    if (rect.top < 16) {
        element.style.top = `16px`;
    }
    if (rect.right > window.innerWidth - 16) {
        element.style.left = `${window.innerWidth - width - 16}px`;
    }
    if (rect.bottom > window.innerHeight - 16) {
        element.style.top = `${window.innerHeight - height - 16}px`;
    }
}

async function injectTwitter(id, data) {
    const container = document.getElementById(id);
    if (!container) {
        return;
    }
    
    container.innerHTML = data;
    
    let twitterScript = document.createElement('script');
    twitterScript.src = "https://platform.twitter.com/widgets.js";
    twitterScript.async = true;
    twitterScript.charset = "utf-8";
    container.appendChild(twitterScript);
}

async function injectReddit(id, data) {
    const container = document.getElementById(id);
    if (!container) {
        return;
    }

    container.setAttribute('data-embed-theme', 'dark');
    container.innerHTML = data;

    let redditScript = document.createElement('script');
    redditScript.src = "https://embed.reddit.com/widgets.js";
    redditScript.async = true;
    redditScript.charset = "utf-8";
    container.appendChild(redditScript);
}

function playLottie(element) {
    element.play();
}