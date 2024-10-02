"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
const init = () => {
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
                        if (service.currentTarget) {
                            service.currentTarget.style.backgroundColor = '#fff';
                        }
                        service.currentTarget = newTarget;
                        service.currentTarget.style.backgroundColor = '#0ff';
                    }
                }
                else {
                    if (service.currentTarget) {
                        service.currentTarget.style.backgroundColor = '#fff';
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
exports.init = init;
//# sourceMappingURL=WindowTargetScanner.razor.js.map