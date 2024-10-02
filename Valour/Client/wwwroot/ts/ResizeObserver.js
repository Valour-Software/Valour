"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
const init = (element, dotnetRef, debounce = 0) => {
    const service = {
        timer: 0,
        observer: new ResizeObserver(async (entries) => {
            const rect = entries[0].contentRect;
            const dimensions = {
                width: rect.width,
                height: rect.height
            };
            service.timer += 1;
            if (service.timer > debounce) {
                service.timer = 0;
                await dotnetRef.invokeMethodAsync('NotifyResize', dimensions);
            }
        }),
        observe: () => {
            service.observer.observe(element);
        },
        dispose: () => {
            service.observer.disconnect();
        }
    };
    return service;
};
exports.init = init;
//# sourceMappingURL=ResizeObserver.js.map