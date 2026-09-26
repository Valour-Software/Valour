// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).

// Decrypts message text for notifications. See notification-preview.js.
// A failure here only costs the text, so it must not stop the worker installing.
try {
    self.importScripts('./lib/noble-chacha.js', './notification-preview.js');
} catch (error) {
    console.warn('[Service Worker] Notification text previews are unavailable.', error);
}

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
    
    event.waitUntil(showPushNotification(payload, tag));
});

async function showPushNotification(payload, tag) {
    // Message text is end-to-end encrypted. When this device holds the
    // channel's key, the text replaces the placeholder the server sent.
    let body = payload.message;
    try {
        body = await self.ValourNotificationPreview?.textForPayload(payload) ?? body;
    } catch (error) {
        console.warn('[Service Worker] Could not decrypt the notification text.', error);
    }

    await self.registration.showNotification(payload.title, {
        body,
        icon: payload.iconUrl,
        vibrate: [100, 50, 100],
        badge: "https://app.valour.gg/_content/Valour.Client/media/logo/victor-mono-192.png",
        tag,
        // Without an explicit timestamp some Android shells render a bogus date
        timestamp: payload.timeSent ? Number(payload.timeSent) : Date.now(),
        data: {
            url: payload.url,
            notificationId: payload.notificationId,
            sourceId: payload.sourceId,
        },
    });
}

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

