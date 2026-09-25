// The document mousemove listener is attached only while .NET has a move
// subscriber or a JS-side drag is active. Moves are sent to .NET at most once
// per animation frame with the movement accumulated since the last send.
export const init = (dotnet) => {
    const service = {
        lastX: null,
        lastY: null,
        pendingMove: null,
        pendingDeltaX: 0,
        pendingDeltaY: 0,
        frameHandle: null,
        netMoveSubscribed: false,
        moveAttached: false,
        // Drag mode state
        dragElement: null,
        dragScannerRef: null,
        moveListener: (e) => {
            if (e.clientX === service.lastX && e.clientY === service.lastY) {
                return;
            }
            const deltaX = service.lastX !== null ? e.clientX - service.lastX : 0;
            const deltaY = service.lastY !== null ? e.clientY - service.lastY : 0;
            service.lastX = e.clientX;
            service.lastY = e.clientY;
            // JS-side drag mode: move element directly, no .NET interop
            if (service.dragElement) {
                document.body.classList.add('no-select');
                let newX = parseFloat(service.dragElement.style.left) + deltaX;
                let newY = parseFloat(service.dragElement.style.top) + deltaY;
                // Bounds checking (equivalent to EnsureOnScreen)
                const width = service.dragElement.offsetWidth;
                const height = service.dragElement.offsetHeight;
                if (newX < 0)
                    newX = 0;
                if (newY < 0)
                    newY = 0;
                if (newX + width > window.innerWidth)
                    newX = window.innerWidth - width;
                if (newY + height > window.innerHeight)
                    newY = window.innerHeight - height;
                service.dragElement.style.left = newX + 'px';
                service.dragElement.style.top = newY + 'px';
                // Run target scanner (it has its own internal throttle)
                if (service.dragScannerRef) {
                    service.dragScannerRef.scan(e.clientX, e.clientY);
                }
                return; // Skip .NET interop
            }
            if (!service.netMoveSubscribed) {
                return;
            }
            // A held button means a .NET subscriber is dragging something
            // (a splitter or a docked tab), so suppress text selection.
            if (e.buttons !== 0) {
                document.body.classList.add('no-select');
            }
            service.pendingMove = e;
            service.pendingDeltaX += deltaX;
            service.pendingDeltaY += deltaY;
            if (service.frameHandle === null) {
                service.frameHandle = requestAnimationFrame(service.flushMove);
            }
        },
        flushMove: () => {
            service.frameHandle = null;
            const move = service.pendingMove;
            const deltaX = service.pendingDeltaX;
            const deltaY = service.pendingDeltaY;
            service.pendingMove = null;
            service.pendingDeltaX = 0;
            service.pendingDeltaY = 0;
            if (!move || !service.netMoveSubscribed) {
                return;
            }
            dotnet.invokeMethodAsync('NotifyMouseMove', move.clientX, move.clientY, move.pageX, move.pageY, deltaX, deltaY)
                .catch((err) => console.error('MouseListener: NotifyMouseMove failed', err));
        },
        cancelPendingMove: () => {
            if (service.frameHandle !== null) {
                cancelAnimationFrame(service.frameHandle);
                service.frameHandle = null;
            }
            service.pendingMove = null;
            service.pendingDeltaX = 0;
            service.pendingDeltaY = 0;
        },
        updateMoveListener: () => {
            const needed = service.netMoveSubscribed || service.dragElement !== null;
            if (needed && !service.moveAttached) {
                document.addEventListener('mousemove', service.moveListener);
                service.moveAttached = true;
            }
            else if (!needed && service.moveAttached) {
                document.removeEventListener('mousemove', service.moveListener);
                service.moveAttached = false;
                service.cancelPendingMove();
                document.body.classList.remove('no-select');
            }
        },
        setMoveSubscribed: (subscribed) => {
            service.netMoveSubscribed = subscribed;
            if (!subscribed) {
                service.cancelPendingMove();
            }
            service.updateMoveListener();
        },
        startMoveListener: () => {
            service.setMoveSubscribed(true);
        },
        stopMoveListener: () => {
            service.setMoveSubscribed(false);
        },
        // Mouse drags start with a mousedown, so recording its position gives
        // the first move after a subscription an accurate delta.
        downListener: (e) => {
            service.lastX = e.clientX;
            service.lastY = e.clientY;
        },
        upListener: (e) => {
            document.body.classList.remove('no-select');
            // Deliver the final movement before the release.
            if (service.pendingMove) {
                if (service.frameHandle !== null) {
                    cancelAnimationFrame(service.frameHandle);
                }
                service.flushMove();
            }
            dotnet.invokeMethodAsync('NotifyMouseUp', e.clientX, e.clientY, e.pageX, e.pageY)
                .catch((err) => console.error('MouseListener: NotifyMouseUp failed', err));
        },
        startUpListener: () => {
            document.addEventListener('mousedown', service.downListener, true);
            document.addEventListener('mouseup', service.upListener);
        },
        stopUpListener: () => {
            document.removeEventListener('mousedown', service.downListener, true);
            document.removeEventListener('mouseup', service.upListener);
        },
        startDrag: (elementId, scannerRef) => {
            const el = document.getElementById(elementId);
            if (el) {
                service.dragElement = el;
                service.dragScannerRef = scannerRef;
                service.updateMoveListener();
            }
        },
        stopDrag: () => {
            if (service.dragElement) {
                const x = parseFloat(service.dragElement.style.left) || 0;
                const y = parseFloat(service.dragElement.style.top) || 0;
                service.dragElement = null;
                service.dragScannerRef = null;
                service.updateMoveListener();
                return [x, y];
            }
            return null;
        }
    };
    return service;
};
//# sourceMappingURL=MouseListener.razor.js.map