import DotnetObject = DotNet.DotnetObject;

type Dimensions = {
    width: number;
    height: number;
};

export const init = (dotnet: DotnetObject) => {
    const onResize = () => {
        const dimensions = getWindowDimensions();
        dotnet.invokeMethod('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
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