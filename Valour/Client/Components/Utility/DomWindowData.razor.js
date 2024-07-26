export const mousePosition = { x: 0, y: 0 };
export const initialize = () => {
    // hook event to update mouse position from document root
    document.addEventListener("mousemove", (event) => {
        mousePosition.x = event.clientX;
        mousePosition.y = event.clientY;
    });
};
export const getWindowDimensions = () => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};
export const getMousePosition = () => {
    return mousePosition;
};
//# sourceMappingURL=DomWindowData.razor.js.map