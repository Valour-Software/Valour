const scriptUrl = "https://unpkg.com/@lottiefiles/lottie-player@2.0.12/dist/lottie-player.js";
let loadPromise = null;

export function ensureLoaded() {
    if (customElements.get("lottie-player")) return Promise.resolve();
    if (loadPromise) return loadPromise;

    loadPromise = new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${scriptUrl}"]`);
        const script = existing ?? document.createElement("script");

        script.addEventListener("load", resolve, { once: true });
        script.addEventListener(
            "error",
            () => reject(new Error("Failed to load Lottie player")),
            { once: true });

        if (!existing) {
            script.src = scriptUrl;
            script.async = true;
            document.head.appendChild(script);
        }
    });

    return loadPromise;
}
