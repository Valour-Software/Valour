(function () {
    // Note: Replace with your own key pair before deploying
    const applicationServerPublicKey = 'BBySvfFjJ4IihseXSujDGj4MsAd3ZAwDwy1Q2XxnMY6V3rCXjuqwm3utGvACAyjl0zEhj4Rk4Eoumy-znN_Y2VQ';

    window.askForPushPermission = async () => {
        if (Notification.permission !== "granted") {
            await Notification.requestPermission();
        }
    };
    
    window.blazorPushNotifications = {
        requestSubscription: async (create = true) => {
            
            await window.askForPushPermission();
            
            const worker = await navigator.serviceWorker.getRegistration();
            const existingSubscription = await worker.pushManager.getSubscription();
            if (!existingSubscription) {

                if (!create)
                    return null;

                const newSubscription = await subscribe(worker);
                if (newSubscription) {
                    return {
                        endpoint: newSubscription.endpoint,
                        key: arrayBufferToBase64(newSubscription.getKey('p256dh')),
                        auth: arrayBufferToBase64(newSubscription.getKey('auth'))
                    };
                }
            }
            else {
                return {
                    endpoint: existingSubscription.endpoint,
                    key: arrayBufferToBase64(existingSubscription.getKey('p256dh')),
                    auth: arrayBufferToBase64(existingSubscription.getKey('auth'))
                };
            }

        },

        removeSubscription: async () => {
            const worker = await navigator.serviceWorker.getRegistration();
            const existingSubscription = await worker.pushManager.getSubscription();
            if (existingSubscription) {

                var sub = {
                    endpoint: existingSubscription.endpoint,
                    key: arrayBufferToBase64(existingSubscription.getKey('p256dh')),
                    auth: arrayBufferToBase64(existingSubscription.getKey('auth'))
                }

                existingSubscription.unsubscribe();
                return sub;
            }

            return null;
        },

        hasNotifications: async () => {
            if (Notification.permission !== "granted") {
                await Notification.requestPermission();
            }
            return !(Notification.permission !== "granted");
        }
    };

    async function subscribe(worker) {
        try {
            return await worker.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: applicationServerPublicKey
            });
        } catch (error) {
            if (error.name === 'NotAllowedError') {
                return null;
            }
            throw error;
        }
    }

    function arrayBufferToBase64(buffer) {
        // https://stackoverflow.com/a/9458996
        var binary = '';
        var bytes = new Uint8Array(buffer);
        var len = bytes.byteLength;
        for (var i = 0; i < len; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return window.btoa(binary);
    }
})();