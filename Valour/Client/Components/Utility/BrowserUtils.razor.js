export const init = (dotnet) => {
    const onResize = async () => {
        const dimensions = getWindowDimensions();
        await dotnet.invokeMethodAsync('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
    };
    window.addEventListener('resize', onResize);
    // Check if Page Visibility API is supported
    const visibilityChangeEvent = "visibilitychange";
    const hiddenProperty = "hidden" in document ? "hidden" : undefined;
    let lastRefocus = null;
    // Function to handle refocus
    const handleRefocus = async () => {
        console.log("Refocus event detected.");
        if (lastRefocus && (new Date().getTime() - lastRefocus.getTime()) < 1000) {
            console.log("Ignoring refocus event, too soon.");
            return;
        }
        await dotnet.invokeMethodAsync("OnRefocus");
        lastRefocus = new Date();
    };
    // Page visibility change event listener
    if (hiddenProperty) {
        document.addEventListener(visibilityChangeEvent, async () => {
            console.log("Visibility change event detected.");
            if (!document[hiddenProperty]) {
                // Page is visible
                await handleRefocus();
            }
        });
    }
    // Window focus event listener
    window.addEventListener("focus", async () => {
        console.log("Window focus event detected.");
        await handleRefocus();
    });
};
export const getWindowDimensions = () => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};
export const getElementDimensions = (element) => {
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
export const getElementDimensionsBySelector = (selector) => {
    const element = document.querySelector(selector);
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
export const getElementPosition = (element) => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};
export const getVerticalDistancesToContainer = (element, container) => {
    // Get the bounding rectangle of the element
    const elementRect = element.getBoundingClientRect();
    // Get the bounding rectangle of the scrollable container
    const containerRect = container.getBoundingClientRect();
    // Calculate the distance from the top of the element to the top of the entire scroll
    const topDistance = elementRect.top - containerRect.top;
    // Calculate the distance from the bottom of the element to the bottom of the entire scroll
    const bottomDistance = containerRect.bottom - elementRect.bottom;
    return {
        topDistance,
        bottomDistance,
    };
};
export const getVisibleVerticalDistancesToContainer = (element, container) => {
    // Get the bounding rectangle of the element and container relative to the viewport
    const elementRect = element.getBoundingClientRect();
    const containerRect = container.getBoundingClientRect();
    // Calculate the distance from the top of the element to the top of the container
    const topDistance = elementRect.top - containerRect.top;
    // Determine the visible bottom boundary within the viewport and container
    const visibleBottomBoundary = Math.min(containerRect.bottom, window.innerHeight);
    // Calculate the distance from the element's bottom to the visible bottom boundary
    const bottomDistance = visibleBottomBoundary - elementRect.bottom;
    return {
        topDistance,
        bottomDistance,
    };
};
export const getWindowUri = () => {
    return {
        href: window.location.href,
        origin: window.location.origin,
        protocol: window.location.protocol,
        host: window.location.host,
        hostname: window.location.hostname,
        port: window.location.port,
        pathname: window.location.pathname,
        search: window.location.search,
        hash: window.location.hash
    };
};
//# sourceMappingURL=BrowserUtils.razor.js.map