export const init = (dotnet) => {
    const service = {
        lastX: 0,
        lastY: 0,
        moveListener: (e) => {
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
        upListener: (e) => {
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
//# sourceMappingURL=MouseListener.razor.js.map