// Runtime-deploy config. Leave blank for same-origin API.
window.valourRuntimeConfig = window.valourRuntimeConfig || {};
if (typeof window.valourRuntimeConfig.apiOrigin !== "string")
    window.valourRuntimeConfig.apiOrigin = "";
// Origin serving the static public thread pages (e.g. https://threads.valour.gg).
// Leave blank to use the built-in default.
if (typeof window.valourRuntimeConfig.threadsOrigin !== "string")
    window.valourRuntimeConfig.threadsOrigin = "";
