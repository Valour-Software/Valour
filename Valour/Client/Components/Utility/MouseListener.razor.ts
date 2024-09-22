import DotNetObject = DotNet.DotNetObject;

type MouseMoveService = {
    lastX?: number;
    lastY?: number;
    
    moveListener: (e: MouseEvent) => void;
    
    startMoveListener: () => void;
    stopMoveListener: () => void;
    
    upListener: (e: MouseEvent) => void;
    
    startUpListener: () => void;
    stopUpListener: () => void;
};

export const init = (dotnet: DotNetObject): MouseMoveService => {
    const service = {
        lastX: null,
        lastY: null,
        
        moveListener: (e: MouseEvent) => {
            if (e.clientX === service.lastX && e.clientY === service.lastY) {
                return;
            }
            
            const deltaX = service.lastX ? e.clientX - service.lastX : 0;
            const deltaY = service.lastY ? e.clientY - service.lastY : 0;

            service.lastX = e.clientX;
            service.lastY = e.clientY;

            dotnet.invokeMethod('NotifyMouseMove', e.clientX, e.clientY, deltaX, deltaY);
        },
        
        startMoveListener: () => {
            document.addEventListener('mousemove', service.moveListener);
        },
        
        stopMoveListener: () => {
            document.removeEventListener('mousemove', service.moveListener);
            service.lastX = null;
            service.lastY = null;
        },
        
        upListener: (e: MouseEvent) => {
            dotnet.invokeMethod('NotifyMouseUp', e.clientX, e.clientY);
        },
        
        startUpListener: () => {
            document.addEventListener('mouseup', service.upListener);
        },
        
        stopUpListener: () => {
            document.removeEventListener('mouseup', service.upListener);
        }
    };
    
    return service;
};