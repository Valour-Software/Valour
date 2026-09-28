// Resolves the latest GitHub release assets and triggers direct downloads.
(function () {
    const REPO = 'Valour-Software/Valour';

    // Maps a button's data-download value to the release asset file name.
    // "linux" picks the build that matches the visitor's processor.
    const ASSETS = {
        windows: 'ValourLauncher.exe',
        android: 'gg.valour.app-Signed.apk',
        'linux-x86_64': 'Valour-linux-x86_64.flatpak',
        'linux-aarch64': 'Valour-linux-aarch64.flatpak'
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

    // Browsers only hint at the processor. Chromium reports it through client
    // hints; Firefox includes it in navigator.platform. Anything else gets the
    // x86_64 build, which covers most Linux desktops.
    async function detectLinuxAsset() {
        let architecture = '';
        try {
            if (navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
                const hints = await navigator.userAgentData.getHighEntropyValues(['architecture', 'bitness']);
                architecture = hints.architecture || '';
            }
        } catch {
            // Client hints are optional.
        }
        const text = `${architecture} ${navigator.platform || ''} ${navigator.userAgent}`.toLowerCase();
        return /\b(arm|aarch64|armv8)/.test(text) ? ASSETS['linux-aarch64'] : ASSETS['linux-x86_64'];
    }

    function assetFor(platform) {
        return platform === 'linux' ? detectLinuxAsset() : Promise.resolve(ASSETS[platform]);
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
        // ChromeOS reports Linux too, but runs the web app rather than Flatpaks.
        if (platform.includes('cros')) return 'web';
        if (platform.includes('win')) return 'windows';
        if (platform.includes('linux') || platform.includes('x11')) return 'linux';
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
            windows: 'Recommended for Windows. Also on Linux, Android and in the browser.',
            linux: 'Recommended for Linux, as a Flatpak. Also on Windows, Android and in the browser.',
            android: 'Recommended for Android. Also on Windows, Linux and in the browser.',
            web: 'Works in any modern browser. Apps for Windows, Linux and Android.'
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
            if (platform !== 'linux' && !ASSETS[platform]) return;
            const assetName = assetFor(platform);

            // No-JS / safety fallback target.
            if (btn.tagName === 'A') {
                assetName.then((name) => btn.setAttribute('href', fallbackUrl(name)));
            }

            btn.addEventListener('click', async function (e) {
                e.preventDefault();
                if (btn.classList.contains('is-loading')) return;

                btn.classList.add('is-loading');
                try {
                    const [release, name] = await Promise.all([getLatestRelease(), assetName]);
                    triggerDownload(resolveAssetUrl(release, name));
                } finally {
                    btn.classList.remove('is-loading');
                }
            });
        });
    });
})();
