import DotnetObject = DotNet.DotnetObject;

type InfiniteScrollService = {
    enabled: boolean;
    observer: IntersectionObserver | null;
    root: HTMLElement | null;
    observe: () => void;
    refresh: () => void;
    setEnabled: (enabled: boolean) => void;
    dispose: () => void;
};

const isScrollable = (element: HTMLElement): boolean => {
    const style = getComputedStyle(element);
    const overflowY = style.overflowY;
    return overflowY === "auto" || overflowY === "scroll" || overflowY === "overlay";
};

const findScrollParent = (element: HTMLElement | null): HTMLElement | null => {
    let current = element?.parentElement ?? null;

    while (current) {
        if (isScrollable(current)) {
            return current;
        }

        current = current.parentElement;
    }

    return null;
};

export const init = (
    element: HTMLElement,
    dotnetRef: DotnetObject,
    rootMargin: string = "300px"
): InfiniteScrollService => {
    const service: InfiniteScrollService = {
        enabled: true,
        observer: null,
        root: null,
        observe: () => {
            if (!service.enabled) {
                return;
            }

            if (!service.observer) {
                service.refresh();
            }

            service.observer?.observe(element);
        },
        refresh: () => {
            service.observer?.disconnect();
            service.root = findScrollParent(element);
            service.observer = new IntersectionObserver(async (entries) => {
                if (!service.enabled) {
                    return;
                }

                const isVisible = entries.some(entry => entry.isIntersecting);
                if (isVisible) {
                    await dotnetRef.invokeMethodAsync("OnSentinelVisible");
                }
            }, {
                root: service.root,
                rootMargin
            });

            if (service.enabled) {
                service.observer.observe(element);
            }
        },
        setEnabled: (enabled: boolean) => {
            service.enabled = enabled;

            if (!enabled) {
                service.observer?.disconnect();
                return;
            }

            service.refresh();
        },
        dispose: () => {
            service.observer?.disconnect();
            service.observer = null;
        }
    };

    return service;
};
