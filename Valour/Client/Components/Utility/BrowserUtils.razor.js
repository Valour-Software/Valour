"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.getElementPosition = exports.getElementDimensionsBySelector = exports.getElementDimensions = exports.getWindowDimensions = exports.init = void 0;
const init = (dotnet) => {
    const onResize = () => {
        const dimensions = (0, exports.getWindowDimensions)();
        dotnet.invokeMethod('NotifyWindowDimensions', { width: dimensions.width, height: dimensions.height });
    };
    window.addEventListener('resize', onResize);
};
exports.init = init;
const getWindowDimensions = () => {
    const { innerWidth: width, innerHeight: height } = window;
    return { width, height };
};
exports.getWindowDimensions = getWindowDimensions;
const getElementDimensions = (element) => {
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
exports.getElementDimensions = getElementDimensions;
const getElementDimensionsBySelector = (selector) => {
    const element = document.querySelector(selector);
    const { clientWidth: width, clientHeight: height } = element;
    return { width, height };
};
exports.getElementDimensionsBySelector = getElementDimensionsBySelector;
const getElementPosition = (element) => {
    const { left, top } = element.getBoundingClientRect();
    return { x: left, y: top };
};
exports.getElementPosition = getElementPosition;
//# sourceMappingURL=BrowserUtils.razor.js.map