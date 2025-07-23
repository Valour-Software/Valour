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
            document.elementsFromPoint(mouseX, mouseY).forEach((element) => {
                if (element.classList.contains('w-drop-target')) {
                    newTarget = element;
                }
                if (newTarget) {
                    if (newTarget !== service.currentTarget) {
                        // Reset previous target
                        if (service.currentTarget) {
                            service.currentTarget.classList.remove('w-target-active');
                        }
                        // Set new target
                        service.currentTarget = newTarget;
                        service.currentTarget.classList.add('w-target-active');
                    }
                }
                else {
                    // If no target found, reset current target
                    if (service.currentTarget) {
                        service.currentTarget.classList.remove('w-target-active');
                        service.currentTarget = null;
                    }
                }
            });
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
    return service;
};
//# sourceMappingURL=WindowTargetScanner.razor.js.map