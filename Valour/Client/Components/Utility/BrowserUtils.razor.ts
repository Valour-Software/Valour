import DotnetObject = DotNet.DotnetObject;

type Dimensions = {
    width: number;
    height: number;
};

type UriLocation = {
    href: string;
    origin: string;
    protocol: string;
    host: string;
    hostname: string;
    port: string;
    pathname: string;
    search: string;
    hash: string;
}
export const init = (dotnet: DotnetObject) => {
    const onResize = async () => {
        const dimensions = getWindowDimensions();
        await dotnet.invokeMethodAsync('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
    };

    const onBlur = async () => {
        await dotnet.invokeMethodAsync('NotifyBlur');
    };

    window.addEventListener('resize', onResize);
    window.addEventListener('blur', onBlur);

    // Check if Page Visibility API is supported
    const visibilityChangeEvent = "visibilitychange";
    const hiddenProperty = "hidden" in document ? "hidden" : undefined;
    let lastRefocus: Date | null = null;

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

            if (!document[hiddenProperty as keyof Document]) {
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

export const getWindowDimensions = (): Dimensions => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};

export const getElementDimensions = (element: HTMLElement | null): Dimensions => {
    if (!element)
        return { width: 0, height: 0 };

    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
}

export const getElementDimensionsBySelector = (selector: string): Dimensions => {
    const element = document.querySelector(selector) as HTMLElement | null;
    if (!element)
        return { width: 0, height: 0 };
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
}

export const getElementPosition = (element: HTMLElement): { x: number, y: number } => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};

export const getElementBoundingRect = (element: HTMLElement) => {
    if (!element || !element.getBoundingClientRect)
        return { top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 } as DOMRect;
    return element.getBoundingClientRect();
}

export const getVerticalDistancesToContainer
    = (element: HTMLElement, container: HTMLElement) => {
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
}

export const getVisibleVerticalDistancesToContainer
    = (element: HTMLElement, container: HTMLElement) => {
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

export const getWindowUri = (): UriLocation => {
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
}