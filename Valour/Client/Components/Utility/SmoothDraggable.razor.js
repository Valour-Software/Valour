export let states = {};
export let elements = {};
export let init = false;
export let activeId = '';

export function initialize(id, x, y, right = false){
    const element = document.getElementById(id);
    
    if (right) {
        element.style.left = (element.parentElement.clientWidth - x - element.clientWidth) + "px";
    } else {
        element.style.left = x + "px";
    }
    element.style.top = y + "px";
    
    states[id] = { distX: x, distY: y };
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
    e.preventDefault();
    const evt = e.type === 'touchstart' ? e.changedTouches[0] : e;
    activeId = id;
    
    states[id].distX = Math.abs(elements[id].offsetLeft - evt.clientX);
    states[id].distY = Math.abs(elements[id].offsetTop - evt.clientY);

    elements[id].style.pointerEvents = 'none';
}
function onUp(e) {
    if (!activeId)
        return;
    
    elements[activeId].style.pointerEvents = 'initial';
    activeId = null;
}

function onMove(e) {
    if (!activeId)
        return;
    
    const element = elements[activeId];
    
    if (element.style.pointerEvents !== 'none')
        return;
    
    const evt = e.type === 'touchmove' ? e.changedTouches[0] : e;
    
    // Update top/left directly in the dom element:
    element.style.left = `${evt.clientX - states[activeId].distX}px`;
    element.style.top = `${evt.clientY - states[activeId].distY}px`;
}