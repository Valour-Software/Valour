import DotnetObject = DotNet.DotnetObject;

type Dimensions = {
    width: number;
    height: number;
};

export const init = (dotnet: DotnetObject) => {
    const onResize = async () => {
        const dimensions = getWindowDimensions();
        await dotnet.invokeMethodAsync('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
    };
    
    window.addEventListener('resize', onResize);
};

export const getWindowDimensions = (): Dimensions => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};

export const getElementDimensions = (element: HTMLElement): Dimensions => {
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
}

export const getElementDimensionsBySelector = (selector: string): Dimensions => {
    const element = document.querySelector(selector) as HTMLElement;
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
}

export const getElementPosition = (element: HTMLElement): { x: number, y: number } => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};

export const getVerticalDistancesToContainer 
    = (element: HTMLElement, container: HTMLElement) => {
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
}

export const getVisibleVerticalDistancesToContainer
    = (element: HTMLElement, container: HTMLElement) => {
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
