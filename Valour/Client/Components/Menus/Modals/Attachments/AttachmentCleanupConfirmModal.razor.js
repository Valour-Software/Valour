// Starts a browser download for a URL whose response carries a
// Content-Disposition: attachment header.
export function startDownload(url) {
    const link = document.createElement('a');
    link.href = url;
    link.rel = 'noopener';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
