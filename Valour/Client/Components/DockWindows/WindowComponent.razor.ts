import DotNetObject = DotNet.DotNetObject;

/*
export const enableDrag = (el: HTMLElement, dotnetRef: DotNetObject, x?: number, y?: number){
    let offsetX = 0;
    let offsetY = 0;
    
    let initialX = x;
    let initialY = y;
    
    let dragTarget: HTMLElement | null;
    
    const mouseDownHandler = (e: MouseEvent) => {
        initialX = e.clientX;
        initialY = e.clientY;
    };
    
    const armDrag = (el: HTMLElement, force: boolean = false) => {
        if (el['floating'] || force) {
            el.classList.add('dragging');
        }
        
        // Store handlers so we can remove them later
        el['mouseMoveHandler'] = mouseMoveHandler;
        el['mouseUpHandler'] = mouseUpHandler;
        
        document.addEventListener('mousemove', mouseMoveHandler);
        document.addEventListener('mouseup', mouseUpHandler);
    };
    
    const mouseMoveHandler = (e: MouseEvent) => {
        
    };
}
*/
 