export function doSplit(el, direction) {
    Split(el.children,
        {
            direction: direction,
        }
    );
}

export function doUnSplit(el) {
    // Remove all children with the class `gutter`
    el.querySelectorAll('.gutter').forEach((e) => e.remove());
}

export function doEmptyHide(el){
    doUnSplit(el);
    for (let i = 0; i < el.children.length; i++){
        el.children[i].style.width = '';
    }
}