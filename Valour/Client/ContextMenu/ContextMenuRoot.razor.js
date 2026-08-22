export let currentMenu = null;
export let targetX = 0;
export let targetY = 0;
export let submenus = [];

export function init(){
    window.addEventListener('resize', () => {
        reposition();
    }, true);
}

export function setMenu(el, mouseX, mouseY){
    currentMenu = el;
    targetX = mouseX;
    targetY = mouseY;
    
    reposition();
}

export function clearMenu() {
    currentMenu = null;
}

// Margin kept between a repositioned submenu and the viewport edge.
const SUBMENU_EDGE_MARGIN = 10;

// Pure geometry helper so the boundary-correction math can be unit tested
// without a DOM (see Tests/Js/context-menu-reposition.test.mjs).
export function computeSubmenuTranslate(boundingBox, windowWidth, windowHeight, margin = SUBMENU_EDGE_MARGIN){
    let translateX = 0;
    let translateY = 0;

    if (boundingBox.right > windowWidth){
        translateX = windowWidth - boundingBox.right - margin;
    }

    if (boundingBox.top < 0){
        translateY = Math.abs(boundingBox.top) + margin;
    } else if (boundingBox.bottom > windowHeight){
        // Submenus anchor via `bottom: -50%` off their parent button
        // (ContextSubMenu.razor.css), so a button near the bottom edge
        // can open a submenu whose content extends past the viewport
        // bottom. Mirrors the right-edge check above. (#1622)
        translateY = windowHeight - boundingBox.bottom - margin;
    }

    return { translateX, translateY };
}

export function reposition(){

    if (!currentMenu)
        return;
    
    // Get width and height of element and then position it to where
    // the mouse is, making sure that it does not go off screen

    // Get width and height of element
    let width = currentMenu.offsetWidth;
    
    // Add width of submenus
    for (let i = 0; i < submenus.length; i++){
        width += submenus[i].offsetWidth;
    }
    
    const height = currentMenu.offsetHeight;

    // Get window width and height
    const windowWidth = document.documentElement.clientWidth;
    const windowHeight = document.documentElement.clientHeight;

    let posX = targetX - 10;
    let posY = targetY - 10;

    // Check if the element is going off the right side of the screen
    if(posX + width > windowWidth){
        posX = windowWidth - width;
    }

    // Check if the element is going off the bottom side of the screen
    if(posY + height > windowHeight){
        posY = windowHeight - height;
    }
    
    // Check the position of submenus. If they overflow the top or right edge
    // of the screen, shift them down/left with margin to fit.
    // Submenus open leftmost to their parent button with no built-in bound,
    // so on narrow screens or where the .mobile layout is not applied
    // they can render partially or fully off-screen. (ContextSubMenu.razor.css; #1622)
    for (let i = 0; i < submenus.length; i++){
        const submenu = submenus[i];

        // Clear any previous correction before measuring, so repeated calls
        // (e.g. on window resize) compute from the untransformed layout
        // position instead of compounding on top of the last correction.
        submenu.style.transform = '';
        const boundingBox = submenu.getBoundingClientRect();

        const { translateX, translateY } = computeSubmenuTranslate(boundingBox, windowWidth, windowHeight);

        if (translateX !== 0 || translateY !== 0){
            submenu.style.transform = `translate(${translateX}px, ${translateY}px)`;
        }
    }

    // Set the position of the element
    currentMenu.style.left = posX + 'px';
    currentMenu.style.top = posY + 'px';
}

export function addSubmenu(submenu){
    
    // Ensure that the submenu is not already in the list
    if (submenus.indexOf(submenu) !== -1)
        return;
    
    submenus.push(submenu);
    reposition();
}

export function removeSubmenu(submenu){
    submenus = submenus.filter(x => x !== submenu);
    reposition();
}