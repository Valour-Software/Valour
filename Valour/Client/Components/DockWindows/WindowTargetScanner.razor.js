export const init = () => {
    const getTargetAtPoint = (mouseX, mouseY) => {
        let target = null;
        const elements = document.elementsFromPoint(mouseX, mouseY);
        elements.forEach((element) => {
            if (element.classList.contains('w-drop-target')) {
                target = element;
            }
        });
        return target;
    };

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
            const newTarget = getTargetAtPoint(mouseX, mouseY);
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
        },
        finalize: (mouseX, mouseY) => {
            const target = getTargetAtPoint(mouseX, mouseY) || service.currentTarget;
            if (target) {
                const ev = new MouseEvent('click', {
                    'view': window,
                    'bubbles': true,
                    'cancelable': true,
                    'screenX': mouseX,
                    'screenY': mouseY
                });
                target.dispatchEvent(ev);
                target.classList.remove('w-target-active');
                service.currentTarget = null;
                return true;
            }
            return false;
        }
    };
    return service;
};
//# sourceMappingURL=WindowTargetScanner.razor.js.map