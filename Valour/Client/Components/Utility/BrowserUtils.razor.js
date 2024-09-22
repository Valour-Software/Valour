export const getWindowDimensions = () => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};
export const getElementDimensions = (element) => {
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
export const getElementPosition = (element) => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};
//# sourceMappingURL=BrowserUtils.razor.js.map