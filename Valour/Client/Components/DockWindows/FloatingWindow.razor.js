export function enableDrag(el, dotnet, x, y, startDragging = false) {
    let offsetX, offsetY;
    let initialX = x;
    let initialY = y;
    let dragTarget = null;
    let scanTimer = 0;

    el.dotnet = dotnet;

    const mouseDownHandler = function(e) {
        // Store the initial position
        initialX = e.clientX;
        initialY = e.clientY;
        
        armDrag(el);
    };
    
    const armDrag = function(el, override = false){
        if (el.floater || override) {
            console.log('Invoking OnDragStart')
            dotnet.invokeMethodAsync('OnDragStart');
            el.classList.add('dragging');
            // el.style.opacity = '0.6';
        }
        
        el.mouseMoveHandler = mouseMoveHandler;
        el.mouseUpHandler = mouseUpHandler;

        document.addEventListener('mousemove', mouseMoveHandler);
        document.addEventListener('mouseup', mouseUpHandler);
    }

    const mouseMoveHandler = function(e)
    {
        if (!el.dragging){
            offsetX = e.clientX - el.getBoundingClientRect().left;
            offsetY = e.clientY - el.getBoundingClientRect().top;
            
            el.dragging = true;
        }
        
        pauseEvent(e);


        // console.log(e.clientX, e.clientY)
        
        // Move the element
        el.style.left = (e.clientX - offsetX) + 'px';
        el.style.top = (e.clientY - offsetY) + 'px';
        el.onScreenHandler();

        // console.log(document.elementsFromPoint(e.clientX, e.clientY))

        scanTimer++;

        // Optimization for expensive scanning
        if (scanTimer % 5 === 0) {
            let newDragTarget = null;

            document.elementsFromPoint(e.clientX, e.clientY).forEach((element) => {
                if (element.classList.contains('w-drop-target')) {
                    newDragTarget = element;
                }
            });

            if (newDragTarget) {
                if (newDragTarget !== dragTarget) {
                    if (dragTarget) {
                        dragTarget.style.backgroundColor = '#fff';
                    }
                    dragTarget = newDragTarget;
                    dragTarget.style.backgroundColor = '#0ff';
                }
            } else {
                if (dragTarget) {
                    dragTarget.style.backgroundColor = '#fff';
                    dragTarget = null;
                }
            }
        }
        
    };

    const mouseUpHandler = function(e) {

        // Stop dragging
        el.dragging = false;
        el.classList.remove('dragging');

        /*
        el.style.zIndex = '100';
        el.style.opacity = '1';
        */

        if (dragTarget){

            const ev = new MouseEvent('click', {
                'view': window,
                'bubbles': true,
                'cancelable': true,
                'screenX': e.screenX,
                'screenY': e.screenY
            });

            dragTarget.dispatchEvent(ev);
        }

        dotnet.invokeMethodAsync('OnDragEnd');

        // Remove the handlers of `mousemove` and `mouseup`
        document.removeEventListener('mousemove', mouseMoveHandler);
        document.removeEventListener('mouseup', mouseUpHandler);
    };

    // Hook mousedown event
    const tab = el.querySelector('.tab-wrapper');
    tab.addEventListener('mousedown', mouseDownHandler);

    // Store the handler so it can be removed later
    el.dragEventHook = mouseDownHandler;
    console.log('Setting up floater');
    
    // Function to ensure window never falls outside of the screen during resize
    const onScreenHandler = function() {
        const rect = el.getBoundingClientRect();
        const width = rect.width;
        const height = rect.height;
        const left = rect.left;
        const top = rect.top;
        const right = left + width;
        const bottom = top + height;
        const windowWidth = window.innerWidth;
        const windowHeight = window.innerHeight;
        const padding = 10;
        const minWidth = 200;
        const minHeight = 200;

        if (left < padding) {
            el.style.left = padding + 'px';
        }
        if (top < padding) {
            el.style.top = padding + 'px';
        }
        if (right > windowWidth - padding) {
            el.style.left = windowWidth - width - padding + 'px';
        }
        if (bottom > windowHeight - padding) {
            el.style.top = windowHeight - height - padding + 'px';
        }
        if (width < minWidth) {
            el.style.width = minWidth + 'px';
        }
        if (height < minHeight) {
            el.style.height = minHeight + 'px';
        }
    };

    el.onScreenHandler = onScreenHandler;
    window.addEventListener('resize', onScreenHandler)


    el.floater = true;
    
    if (startDragging) {
        el.classList.add('dragging');
        armDrag(el, false);
    }
}

export function cleanupFloater(el) {
    // Remove all styles
    el.classList.remove('w-floating');
    el.style.left = '';
    el.style.top = '';

    /*
    if (el.dragEventHook) {
        const tab = el.querySelector('.tab-wrapper');
        tab.removeEventListener('mousedown', el.dragEventHook);
    }
    */

    if (el.onScreenHandler) {
        window.removeEventListener('resize', el.onScreenHandler);
    }

    if (el.mouseMoveHandler) {
        document.removeEventListener('mousemove', el.mouseMoveHandler);
    }

    if (el.mouseUpHandler) {
        document.removeEventListener('mouseup', el.mouseUpHandler);
    }

    el.floater = false;
}

export function pauseEvent(e){
    if(e.stopPropagation) e.stopPropagation();
    if(e.preventDefault) e.preventDefault();
    e.cancelBubble=true;
    e.returnValue=false;
    return false;
}

export function getPosition(el){
    const bounds = el.getBoundingClientRect();
    return [bounds.left, bounds.top]
}

export function reRender(el, isActive){
    if (isActive){
        el.style.zIndex = '101';
    } else {
        el.style.zIndex = '100';
    }
}