type WindowDimensions = {
    width: number;
    height: number;
};

export const getWindowDimensions = (): WindowDimensions => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};