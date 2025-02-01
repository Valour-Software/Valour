const publicKey = 'BBySvfFjJ4IihseXSujDGj4MsAd3ZAwDwy1Q2XxnMY6V3rCXjuqwm3utGvACAyjl0zEhj4Rk4Eoumy-znN_Y2VQ';

type PushSubscriptionResult = {
    success: boolean;
    error?: string;
    subscription?: {
        endpoint: string;
        key: string;
        auth: string;
    }
}

type PushNotificationsService = {
    notificationsEnabled: () => Promise<boolean>;
    getPermissionState: () => NotificationPermission;
    askForPermission: () => Promise<NotificationPermission>;
    requestSubscription: () => Promise<PushSubscriptionResult>;
    unsubscribe: () => Promise<void>;
}

export function init(): PushNotificationsService {
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
        requestSubscription: async (): Promise<PushSubscriptionResult> => {
            const permission = await Notification.requestPermission();
            
            if (permission !== 'granted') {
                return {
                    success: false,
                    error: 'Permission denied'
                }
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
                    }
                } else {
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
                    }
                }
            } catch (e) {
                return {
                    success: false,
                    error: 'Unexpected error: ' + e.message
                }
            }
        },
        unsubscribe: async () => {
            const registration = await navigator.serviceWorker.ready;
            const existingSubscription = await registration.pushManager.getSubscription();
            
            if (existingSubscription) {
                await existingSubscription.unsubscribe();
            }
        }
    }
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
    // https://stackoverflow.com/a/9458996
    let binary = '';
    const bytes = new Uint8Array(buffer);
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
}