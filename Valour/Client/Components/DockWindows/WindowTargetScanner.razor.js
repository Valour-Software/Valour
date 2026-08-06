export const init = () => {
    const service = {
        scanTimer: 0,
        currentTarget: null,
        scan: (mouseX, mouseY) => {
            // Optimize scan to only run every 5th frame
            service.scanTimer++;
            if (service.scanTimer < 5) {
                return;
            }
            service.scanTimer = 0;
            let newTarget = null;
            const elements = document.elementsFromPoint(mouseX, mouseY);
            console.log("Scanning...", elements);
            elements.forEach((element) => {
                if (element.classList.contains('w-drop-target')) {
                    newTarget = element;
                }
            });
            if (newTarget) {
                if (newTarget !== service.currentTarget) {
                    // Reset previous target
                    if (service.currentTarget) {
                        service.currentTarget.classList.remove('w-target-active');
                    }
                    // Set new target
                    service.currentTarget = newTarget;
                    service.currentTarget.classList.add('w-target-active');
                    console.log("New target found:", service.currentTarget);
                }
            }
            else {
                // If no target found, reset current target
                if (service.currentTarget) {
                    service.currentTarget.classList.remove('w-target-active');
                    service.currentTarget = null;
                }
            }
        },
        finalize: (mouseX, mouseY) => {
            if (service.currentTarget) {
                const ev = new MouseEvent('click', {
                    'view': window,
                    'bubbles': true,
                    'cancelable': true,
                    'screenX': mouseX,
                    'screenY': mouseY
                });
                service.currentTarget.dispatchEvent(ev);
                service.currentTarget = null;
            }
        }
    };
    // clientX/clientY on the relayed @ondrag event don't track the real
    // cursor for native drags, so scan() above misses channel drags -
    // dragover always has the real hovered element. Capture phase because
    // .drop-targets calls stopPropagation() on the bubble.
    document.addEventListener('dragover', (e) => {
        const hit = e.target?.closest?.('.w-drop-target');
        if (hit === service.currentTarget) {
            return;
        }
        service.currentTarget?.classList.remove('w-target-active');
        service.currentTarget = hit;
        service.currentTarget?.classList.add('w-target-active');
    }, true);
    return service;
};
//# sourceMappingURL=WindowTargetScanner.razor.js.map