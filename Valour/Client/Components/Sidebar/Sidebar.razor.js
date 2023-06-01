var dotnetRef = null;

export function init(ref){
    dotnetRef = ref;
    window.addEventListener('touchstart', handleTouchStart, false);
    window.addEventListener('touchmove', handleTouchMove, false);
}

var xDown = null;
var yDown = null;

function handleTouchStart(evt) {
    const firstTouch = getTouches(evt)[0];
    xDown = firstTouch.clientX;
    yDown = firstTouch.clientY;
}

function handleTouchMove(evt) {
    if (!xDown || !yDown) {
        return;
    }

    var xUp = evt.touches[0].clientX;
    var yUp = evt.touches[0].clientY;

    var xDiff = xDown - xUp;
    var yDiff = yDown - yUp;

    const width = screen.width;

    if (Math.abs(xDiff) > Math.abs(yDiff)) {/*most significant*/
        if (xUp > width - 50 && xDiff > 5) {
            /* left swipe */
            OnLeftSwipe();
        } else if (xUp < 50 && xDiff < -5) {
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
}

function getTouches(evt) {
    return evt.touches ||             // browser API
        evt.originalEvent.touches; // jQuery
}

function OnRightSwipe() {
        dotnetRef.invokeMethodAsync('OnRightSwipe');
}

function OnLeftSwipe() {
    dotnetRef.invokeMethodAsync('OnLeftSwipe');
}

export function cleanup() {
    window.removeEventListener('touchstart', handleTouchStart, false);
    window.removeEventListener('touchmove', handleTouchMove, false);
}

