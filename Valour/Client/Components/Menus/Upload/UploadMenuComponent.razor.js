export var dotnet = null;
    
export function initialize(elementId, dotnetRef) {
    dotnet = dotnetRef;

    const handler = function(e){
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
        // Close the menu if the target was not us or the upload
        // button
        dotnet.invokeMethodAsync('Hide').catch((err) => {
            document.removeEventListener('click', handler);
            console.log("Cleaning up old Upload Menu event");
        });
    }

    document.addEventListener('click', handler);
}

export function OpenUploadFile(windowId){
    document.getElementById(`upload-core-${windowId}`).click();
}