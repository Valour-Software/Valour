type WindowTargetService = {
    scanTimer: number;
    currentTarget: HTMLElement | null;
    scan: (mouseX: number, mouseY: number) => void;
    finalize: (mouseX: number, mouseY: number) => void;
};

export const init = (): WindowTargetService => {
    const service: WindowTargetService = {
        scanTimer: 0,
        currentTarget: null,
        scan: (mouseX: number, mouseY: number) => {
            // Optimize scan to only run every 5th frame
            service.scanTimer++;
            if (service.scanTimer < 5) {
                return;
            }
            service.scanTimer = 0;

            let newTarget: HTMLElement = null;
            
            document.elementsFromPoint(mouseX, mouseY).forEach((element) => {
                if (element.classList.contains('w-drop-target')) {
                    newTarget = element as HTMLElement;
                }

                if (newTarget) {
                    if (newTarget !== service.currentTarget) {
                        if (service.currentTarget) {
                            service.currentTarget.style.backgroundColor = '#fff';
                        }
                        service.currentTarget = newTarget;
                        service.currentTarget.style.backgroundColor = '#0ff';
                    }
                } else {
                    if (service.currentTarget) {
                        service.currentTarget.style.backgroundColor = '#fff';
                        service.currentTarget = null;
                    }
                }
            });
        },
        finalize: (mouseX: number, mouseY: number) => {
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
}