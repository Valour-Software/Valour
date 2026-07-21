const publicKey = 'BBySvfFjJ4IihseXSujDGj4MsAd3ZAwDwy1Q2XxnMY6V3rCXjuqwm3utGvACAyjl0zEhj4Rk4Eoumy-znN_Y2VQ';
// navigator.serviceWorker.ready never resolves if the service worker failed to
// install, which would otherwise hang every caller (including the re-subscribe
// keepalive). Bound the wait so failures surface as "not available" instead.
const serviceWorkerReadyTimeoutMs = 10000;
function pushSupported() {
    return typeof Notification !== 'undefined'
        && typeof navigator !== 'undefined'
        && !!navigator.serviceWorker
        && 'PushManager' in window;
}
function getReadyRegistration() {
    return new Promise(resolve => {
        const timeout = setTimeout(() => resolve(null), serviceWorkerReadyTimeoutMs);
        navigator.serviceWorker.ready.then(registration => {
            clearTimeout(timeout);
            resolve(registration);
        }, () => {
            clearTimeout(timeout);
            resolve(null);
        });
    });
}
export function init() {
    return {
        notificationsEnabled: async () => {
            if (!pushSupported()) {
                return false;
            }
            const registration = await getReadyRegistration();
            if (!registration?.pushManager) {
                return false;
            }
            const existingSubscription = await registration.pushManager.getSubscription();
            return existingSubscription !== null;
        },
        getPermissionState: () => {
            if (typeof Notification === 'undefined') {
                return 'denied';
            }
            return Notification.permission;
        },
        askForPermission: async () => {
            if (typeof Notification === 'undefined') {
                return 'denied';
            }
            return await Notification.requestPermission();
        },
        requestSubscription: async () => {
            if (!pushSupported()) {
                return {
                    success: false,
                    error: 'Push notifications are not supported in this browser'
                };
            }
            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                return {
                    success: false,
                    error: 'Permission denied'
                };
            }
            const registration = await getReadyRegistration();
            if (!registration?.pushManager) {
                return {
                    success: false,
                    error: 'Service worker is not active'
                };
            }
            const existingSubscription = await registration.pushManager.getSubscription();
            try {
                if (existingSubscription) {
                    return {
                        success: true,
                        subscription: {
                            endpoint: existingSubscription.endpoint,
                            key: arrayBufferToBase64(existingSubscription.getKey('p256dh')),
                            auth: arrayBufferToBase64(existingSubscription.getKey('auth')),
                        }
                    };
                }
                else {
                    const newSub = await registration.pushManager.subscribe({
                        userVisibleOnly: true,
                        applicationServerKey: publicKey
                    });
                    return {
                        success: true,
                        subscription: {
                            endpoint: newSub.endpoint,
                            key: arrayBufferToBase64(newSub.getKey('p256dh')),
                            auth: arrayBufferToBase64(newSub.getKey('auth')),
                        }
                    };
                }
            }
            catch (e) {
                return {
                    success: false,
                    error: 'Unexpected error: ' + e.message
                };
            }
        },
        getSubscription: async () => {
            if (!pushSupported()) {
                return {
                    success: false,
                    error: 'Push notifications are not supported in this browser'
                };
            }
            const registration = await getReadyRegistration();
            if (!registration?.pushManager) {
                return {
                    success: false,
                    error: 'Service worker is not active'
                };
            }
            const existingSubscription = await registration.pushManager.getSubscription();
            if (existingSubscription) {
                return {
                    success: true,
                    subscription: {
                        endpoint: existingSubscription.endpoint,
                        key: arrayBufferToBase64(existingSubscription.getKey('p256dh')),
                        auth: arrayBufferToBase64(existingSubscription.getKey('auth')),
                    }
                };
            }
            else {
                return {
                    success: false,
                    error: 'No subscription found'
                };
            }
        },
        unsubscribe: async () => {
            if (!pushSupported()) {
                return;
            }
            const registration = await getReadyRegistration();
            if (!registration?.pushManager) {
                return;
            }
            const existingSubscription = await registration.pushManager.getSubscription();
            if (existingSubscription) {
                await existingSubscription.unsubscribe();
            }
        },
        dismissNotification: async (notificationId, sourceId) => {
            if (!pushSupported()) {
                return;
            }
            const registration = await getReadyRegistration();
            if (!registration) {
                return;
            }
            const notifications = await registration.getNotifications();
            for (const notification of notifications) {
                if (notification.data?.notificationId === notificationId ||
                    (sourceId && notification.data?.sourceId === sourceId)) {
                    notification.close();
                }
            }
        },
        dismissAllNotifications: async () => {
            if (!pushSupported()) {
                return;
            }
            const registration = await getReadyRegistration();
            if (!registration) {
                return;
            }
            const notifications = await registration.getNotifications();
            notifications.forEach(notification => notification.close());
        }
    };
}
function arrayBufferToBase64(buffer) {
    // https://stackoverflow.com/a/9458996
    let binary = '';
    const bytes = new Uint8Array(buffer);
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
}
//# sourceMappingURL=PushSubscriptionsComponent.razor.js.map