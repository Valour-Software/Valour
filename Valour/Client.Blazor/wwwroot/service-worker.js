// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).

self.addEventListener('install', async event => {
    console.log('Installing service worker...');
    self.skipWaiting();
});

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
            badge: "https://app.valour.gg/_content/Valour.Client/media/logo/victor-mono-192.png",
            tag,
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

