import DotNetObject = DotNet.DotNetObject;

type MouseMoveService = {
    lastX: number;
    lastY: number;
    
    moveListener: (e: MouseEvent) => void;
    
    startMoveListener: () => void;
    stopMoveListener: () => void;
    
    upListener: (e: MouseEvent) => void;
    
    startUpListener: () => void;
    stopUpListener: () => void;
};

export const init = (dotnet: DotNetObject): MouseMoveService => {
    const service = {
        lastX: 0,
        lastY: 0,
        
        moveListener: (e: MouseEvent) => {
            if (e.clientX === service.lastX && e.clientY === service.lastY) {
                return;
            }

            const deltaX = e.clientX - service.lastX;
            const deltaY = e.clientY - service.lastY;

            service.lastX = e.clientX;
            service.lastY = e.clientY;

            dotnet.invokeMethod('NotifyMouseMove', e.clientX, e.clientY, deltaX, deltaY);
        },
        
        startMoveListener: () => {
            document.addEventListener('mousemove', service.moveListener);
        },
        
        stopMoveListener: () => {
            document.removeEventListener('mousemove', service.moveListener);
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