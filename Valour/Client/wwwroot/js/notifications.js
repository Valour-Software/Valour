var applicationServerPublicKey;
var serviceWorker = '/noti-worker.js';
var isSubscribed = false;

function SetVapidKey(key) {
    applicationServerPublicKey = key;
}

function enableNotifications() {
    if (typeof applicationServerPublicKey === 'undefined') {
        errorHandler('Vapid public key is undefined.');
        return;
    }

    Notification.requestPermission().then(function (status) {
        if (status === 'denied') {
            errorHandler('[Notification.requestPermission] Browser denied permissions to notification api.');
        } else if (status === 'granted') {
            console.log('[Notification.requestPermission] Initializing service worker.');
            initialiseServiceWorker();
        }
    });

    subscribe();
}

function initialiseServiceWorker() {
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.register(serviceWorker).then(handleSWRegistration);
    } else {
        errorHandler('[initialiseServiceWorker] Service workers are not supported in this browser.');
    }
};

function handleSWRegistration(reg) {
    if (reg.installing) {
        console.log('Notifications Service worker installing');
    } else if (reg.waiting) {
        console.log('Notifications Service worker installed');
    } else if (reg.active) {
        console.log('Notifications Service worker active');
    }

    initialiseState(reg);
}

// Once the service worker is registered set the initial state
function initialiseState(reg) {
    // Are Notifications supported in the service worker?
    if (!(reg.showNotification)) {
        errorHandler('[initialiseState] Notifications aren\'t supported on service workers.');
        return;
    }

    // Check if push messaging is supported
    if (!('PushManager' in window)) {
        errorHandler('[initialiseState] Push messaging isn\'t supported.');
        return;
    }

    // We need the service worker registration to check for a subscription
    navigator.serviceWorker.ready.then(function (reg) {
        // Do we already have a push message subscription?
        reg.pushManager.getSubscription()
            .then(function (subscription) {
                isSubscribed = subscription;
                if (isSubscribed) {
                    console.log('User is already subscribed to push notifications');
                } else {
                    console.log('User is not yet subscribed to push notifications');
                }

                

                // console.log("Expires in: " + subscription.expirationTime);
            })
            .catch(function (err) {
                console.log('[req.pushManager.getSubscription] Unable to get subscription details.', err);
            });
    });
}

function subscribe() {
    navigator.serviceWorker.ready.then(function (reg) {
        var subscribeParams = { userVisibleOnly: true };

        //Setting the public key of our VAPID key pair.
        var applicationServerKey = urlB64ToUint8Array(applicationServerPublicKey);
        subscribeParams.applicationServerKey = applicationServerKey;

        reg.pushManager.subscribe(subscribeParams)
            .then(function (subscription) {
                isSubscribed = true;

                var p256dh = base64Encode(subscription.getKey('p256dh'));
                var auth = base64Encode(subscription.getKey('auth'));

                console.log(subscription);

                TransmitSub(subscription.endpoint, p256dh, auth);
            })
            .catch(function (e) {
                errorHandler('[subscribe] Unable to subscribe to push', e);
            });
    });
}

function TransmitSub(endpoint, key, auth) {
    DotNet.invokeMethodAsync('Valour.Client', 'NotifyPushSub', endpoint, key, auth);
}

function errorHandler(message, e) {
    if (typeof e == 'undefined') {
        e = null;
    }

    console.error(message, e);
    $("#errorMessage").append('<li>' + message + '</li>').parent().show();
}

function urlB64ToUint8Array(base64String) {
    var padding = '='.repeat((4 - base64String.length % 4) % 4);
    var base64 = (base64String + padding)
        .replace(/\-/g, '+')
        .replace(/_/g, '/');

    var rawData = window.atob(base64);
    var outputArray = new Uint8Array(rawData.length);

    for (var i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

function base64Encode(arrayBuffer) {
    return btoa(String.fromCharCode.apply(null, new Uint8Array(arrayBuffer)));
}