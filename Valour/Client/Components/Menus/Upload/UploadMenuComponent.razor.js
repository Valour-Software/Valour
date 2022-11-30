export function initialize(elementId, dotnetRef) {
    document.addEventListener('click', (e) => {
        // Allow upload button
        if (e.target.classList.contains('upload')) {
            return;
        }
        
        let targetEl = e.target;
        
        // Checks if the clicked element is a child or equal to
        // the given element. If target is outside of the given
        // element, it will continue. Otherwise, it will halt.
        do {
            if (targetEl.id == elementId)
                return;
            targetEl = targetEl.parentElement;
        } while (targetEl);
        
        // Close the menu if the target was not us or the upload
        // button
        dotnetRef.invokeMethodAsync('Hide');
    });
}

export function OpenUploadFile(windowId){
    document.getElementById(`upload-core-${windowId}`).click();
}
