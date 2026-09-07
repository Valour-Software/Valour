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

        let translateX = 0;
        let translateY = 0;

        if (boundingBox.right > windowWidth){
            translateX = windowWidth - boundingBox.right - 10;
        }

        if (boundingBox.top < 0){
            translateY = Math.abs(boundingBox.top) + 10;
        }

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