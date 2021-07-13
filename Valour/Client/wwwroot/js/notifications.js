function enableNotifications() {

    console.log("Preparing notifications system.");

    window.blazorPushNotifications = {
        requestSubscription: async () => {
            const worker = await navigator.serviceWorker.getRegistration();
            const existingSubscription = await worker.pushManager.getSubscription();
            if (!existingSubscription) {
                const newSubscription = await subscribe(worker);
                if (newSubscription) {

                    var sub = {
                        url: newSubscription.endpoint,
                        p256dh: base64Encode(newSubscription.getKey('p256dh')),
                        auth: base64Encode(newSubscription.getKey('auth'))
                    };

                    console.log(sub);

                    return sub;
                }
            }
        }
    };

    async function subscribe(worker) {
        try {

            var applicationServerKey = urlB64ToUint8Array(applicationServerPublicKey);

            return await worker.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: applicationServerKey
            });
        } catch (error) {
            if (error.name === 'NotAllowedError') {
                return null;
            }
            throw error;
        }
    }

    function base64Encode(arrayBuffer) {
        return btoa(String.fromCharCode.apply(null, new Uint8Array(arrayBuffer)));
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
}

function TransmitSub(endpoint, key, auth) {
    DotNet.invokeMethodAsync('Valour.Client', 'NotifyPushSub', endpoint, key, auth);
}

function SetVapidKey(key) {
    applicationServerPublicKey = key;
}