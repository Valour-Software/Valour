export const init = (dotnet) => {
    let disposed = false;
    let lastRefocus = 0;
    const notify = async (method, ...args) => {
        if (disposed)
            return;
        try {
            await dotnet.invokeMethodAsync(method, ...args);
        }
        catch (error) {
            if (!disposed)
                throw error;
        }
    };
    const onResize = () => notify('NotifyWindowDimensions', getWindowDimensions());
    const onBlur = () => notify('NotifyBlur');
    const onFocus = () => {
        const now = Date.now();
        if (now - lastRefocus < 1000)
            return;
        lastRefocus = now;
        return notify('OnRefocus');
    };
    const onVisibility = () => document.hidden ? onBlur() : onFocus();
    window.addEventListener('resize', onResize);
    window.addEventListener('blur', onBlur);
    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisibility);
    return {
        dispose: () => {
            disposed = true;
            window.removeEventListener('resize', onResize);
            window.removeEventListener('blur', onBlur);
            window.removeEventListener('focus', onFocus);
            document.removeEventListener('visibilitychange', onVisibility);
        }
    };
};
export const getWindowDimensions = () => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};
export const getElementDimensions = (element) => {
    if (!element)
        return { width: 0, height: 0 };
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
export const getElementDimensionsBySelector = (selector) => {
    const element = document.querySelector(selector);
    if (!element)
        return { width: 0, height: 0 };
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
export const getElementPosition = (element) => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};
export const getElementBoundingRect = (element) => {
    if (!element || !element.getBoundingClientRect)
        return { top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 };
    return element.getBoundingClientRect();
};
export const getVerticalDistancesToContainer = (element, container) => {
    if (!element || !element.getBoundingClientRect || !container || !container.getBoundingClientRect)
        return { topDistance: 0, bottomDistance: 0 };
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
    if (!element || !element.getBoundingClientRect || !container || !container.getBoundingClientRect)
        return { topDistance: 0, bottomDistance: 0 };
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