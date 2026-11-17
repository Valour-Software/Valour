// Starts a browser download. The browser shows its own progress and decides
// where the file goes.

function startDownload(href, fileName) {
    const link = document.createElement('a');
    link.href = href;
    link.download = fileName || '';
    link.rel = 'noopener';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}

// For a URL whose response is already sent as an attachment, or is served from
// the same origin. A cross-origin link ignores the file name, so the server's
// Content-Disposition header decides.
export function downloadUrl(url, fileName) {
    startDownload(url, fileName);
}

// For content the app holds in memory.
export function downloadBytes(fileName, mimeType, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    startDownload(url, fileName);
    // The browser reads the blob after the click, so release it later.
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}
