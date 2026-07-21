let dotNetRef = null;

function createState(tabId, contentId) {
    return {
        ...(window.history.state ?? {}),
        valourNavigation: { tabId, contentId }
    };
}

function onPopState(event) {
    const navigation = event.state?.valourNavigation;
    if (!navigation || !dotNetRef)
        return;

    void dotNetRef.invokeMethodAsync(
        "OnBrowserHistoryNavigation",
        navigation.tabId,
        navigation.contentId);
}

export function initialize(ref, tabId, contentId) {
    dotNetRef = ref;
    window.history.replaceState(createState(tabId, contentId), "");
    window.addEventListener("popstate", onPopState);
}

export function push(tabId, contentId) {
    const current = window.history.state?.valourNavigation;
    if (current?.tabId === tabId && current?.contentId === contentId)
        return;

    window.history.pushState(createState(tabId, contentId), "");
}

export function dispose() {
    window.removeEventListener("popstate", onPopState);
    dotNetRef = null;
}
