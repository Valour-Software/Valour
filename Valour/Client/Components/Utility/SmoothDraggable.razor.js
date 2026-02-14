export let states = {};
export let elements = {};
export let init = false;
export let activeId = '';

export function initialize(id, x, y, right = false){
    const element = document.getElementById(id);
    
    console.debug('initializing draggable ' + id);
    
    if (right) {
        element.style.left = ((element.parentElement.clientWidth - x - element.clientWidth) / element.parentElement.clientWidth) * 100 + "%";
    } else {
        element.style.left =  ((element.clientWidth + x) / element.parentElement.clientWidth) * 100 + "px";
    }
    element.style.top = ((y / element.parentElement.clientHeight) * 100) + "%";
    
    states[id] = { distX: element.style.left, distY: element.style.top };
    elements[id] = element;
    
    if (!init){
        document.addEventListener('mousemove', onMove);
        document.addEventListener('touchmove', onMove);
        document.addEventListener('mouseup', onUp);
        init = true;
    }
    
    element.addEventListener('mousedown', (e) => onDown(e, id));
}

function onDown(e, id) {
    // Don't initiate drag from interactive form controls
    if (e.target.closest('input, select, textarea')) {
        return;
    }

    e.preventDefault();
    const evt = e.type === 'touchstart' ? e.changedTouches[0] : e;
    activeId = id;
    
    states[id].distX = Math.abs((elements[id].offsetLeft - evt.clientX) / elements[id].parentElement.clientWidth * 100);
    states[id].distY = Math.abs((elements[id].offsetTop - evt.clientY) / elements[id].parentElement.clientHeight * 100);

    elements[id].dataset.moving = 'true';
}
function onUp(e) {
    if (!activeId)
        return;
    
    elements[activeId].dataset.moving = 'false';
    activeId = null;
}

function onMove(e) {
    if (!activeId)
        return;
    
    const element = elements[activeId];
    
    if (element.dataset.moving === 'false')
        return;
    
    const evt = e.type === 'touchmove' ? e.changedTouches[0] : e;
    
    // Update top/left directly in the dom element:
    element.style.left = `${((evt.clientX / element.parentElement.clientWidth) * 100) - states[activeId].distX}%`;
    element.style.top = `${((evt.clientY / element.parentElement.clientHeight) * 100) - states[activeId].distY}%`;
}