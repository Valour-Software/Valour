function hljsHighlight(el){
    const prior = el.parentElement.querySelector('.hljs-clone');
    if (prior) {
        el.parentElement.removeChild(prior);
    }

    const clone = el.cloneNode(true);
    clone.classList.add('hljs-clone');
    clone.style.display = 'inherit';
    hljs.highlightElement(clone);

    el.parentElement.insertBefore(clone, el.nextSibling);
    el.style.display = 'none';
}

// Drop zone logic (thanks to https://www.meziantou.net/upload-files-with-drag-drop-or-paste-from-clipboard-in-blazor.htm)

function dispatchFilesToInput(inputFile, files) {
    if (!inputFile || !files || files.length === 0) {
        return;
    }

    inputFile.value = '';
    inputFile.files = files;
    const event = new Event('change', { bubbles: true });
    inputFile.dispatchEvent(event);
}

function initializeFileDropZone(dropZoneElement, inputFile, uploadButtonElement) {
    // Add a class when the user drags a file over the drop zone
    function onDragHover(e) {
        e.preventDefault();
        dropZoneElement.classList.add("hover");
    }

    function onDragLeave(e) {
        e.preventDefault();
        dropZoneElement.classList.remove("hover");
    }

    // Handle the paste and drop events
    function onDrop(e) {
        e.preventDefault();
        dropZoneElement.classList.remove("hover");

        // Route dropped files through the same input/change path used by the picker
        dispatchFilesToInput(inputFile, e.dataTransfer.files);
    }

    function onPaste(e) {

        if (e.clipboardData.files.length == 0)
            return;

        // Route pasted files through the same input/change path used by the picker
        dispatchFilesToInput(inputFile, e.clipboardData.files);
    }
    
    function onUploadButtonClick(e) {
        inputFile.click();
    }

    // Register all events
    dropZoneElement.addEventListener("dragenter", onDragHover);
    dropZoneElement.addEventListener("dragover", onDragHover);
    dropZoneElement.addEventListener("dragleave", onDragLeave);
    dropZoneElement.addEventListener("drop", onDrop);
    dropZoneElement.addEventListener('paste', onPaste);
    uploadButtonElement.addEventListener('click', onUploadButtonClick);

    // The returned object allows to unregister the events when the Blazor component is destroyed
    return {
        dispose: () => {
            dropZoneElement.removeEventListener('dragenter', onDragHover);
            dropZoneElement.removeEventListener('dragover', onDragHover);
            dropZoneElement.removeEventListener('dragleave', onDragLeave);
            dropZoneElement.removeEventListener("drop", onDrop);
            dropZoneElement.removeEventListener('paste', onPaste);
            uploadButtonElement.removeEventListener('click', onUploadButtonClick);
        }
    }
}