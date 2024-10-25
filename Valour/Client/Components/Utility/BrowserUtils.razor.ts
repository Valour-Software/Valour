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