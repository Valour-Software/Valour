// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

// This will be replaced by your build function
const COMMIT_HASH = '$(SHORTHASH)';

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}-${COMMIT_HASH}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/];
const offlineAssetsExclude = [/^service-worker\.js$/, /^bundled\.min\.css$/];

// Function to check if the stored hash matches the current hash
async function hasHashChanged() {
    try {
        const storedHash = await localforage.getItem('commit-hash');
        return storedHash !== COMMIT_HASH;
    } catch (e) {
        console.error('Error checking hash:', e);
        // If there's an error, assume hash has changed to force update
        return true;
    }
}

// Function to store the current hash
async function storeCurrentHash() {
    try {
        await localforage.setItem('commit-hash', COMMIT_HASH);
        console.info('Stored new commit hash:', COMMIT_HASH);
    } catch (e) {
        console.error('Error storing hash:', e);
    }
}

async function onInstall(event) {
    console.info('Service worker: Install with commit hash:', COMMIT_HASH);

    // Activate the new service worker as soon as the old one is retired
    await self.skipWaiting();

    // Check if the hash has changed
    const hashChanged = await hasHashChanged();

    if (hashChanged) {
        console.info('Commit hash changed. Clearing all caches...');
        // Clear all existing caches
        const cacheKeys = await caches.keys();
        await Promise.all(
            cacheKeys.filter(key => key.startsWith(cacheNamePrefix)).map(key => caches.delete(key))
        );
    }

    // Store the current hash for future comparisons
    await storeCurrentHash();

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => caches.delete(key))
    );

    // Take control immediately
    await self.clients.claim();
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache
        const shouldServeIndexHtml =
            event.request.mode === 'navigate' &&
            !event.request.url.includes('/connect/') &&
            !event.request.url.includes('api');

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

self.addEventListener('push', event => {
    const payload = event.data.json();
    event.waitUntil(
        self.registration.showNotification(payload.title, {
            body: payload.message,
            icon: payload.iconUrl,
            vibrate: [100, 50, 100],
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
});

// You'll need to include localforage (or use IndexedDB directly)
// Add this line at the top of your file:
self.importScripts('https://cdnjs.cloudflare.com/ajax/libs/localforage/1.10.0/localforage.min.js');
