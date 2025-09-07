// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).

// If the server is still starting up, try again
self.addEventListener('fetch', () => {});


self.addEventListener('install', async event => {
    console.log('Installing service worker...');
    self.skipWaiting();
});

self.addEventListener('push', event => {
    
    console.log('[Service Worker] Push Received.');
    const payload = event.data.json();
    
    console.log(payload);
    
    event.waitUntil(
        self.registration.showNotification(payload.title, {
            body: payload.message,
            icon: payload.iconUrl,
            vibrate: [100, 50, 100],
            badge: "https://app.valour.gg/_content/Valour.Client/media/logo/victor-mono-192.png",
            //data: { url: payload.url }
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
});

