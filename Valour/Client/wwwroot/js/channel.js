// Returns highlighted markup for a code block. highlight.js escapes the code
// text, so the result is safe to render as markup.
async function hljsHighlightHtml(code, language) {
    await ensureChannelHighlightScript();

    if (language && hljs.getLanguage(language))
        return hljs.highlight(code, { language, ignoreIllegals: true }).value;

    return hljs.highlightAuto(code).value;
}

let channelHighlightScriptPromise = null;

function ensureChannelHighlightScript() {
    if (window.hljs) return Promise.resolve();
    if (channelHighlightScriptPromise) return channelHighlightScriptPromise;

    channelHighlightScriptPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.2.0/highlight.min.js';
        script.async = true;
        script.onload = resolve;
        script.onerror = () => reject(new Error('Failed to load syntax highlighting'));
        document.head.appendChild(script);
    });

    return channelHighlightScriptPromise;
}

// Drop zone logic (thanks to https://www.meziantou.net/upload-files-with-drag-drop-or-paste-from-clipboard-in-blazor.htm)

function dispatchFilesToInput(inputFile, files) {
    if (!inputFile || !files || files.length === 0) {
        return;
    }

    // Clipboard and drop FileLists become inaccessible after their event returns.
    const transfer = new DataTransfer();
    for (const file of files) {
        transfer.items.add(file);
    }
    inputFile.value = '';
    inputFile.files = transfer.files;
    const event = new Event('change', { bubbles: true });
    inputFile.dispatchEvent(event);
}

function initializeFileDropZone(dropZoneElement, inputFile, uploadButtonElement) {
    if (!dropZoneElement || !inputFile) return { dispose() {} };

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
    
    // Clear the previous selection so picking the same file again still raises change.
    function onInputFileClick() {
        inputFile.value = '';
    }

    function onUploadButtonClick(e) {
        e.preventDefault();
        inputFile.click();
        console.log("Upload button clicked, opening file picker");
    }

    // Register all events
    dropZoneElement.addEventListener("dragenter", onDragHover);
    dropZoneElement.addEventListener("dragover", onDragHover);
    dropZoneElement.addEventListener("dragleave", onDragLeave);
    dropZoneElement.addEventListener("drop", onDrop);
    dropZoneElement.addEventListener('paste', onPaste);
    inputFile.addEventListener('click', onInputFileClick);
    uploadButtonElement?.addEventListener('click', onUploadButtonClick);
    
    console.log("File drop zone initialized");

    // The returned object allows to unregister the events when the Blazor component is destroyed
    return {
        dispose: () => {
            dropZoneElement.removeEventListener('dragenter', onDragHover);
            dropZoneElement.removeEventListener('dragover', onDragHover);
            dropZoneElement.removeEventListener('dragleave', onDragLeave);
            dropZoneElement.removeEventListener("drop", onDrop);
            dropZoneElement.removeEventListener('paste', onPaste);
            inputFile.removeEventListener('click', onInputFileClick);
            uploadButtonElement?.removeEventListener('click', onUploadButtonClick);
        }
    }
}
