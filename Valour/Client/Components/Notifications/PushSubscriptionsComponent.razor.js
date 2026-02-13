const publicKey = 'BBySvfFjJ4IihseXSujDGj4MsAd3ZAwDwy1Q2XxnMY6V3rCXjuqwm3utGvACAyjl0zEhj4Rk4Eoumy-znN_Y2VQ';
const isSupported = typeof Notification !== 'undefined' && 'serviceWorker' in navigator;
export function init() {
    if (!isSupported) {
        return {
            notificationsEnabled: () => false,
            getPermissionState: () => 'denied',
            askForPermission: () => 'denied',
            requestSubscription: () => ({ success: false, error: 'Push notifications are not supported in this environment' }),
            getSubscription: () => ({ success: false, error: 'Push notifications are not supported in this environment' }),
            unsubscribe: () => {}
        };
    }
    return {
        notificationsEnabled: async () => {
            const registration = await navigator.serviceWorker.ready;
            const existingSubscription = await registration.pushManager.getSubscription();
            return existingSubscription !== null;
        },
        getPermissionState: () => {
            return Notification.permission;
        },
        askForPermission: async () => {
            return await Notification.requestPermission();
        },
        requestSubscription: async () => {
            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                return {
                    success: false,
                    error: 'Permission denied'
                };
            }
            const registration = await navigator.serviceWorker.ready;
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
            const registration = await navigator.serviceWorker.ready;
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
            const registration = await navigator.serviceWorker.ready;
            const existingSubscription = await registration.pushManager.getSubscription();
            if (existingSubscription) {
                await existingSubscription.unsubscribe();
            }
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