const publicKey = 'BBySvfFjJ4IihseXSujDGj4MsAd3ZAwDwy1Q2XxnMY6V3rCXjuqwm3utGvACAyjl0zEhj4Rk4Eoumy-znN_Y2VQ';
export function init() {
    return {
        notificationsEnabled: async () => {
            if (!navigator.serviceWorker) return false;
            const registration = await navigator.serviceWorker.ready;
            if (!registration?.pushManager) return false;
            const existingSubscription = await registration.pushManager.getSubscription();
            return existingSubscription !== null;
        },
        getPermissionState: () => {
            if (typeof Notification === 'undefined') return 'denied';
            return Notification.permission;
        },
        askForPermission: async () => {
            if (typeof Notification === 'undefined') return 'denied';
            return await Notification.requestPermission();
        },
        requestSubscription: async () => {
            if (typeof Notification === 'undefined') {
                return { success: false, error: 'Notifications not supported' };
            }
            if (!navigator.serviceWorker) {
                return { success: false, error: 'Service workers not supported' };
            }
            const registration = await navigator.serviceWorker.ready;
            if (!registration?.pushManager) {
                return { success: false, error: 'Push not supported' };
            }
            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                return {
                    success: false,
                    error: 'Permission denied'
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
            if (!navigator.serviceWorker) {
                return { success: false, error: 'Service workers not supported' };
            }
            const registration = await navigator.serviceWorker.ready;
            if (!registration?.pushManager) {
                return { success: false, error: 'Push not supported' };
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
            if (!navigator.serviceWorker) return;
            const registration = await navigator.serviceWorker.ready;
            if (!registration?.pushManager) return;
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