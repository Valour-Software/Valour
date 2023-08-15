let xLast = null;
let yLast = null;

export function init(ref, id) {
    const sidebar = document.getElementById(id);
    
    let slideDistance = 0;
    let dragging = false;
    let open = false;
    const width = screen.width;
    
    let i = 0;
    
    const onTouchStart = (e) => {
        const firstTouch = getTouches(e)[0];
        xLast = firstTouch.clientX;
        yLast = firstTouch.clientY;
    }

    const onTouchMove = (e) => {
        if (!xLast || !yLast) {
            return;
        }
        
        i++;
        
       /* if (i % 10 !== 0) {
            return;
        } */

        let x = e.touches[0].clientX;
        let y = e.touches[0].clientY;

        const xDiff = xLast - x;
        const yDiff = yLast - y;
        
        if (Math.abs(xDiff) > Math.abs(yDiff)) {/*most significant*/
            
            if (dragging === false) {
                
                if (open) {
                    if (x < width - 100) {
                        return;
                    }
                } else {
                    if (x > 100) {
                        return;
                    }
                }
                
                dragging = true;
                sidebar.style.transition = 'none';
            }
            
            slideDistance = x - width;
            if (slideDistance > 0) {
                slideDistance = 0;
            }
            if (slideDistance < -width){
                slideDistance = -width;
            }
            sidebar.style.transform = `translateX(${slideDistance}px)`;
        }
    }
    
    const onTouchEnd = function (e) {
        if (Math.abs(slideDistance) < width / 2) {
            sidebar.style.transform = `translateX(0px)`;
            open = true;
        } else {
            sidebar.style.transform = null;
            open = false;
        }
        
        dragging = false;
        sidebar.style.transition = null;
    }

    window.addEventListener('touchstart', onTouchStart, false);
    window.addEventListener('touchmove', onTouchMove, false);
    window.addEventListener('touchend', onTouchEnd, false);
}

function getTouches(evt) {
    return evt.touches ||             // browser API
        evt.originalEvent.touches; // jQuery
}

export function cleanup() {
    window.removeEventListener('touchstart', handleTouchStart, false);
    window.removeEventListener('touchmove', handleTouchMove, false);
}

