// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

// This will be replaced by your build function
const COMMIT_HASH = '$(SHORTHASH)';

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => {
    const requestUrl = new URL(event.request.url);

    // The offline cache only contains this app's static assets. Let cross-origin
    // CDN/API requests and non-GET requests pass through without service-worker
    // interception; wrapping them in fetch() adds overhead and makes DevTools show
    // a second service-worker request for the same network operation.
    if (event.request.method !== 'GET' || requestUrl.origin !== self.location.origin)
        return;

    event.respondWith(onFetch(event));
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}-${COMMIT_HASH}`;
// The precache holds what the app needs to start: the .NET runtime and assemblies,
// the stylesheet bundle, fonts, startup and component scripts, and the logo and
// favicons. Other assets (images, sounds, feature-specific scripts and data) are
// fetched from the network when first used and kept by the browser's HTTP cache.
//
// index.html is never precached because Cloudflare Pages serves it through a
// pretty-URL redirect whose cached body can come from a different deployment. That
// fails the integrity check and breaks the whole install, which kills push
// notifications.
const offlineAssetsInclude = [
    /^_framework\/[^/]+\.(?:wasm|js)$/,
    // The runtime loads one ICU data shard, chosen from the browser language. EFIGS
    // covers English, French, Italian, German and Spanish. Other shards load from
    // the network.
    /^_framework\/icudt_EFIGS\.(?:[^/]+\.)?dat$/,
    // index.html loads only the combined stylesheet bundle. The *.scp.css bundle
    // is loaded only by the MAUI host.
    /^_content\/Valour\.Client\/css\/bundled\.min\.css$/,
    /^_content\/Valour\.Client\/css\/fonts\/[^/]+\.woff2$/,
    /^_content\/Valour\.Client\/.+\.js$/,
    /^_content\/Valour\.Client\/media\/logo\/[^/]+\.webp$/,
    /^_content\/Valour\.Client\/media\/favicon\/favicon-[^/]+\.png$/,
    /^manifest\.json$/,
];
// Large scripts used only by calls, the demo engine, villages, and the staff
// dashboard, plus the largest logo.
const offlineAssetsExclude = [
    /\/js\/(?:livekit-client\.umd|livekit\.interop|realtimekit)\.js$/,
    /\/Components\/Calls\//,
    /\/demo\//,
    /\/Villages\//,
    /\/ts\/Village[^/]*\.js$/,
    /\/Staff\//,
    /\/logo-square-1k\.webp$/,
];

async function onInstall(event) {
    console.info('Service worker: Install with commit hash:', COMMIT_HASH);

    // Activate the new service worker as soon as it has installed
    await self.skipWaiting();

    // Caches from earlier deployments stay in place until onActivate removes them,
    // because the previous worker may still be serving the open page from them.

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        // Fingerprinted framework assets can be reused from the HTTP cache when a
        // new service worker cache is populated. Non-fingerprinted assets still
        // revalidate according to their Cache-Control response headers.
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'default' }));

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

        // The assets manifest stores logical paths without the deploy-version
        // query used by index.html and dynamic component imports. Reuse the
        // current worker's cached asset only when the request version exactly
        // matches this deployment. An older controlling worker therefore
        // cannot accidentally serve an old script to a newly deployed page.
        if (!cachedResponse && !shouldServeIndexHtml) {
            const requestUrl = new URL(event.request.url);
            const isCurrentVersion = requestUrl.searchParams.get('version') === COMMIT_HASH;
            const isRuntimeConfig = requestUrl.pathname.endsWith('/valour-runtime-config.js');

            if (isCurrentVersion && !isRuntimeConfig) {
                requestUrl.searchParams.delete('version');
                cachedResponse = await cache.match(requestUrl.toString());
            }
        }
    }

    return cachedResponse || fetch(event.request);
}

self.addEventListener('push', event => {
    console.log('[Service Worker] Push Received.');
    
    const payload = event.data.json();

    console.log(payload);

    const tag = payload.notificationId
        ? `notification-${payload.notificationId}`
        : payload.sourceId
            ? `source-${payload.sourceId}`
            : undefined;
    
    event.waitUntil(
        self.registration.showNotification(payload.title, {
            body: payload.message,
            icon: payload.iconUrl,
            vibrate: [100, 50, 100],
            badge: "https://app.valour.gg/_content/Valour.Client/media/logo/logo-square-256.png",
            tag,
            // Without an explicit timestamp some Android shells render a bogus date
            timestamp: payload.timeSent ? Number(payload.timeSent) : Date.now(),
            data: {
                url: payload.url,
                notificationId: payload.notificationId,
                sourceId: payload.sourceId,
            },
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();

    const targetUrl = new URL(event.notification.data?.url || '/', self.location.origin).href;

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(windowClients => {
            for (const client of windowClients) {
                if ('focus' in client) {
                    client.focus();
                    if ('navigate' in client && targetUrl !== client.url) {
                        return client.navigate(targetUrl).catch(() => {});
                    }
                    return;
                }
            }
            return clients.openWindow(targetUrl);
        })
    );
});
