// Pushes instant notifications when someone joins/leaves the modded-client
// online list, so the client doesn't have to rely purely on polling.
window.moddedPresence = {
    _source: null,
    connect: function (url) {
        if (this._source) return;

        const source = new EventSource(url);
        source.addEventListener("presence-changed", function () {
            DotNet.invokeMethodAsync("Valour.Client", "OnModdedPresenceChanged");
        });

        this._source = source;
    }
};
