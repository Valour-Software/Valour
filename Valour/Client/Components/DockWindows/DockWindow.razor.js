export function enableDrag(el, dotnet) {
    let offsetX, offsetY;
    let initialX, initialY;
    let dragTarget = null;
    let scanTimer = 0;

    el.dotnet = dotnet;

    const mouseDownHandler = function(e) {
        // Store the initial position
        initialX = e.clientX;
        initialY = e.clientY;

        if (el.floater) {
            console.log('Invoking OnDragStart')
            dotnet.invokeMethodAsync('OnDragStart');
            el.classList.add('dragging');
            // el.style.opacity = '0.6';
        }

        el.mouseMoveHandler = mouseMoveHandler;
        el.mouseUpHandler = mouseUpHandler;
        
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
        if (!el.dragging && (el.floater || Math.sqrt(dx * dx + dy * dy) > 10)) {
            
            offsetX = e.clientX - el.getBoundingClientRect().left;
            offsetY = e.clientY - el.getBoundingClientRect().top;
            
            if (!el.floater) {
                
                console.log('Setting up floater');

                offsetX = e.clientX - el.getBoundingClientRect().left - (el.getBoundingClientRect().width - 300);
                
                console.log('Invoking OnFloaterStart')
                dotnet.invokeMethodAsync('OnFloaterStart', e.clientX - 150, e.clientY);

                document.removeEventListener('mousemove', mouseMoveHandler);
                document.removeEventListener('mouseup', mouseUpHandler);
            }
        }
    };

    const mouseUpHandler = function(e) {
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

export function pauseEvent(e){
    if(e.stopPropagation) e.stopPropagation();
    if(e.preventDefault) e.preventDefault();
    e.cancelBubble=true;
    e.returnValue=false;
    return false;
}