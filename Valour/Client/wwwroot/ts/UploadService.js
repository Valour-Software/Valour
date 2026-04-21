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
