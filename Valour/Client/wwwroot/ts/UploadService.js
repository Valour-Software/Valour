/**
 * UploadService JS module
 * Provides real XHR upload progress + cancel support for Blazor WASM.
 * Each call to start() returns an upload handle with its own XHR and abort function.
 */

/**
 * @param {string} url - Upload endpoint
 * @param {Uint8Array} byteArray - File data
 * @param {string} mimeType - MIME type
 * @param {string} fileName - File name
 * @param {object} dotnetRef - DotNetObjectReference for callbacks
 * @param {string} authToken - Optional Authorization header value
 * @returns {object} Upload handle with an abort() method
 */
export function start(url, byteArray, mimeType, fileName, dotnetRef, authToken) {
    const xhr = new XMLHttpRequest();

    const handle = {
        abort: () => xhr.abort()
    };

    xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
            dotnetRef.invokeMethodAsync('NotifyUploadProgress', e.loaded, e.total);
        }
    });

    xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
            dotnetRef.invokeMethodAsync('NotifyUploadComplete', xhr.responseText);
        } else if (xhr.status === 421) {
            dotnetRef.invokeMethodAsync('NotifyUploadMisdirect', xhr.responseText, xhr.status);
        } else {
            dotnetRef.invokeMethodAsync('NotifyUploadError', `${xhr.status}: ${xhr.responseText}`);
        }
    });

    xhr.addEventListener('error', () => {
        dotnetRef.invokeMethodAsync('NotifyUploadError', 'Network error during upload');
    });

    xhr.addEventListener('abort', () => {
        dotnetRef.invokeMethodAsync('NotifyUploadCancelled');
    });

    xhr.open('POST', url);

    if (authToken) {
        xhr.setRequestHeader('Authorization', authToken);
    }

    const blob = new Blob([byteArray], { type: mimeType || 'application/octet-stream' });
    const formData = new FormData();
    formData.append(fileName || 'file', blob, fileName || 'file');

    xhr.send(formData);

    return handle;
}

/**
 * Uploads the selected File directly from an input element. Keeping the File
 * in JavaScript avoids serializing large videos through Blazor interop.
 */
export function startFromInput(url, input, fileName, authToken, dotnetRef) {
    const file = selectedFile(input);
    const xhr = createPostRequest(url, fileName || file.name, authToken, dotnetRef);
    const formData = new FormData();
    formData.append(fileName || file.name || 'file', file, fileName || file.name || 'file');
    xhr.send(formData);
    return { abort: () => xhr.abort() };
}

/**
 * Raw-body PUT for direct-to-bucket uploads (presigned URLs).
 * No auth header — authorization is in the signed URL. The Content-Type
 * must match what was signed into the grant.
 * @returns {object} Upload handle with an abort() method
 */
export function startPut(url, byteArray, mimeType, dotnetRef) {
    const xhr = new XMLHttpRequest();

    const handle = {
        abort: () => xhr.abort()
    };

    xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
            dotnetRef.invokeMethodAsync('NotifyUploadProgress', e.loaded, e.total);
        }
    });

    xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
            dotnetRef.invokeMethodAsync('NotifyUploadComplete', xhr.responseText);
        } else {
            dotnetRef.invokeMethodAsync('NotifyUploadError', `${xhr.status}: ${xhr.responseText}`);
        }
    });

    xhr.addEventListener('error', () => {
        dotnetRef.invokeMethodAsync('NotifyUploadError',
            'Network error during upload. The storage host may be unreachable or missing CORS configuration.');
    });

    xhr.addEventListener('abort', () => {
        dotnetRef.invokeMethodAsync('NotifyUploadCancelled');
    });

    xhr.open('PUT', url);
    xhr.setRequestHeader('Content-Type', mimeType || 'application/octet-stream');

    xhr.send(new Blob([byteArray], { type: mimeType || 'application/octet-stream' }));

    return handle;
}

/**
 * Direct-to-bucket PUT using the selected browser File as the raw body.
 */
export function startPutFromInput(url, input, mimeType, dotnetRef) {
    const file = selectedFile(input);
    const xhr = createPutRequest(url, mimeType || file.type, dotnetRef);
    xhr.send(file);
    return { abort: () => xhr.abort() };
}

export function createObjectUrlFromInput(input) {
    return URL.createObjectURL(selectedFile(input));
}

export function revokeObjectUrl(url) {
    URL.revokeObjectURL(url);
}

function selectedFile(input) {
    const file = input?.files?.[0];
    if (!file) {
        throw new Error('The selected file is no longer available. Please choose it again.');
    }
    return file;
}

function createPostRequest(url, fileName, authToken, dotnetRef) {
    const xhr = new XMLHttpRequest();
    wireEvents(xhr, dotnetRef, true);
    xhr.open('POST', url);
    if (authToken) {
        xhr.setRequestHeader('Authorization', authToken);
    }
    return xhr;
}

function createPutRequest(url, mimeType, dotnetRef) {
    const xhr = new XMLHttpRequest();
    wireEvents(xhr, dotnetRef, false);
    xhr.open('PUT', url);
    xhr.setRequestHeader('Content-Type', mimeType || 'application/octet-stream');
    return xhr;
}

function wireEvents(xhr, dotnetRef, supportsMisdirect) {
    xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
            dotnetRef.invokeMethodAsync('NotifyUploadProgress', e.loaded, e.total);
        }
    });

    xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
            dotnetRef.invokeMethodAsync('NotifyUploadComplete', xhr.responseText);
        } else if (supportsMisdirect && xhr.status === 421) {
            dotnetRef.invokeMethodAsync('NotifyUploadMisdirect', xhr.responseText, xhr.status);
        } else {
            dotnetRef.invokeMethodAsync('NotifyUploadError', `${xhr.status}: ${xhr.responseText}`);
        }
    });

    xhr.addEventListener('error', () => {
        dotnetRef.invokeMethodAsync(
            'NotifyUploadError',
            supportsMisdirect
                ? 'Network error during upload'
                : 'Network error during upload. The storage host may be unreachable or missing CORS configuration.');
    });

    xhr.addEventListener('abort', () => {
        dotnetRef.invokeMethodAsync('NotifyUploadCancelled');
    });
}
