// Glue between the page and the Photino desktop host.
(function () {
    window.getValourHostMode = function () {
        return "hybrid";
    };

    // WebKitGTK crashes the page process when a worker commits frames from a
    // transferred OffscreenCanvas through its GPU canvas backend, so canvases
    // are drawn on the main thread.
    window.valourDisableOffscreenCanvasWorkers = true;

    // The web view can't hand navigation to the system browser by itself, so
    // links and window.open calls that leave the app are sent to the host,
    // which opens them in the default browser.
    const hostPrefix = "valour-host:";

    function isExternal(url) {
        return (url.protocol === "http:" || url.protocol === "https:" || url.protocol === "mailto:")
            && url.origin !== location.origin;
    }

    // Errors and warnings are copied to the host, which prints them in Debug
    // builds. WebKitGTK has no inspector window unless one is opened by hand.
    // Libraries log large object graphs, so only a shallow summary is sent.
    function describe(value, depth = 0) {
        if (value instanceof Error) return value.stack || String(value);
        if (value === null || typeof value !== "object") return String(value);
        if (depth >= 2) return Array.isArray(value) ? `[Array(${value.length})]` : "{…}";
        const entries = Array.isArray(value)
            ? value.slice(0, 10).map(item => describe(item, depth + 1))
            : Object.keys(value).slice(0, 10).map(key => `${key}: ${describe(value[key], depth + 1)}`);
        return Array.isArray(value) ? `[${entries.join(", ")}]` : `{${entries.join(", ")}}`;
    }

    function forwardConsole(level, values) {
        let text;
        try {
            text = values.map(value => describe(value)).join(" ").slice(0, 4000);
        } catch {
            text = "(unprintable console arguments)";
        }
        // Sending from inside a host message handler would re-enter the host.
        setTimeout(() => {
            try {
                window.external.sendMessage(hostPrefix + JSON.stringify({ type: "console", level, text }));
            } catch {
                // The host bridge may not exist yet during early page load.
            }
        }, 0);
    }

    for (const level of ["error", "warn"]) {
        const original = console[level].bind(console);
        console[level] = (...values) => {
            forwardConsole(level, values);
            original(...values);
        };
    }
    window.addEventListener("error", e => forwardConsole("error", [e.error || e.message]));
    window.addEventListener("unhandledrejection", e => forwardConsole("error", ["Unhandled rejection:", e.reason]));

    function openExternally(href) {
        window.external.sendMessage(hostPrefix + JSON.stringify({ type: "open", url: href }));
    }

    document.addEventListener("click", function (e) {
        if (e.defaultPrevented || e.button !== 0) return;
        const anchor = e.target instanceof Element ? e.target.closest("a[href]") : null;
        if (!anchor) return;

        let url;
        try {
            url = new URL(anchor.href, location.href);
        } catch {
            return;
        }

        if (isExternal(url)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            openExternally(url.href);
        }
    }, true);

    const nativeOpen = window.open.bind(window);
    window.open = function (target, name, features) {
        try {
            const url = new URL(target, location.href);
            if (isExternal(url)) {
                openExternally(url.href);
                return null;
            }
        } catch {
            // Fall through to the web view for anything that isn't a URL.
        }
        return nativeOpen(target, name, features);
    };
})();
