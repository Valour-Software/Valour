export const init = (dotnet) => {
    const onResize = () => {
        const dimensions = getWindowDimensions();
        dotnet.invokeMethod('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
    };
    window.addEventListener('resize', onResize);
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
//# sourceMappingURL=BrowserUtils.razor.js.map