// Resolves the latest GitHub release assets and triggers direct downloads.
(function () {
    const REPO = 'Valour-Software/Valour';

    // Maps a button's data-download value to the release asset file name.
    const ASSETS = {
        windows: 'ValourLauncher.exe',
        android: 'gg.valour.app-Signed.apk'
    };

    // GitHub redirects this URL to the matching asset on the latest release,
    // so it works even if the API call is rate-limited or fails.
    const fallbackUrl = (asset) =>
        `https://github.com/${REPO}/releases/latest/download/${asset}`;

    let releasePromise = null;
    function getLatestRelease() {
        if (!releasePromise) {
            releasePromise = fetch(
                `https://api.github.com/repos/${REPO}/releases/latest`,
                { headers: { Accept: 'application/vnd.github+json' } }
            )
                .then((res) => (res.ok ? res.json() : null))
                .catch(() => null);
        }
        return releasePromise;
    }

    function resolveAssetUrl(release, assetName) {
        if (release && Array.isArray(release.assets)) {
            const match = release.assets.find((a) => a.name === assetName);
            if (match && match.browser_download_url) {
                return match.browser_download_url;
            }
        }
        return fallbackUrl(assetName);
    }

    function triggerDownload(url) {
        const link = document.createElement('a');
        link.href = url;
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        link.remove();
    }

    function detectPlatform() {
        const platform = [
            navigator.userAgentData && navigator.userAgentData.platform,
            navigator.platform,
            navigator.userAgent
        ]
            .filter(Boolean)
            .join(' ')
            .toLowerCase();

        if (platform.includes('android')) return 'android';
        if (platform.includes('win')) return 'windows';
        return 'web';
    }

    // Shows the download that fits the visitor's device next to the web app
    // button. Devices without a native app get a link to every download instead.
    function setupDownloadPicker() {
        const picker = document.querySelector('[data-download-picker]');
        if (!picker) return;

        const currentPlatform = detectPlatform();
        const options = picker.querySelectorAll('[data-platform-option]');
        const note = document.querySelector('[data-platform-note]');
        const notes = {
            windows: 'Recommended for Windows. Also on Android and in the browser.',
            android: 'Recommended for Android. Also on Windows and in the browser.',
            web: 'Works in any modern browser. Apps for Windows and Android.'
        };

        options.forEach((option) => {
            const isCurrent = option.getAttribute('data-platform-option') === currentPlatform;
            option.classList.toggle('is-hidden', !isCurrent);
        });

        if (note) {
            note.textContent = notes[currentPlatform];
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        const buttons = document.querySelectorAll('[data-download]');
        setupDownloadPicker();
        if (!buttons.length) return;

        // Warm the cache so the first click feels instant.
        getLatestRelease();

        buttons.forEach((btn) => {
            const platform = btn.getAttribute('data-download');
            const assetName = ASSETS[platform];
            if (!assetName) return;

            // No-JS / safety fallback target.
            if (btn.tagName === 'A') {
                btn.setAttribute('href', fallbackUrl(assetName));
            }

            btn.addEventListener('click', async function (e) {
                e.preventDefault();
                if (btn.classList.contains('is-loading')) return;

                btn.classList.add('is-loading');
                try {
                    const release = await getLatestRelease();
                    triggerDownload(resolveAssetUrl(release, assetName));
                } finally {
                    btn.classList.remove('is-loading');
                }
            });
        });
    });
})();
