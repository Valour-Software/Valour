type WindowDimensions = {
    width: number;
    height: number;
};

type ScreenPosition = {
    x: number;
    y: number;
};

export const mousePosition : ScreenPosition = { x: 0, y: 0 };

export const initialize = () => {
    // hook event to update mouse position from document root
    document.addEventListener("mousemove", (event) => {
        mousePosition.x = event.clientX;
        mousePosition.y = event.clientY;
    });
};

export const getWindowDimensions = (): WindowDimensions => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};

export const getMousePosition = (): ScreenPosition => {
    return mousePosition;
};