export function enableDrag(el, dotnet) {
    let offsetX, offsetY;
    let initialX, initialY;
    let dragging = false;
    
    el.dotnet = dotnet;

    const mouseDownHandler = function(e) {
        // Store the initial position
        initialX = e.clientX;
        initialY = e.clientY;
        
        dotnet.invokeMethodAsync('OnDragStart');

        document.addEventListener('mousemove', mouseMoveHandler);
        document.addEventListener('mouseup', mouseUpHandler);
    };

    const mouseMoveHandler = function(e)
    {
        pauseEvent(e);
        
        // Calculate the distance moved
        const dx = e.clientX - initialX;
        const dy = e.clientY - initialY;

        // Check if the mouse has moved more than 10 pixels in any direction
        if (!dragging && (el.floater || Math.sqrt(dx * dx + dy * dy) > 10)) {
            
            dragging = true;
            
            
            offsetX = e.clientX - el.getBoundingClientRect().left;
            offsetY = e.clientY - el.getBoundingClientRect().top;
            
            if (!el.floater) {
                
                console.log('Setting up floater');

                offsetX = e.clientX - el.getBoundingClientRect().left - (el.getBoundingClientRect().width - 300);
                
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
                
                el.style.position = 'fixed';
                el.style.width = '300px';
                el.style.height = '300px';
                el.style.zIndex = '101';
                el.style.resize = 'both';
                el.style.overflow = 'hidden';
                el.style.pointerEvents = 'all';
                el.borderRadius = '0 0 1em 1em'

                const tab = el.querySelector('.tab-wrapper');
                tab.style.width = '100%';
                tab.style.marginLeft = '0';

                el.floater = true;
                console.log('Invoking OnFloaterStart')
                dotnet.invokeMethodAsync('OnFloaterStart');
            }
        } 
        
        if (dragging) {
            // Move the element
            el.style.left = e.clientX - offsetX + 'px';
            el.style.top = e.clientY - offsetY + 'px';
            el.onScreenHandler();
        }
    };

    const mouseUpHandler = function() {
        // Stop dragging
        dragging = false;
        
        el.style.zIndex = '100';

        // Remove the handlers of `mousemove` and `mouseup`
        document.removeEventListener('mousemove', mouseMoveHandler);
        document.removeEventListener('mouseup', mouseUpHandler);
    };

    // Hook mousedown event
    const tab = el.querySelector('.tab-wrapper');
    tab.addEventListener('mousedown', mouseDownHandler);

    // Store the handler so it can be removed later
    el.dragEventHook = mouseDownHandler;
}

export function disableDrag(el) {
    if (el.dragEventHook) {
        const tab = el.querySelector('.tab-wrapper');
        tab.removeEventListener('mousedown', el.dragEventHook);
    }
}

export function pauseEvent(e){
    if(e.stopPropagation) e.stopPropagation();
    if(e.preventDefault) e.preventDefault();
    e.cancelBubble=true;
    e.returnValue=false;
    return false;
}