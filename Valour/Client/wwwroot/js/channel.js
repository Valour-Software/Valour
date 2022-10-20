function OnMessageLoad(innerContent) {

    if (innerContent != null && innerContent.getElementsByTagName) {
        var code_els = innerContent.getElementsByTagName('code');

        if (code_els != null) {
            for (let item of code_els) {
                hljs.highlightElement(item);
            }
        }

        var images = innerContent.getElementsByTagName('img');
        if (images != null) {
            for (let image of images) {
                if (!image.classList.contains('attached-image')) { 
                    image.addEventListener('click', function () {
                        image.classList.toggle('enlarged');
                    });
                }
            }
        }

        twemoji.parse(innerContent, {
            folder: 'svg',
            ext: '.svg'
        })
    }

    //innerContent.getElementsByTagName('code').forEach(el => 
    //    console.log(el)
    //);
}


// Drop zone logic (thanks to https://www.meziantou.net/upload-files-with-drag-drop-or-paste-from-clipboard-in-blazor.htm)

function initializeFileDropZone(dropZoneElement, inputFile) {
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

        // Set the files property of the input element and raise the change event
        inputFile.files = e.dataTransfer.files;
        const event = new Event('change', { bubbles: true });
        inputFile.dispatchEvent(event);
    }

    function onPaste(e) {

        if (e.clipboardData.files.length == 0)
            return;

        // Set the files property of the input element and raise the change event
        inputFile.files = e.clipboardData.files;

        const event = new Event('change', { bubbles: true });
        inputFile.dispatchEvent(event);
    }

    // Register all events
    dropZoneElement.addEventListener("dragenter", onDragHover);
    dropZoneElement.addEventListener("dragover", onDragHover);
    dropZoneElement.addEventListener("dragleave", onDragLeave);
    dropZoneElement.addEventListener("drop", onDrop);
    dropZoneElement.addEventListener('paste', onPaste);

    // The returned object allows to unregister the events when the Blazor component is destroyed
    return {
        dispose: () => {
            dropZoneElement.removeEventListener('dragenter', onDragHover);
            dropZoneElement.removeEventListener('dragover', onDragHover);
            dropZoneElement.removeEventListener('dragleave', onDragLeave);
            dropZoneElement.removeEventListener("drop", onDrop);
            dropZoneElement.removeEventListener('paste', handler);
        }
    }
}