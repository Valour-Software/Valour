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

    let posX = targetX;
    let posY = targetY;

    // Check if the element is going off the right side of the screen
    if(posX + width > windowWidth){
        posX = windowWidth - width;
    }

    // Check if the element is going off the bottom side of the screen
    if(posY + height > windowHeight){
        posY = windowHeight - height;
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