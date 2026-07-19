// Runtime-deploy config. Leave blank for same-origin API.
window.valourRuntimeConfig = window.valourRuntimeConfig || {};
if (typeof window.valourRuntimeConfig.apiOrigin !== "string")
    window.valourRuntimeConfig.apiOrigin = "";
// Origin serving the static public thread pages (e.g. https://threads.valour.gg).
// Leave blank to use the built-in default.
if (typeof window.valourRuntimeConfig.threadsOrigin !== "string")
    window.valourRuntimeConfig.threadsOrigin = "";

// Klipy is a public client-side platform key, like the former Tenor key. It
// will be visible to anyone using the application, so use a dedicated web key
// and never place a server credential here.
if (typeof window.valourRuntimeConfig.klipyApiKey !== "string")
    window.valourRuntimeConfig.klipyApiKey = "aJjFKDoPYkUnpbYgxESxGeLGDBUFM2wrtaPkO1HEexkzJDGzG4xefZirEGHbZ3us";
