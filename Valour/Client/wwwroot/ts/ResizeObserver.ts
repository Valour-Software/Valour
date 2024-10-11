import DotnetObject = DotNet.DotnetObject;

type ElementDimensions = {
    width: number;
    height: number;
};

type ResizeService = {
    observer: ResizeObserver;
    observe: () => void;
    dispose: () => void;
    timer: number;
}

export const init = (element: HTMLElement, dotnetRef: DotnetObject, debounce: number = 0) => {
    const service: ResizeService = {
        timer: 0,
        observer: new ResizeObserver(async (entries) => {
            const rect = entries[0].contentRect;
            const dimensions: ElementDimensions = {
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
}
