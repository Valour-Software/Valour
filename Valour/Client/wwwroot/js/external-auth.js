// Popup for signing in with Google or Discord. The window is opened during
// the click (so browsers allow it) and pointed at the provider afterwards.
// The app collects the result from the server, not from this window.
window.valourExternalAuth = (() => {
    let popup = null;

    const features = () => {
        const width = 500, height = 700;
        const left = Math.max(0, (window.screenX || 0) + (window.outerWidth - width) / 2);
        const top = Math.max(0, (window.screenY || 0) + (window.outerHeight - height) / 2);
        return `popup=yes,width=${width},height=${height},left=${left},top=${top}`;
    };

    return {
        open() {
            popup = window.open('about:blank', 'valour-external-auth', features());
            return !!popup;
        },
        navigate(url) {
            if (!popup || popup.closed) return false;
            popup.location.href = url;
            return true;
        },
        openUrl(url) {
            popup = window.open(url, 'valour-external-auth', features());
            return !!popup;
        },
        close() {
            try {
                if (popup && !popup.closed) popup.close();
            } catch {
                // A provider page may have separated the popup from this window.
            }
            popup = null;
        },
    };
})();
