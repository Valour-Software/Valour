// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

self.addEventListener('push', function (event) {

    console.log("Recieved push notification");

    if (!(self.Notification && self.Notification.permission === 'granted')) {
        return;
    }

    var data = {};
    if (event.data) {
        data = event.data.json();
    }

    console.log('Notification Received:');
    console.log(data);

    var title = data.title;
    var message = data.message;
    var icon = "images/push-icon.jpg";

    event.waitUntil(self.registration.showNotification(title, {
        body: message,
        icon: icon,
        badge: icon
    }));
});

self.addEventListener('notificationclick', function (event) {
    event.notification.close();
});

self.addEventListener('pushsubscriptionchange', e => {

    console.log("Detected push subscription change!")

    e.waitUntil(registration.pushManager.subscribe(e.oldSubscription.options)
        .then(subscription => {
            console.log("Updating notification subscription.");

            var p256dh = base64Encode(subscription.getKey('p256dh'));
            var auth = base64Encode(subscription.getKey('auth'));

            TransmitSub(subscription.endpoint, p256dh, auth);
        }));
});

