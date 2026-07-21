const states = new WeakMap();
const viewportObservers = new Map();
const rootedObservers = new WeakMap();
const isScrollable = (element) => {
    const overflowY = getComputedStyle(element).overflowY;
    return overflowY === "auto" || overflowY === "scroll" || overflowY === "overlay";
};
const findScrollParent = (element) => {
    let current = element.parentElement;
    while (current) {
        if (isScrollable(current)) {
            return current;
        }
        current = current.parentElement;
    }
    return null;
};
const load = (state) => {
    state.loaded = true;
    state.usingFallback = false;
    if (state.src) {
        state.element.src = state.src;
    }
    else {
        state.element.removeAttribute("src");
    }
};
const createObserver = (root, rootMargin) => {
    let observer;
    observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) {
                continue;
            }
            const element = entry.target;
            const state = states.get(element);
            observer.unobserve(element);
            if (state) {
                load(state);
            }
        }
    }, {
        root,
        rootMargin,
        threshold: 0.01
    });
    return observer;
};
const getObserver = (element, rootMargin) => {
    const root = findScrollParent(element);
    if (!root) {
        let observer = viewportObservers.get(rootMargin);
        if (!observer) {
            observer = createObserver(null, rootMargin);
            viewportObservers.set(rootMargin, observer);
        }
        return observer;
    }
    let observers = rootedObservers.get(root);
    if (!observers) {
        observers = new Map();
        rootedObservers.set(root, observers);
    }
    let observer = observers.get(rootMargin);
    if (!observer) {
        observer = createObserver(root, rootMargin);
        observers.set(rootMargin, observer);
    }
    return observer;
};
export const observe = (element, src, fallbackSrc = "", rootMargin = "600px 0px") => {
    const state = {
        element,
        src: src ?? "",
        fallbackSrc: fallbackSrc ?? "",
        observer: null,
        loaded: false,
        usingFallback: false,
        onError: () => {
            if (!state.usingFallback && state.fallbackSrc) {
                state.usingFallback = true;
                state.element.src = state.fallbackSrc;
            }
        }
    };
    states.set(element, state);
    element.addEventListener("error", state.onError);
    if (typeof IntersectionObserver === "undefined") {
        load(state);
    }
    else {
        state.observer = getObserver(element, rootMargin);
        state.observer.observe(element);
    }
    return {
        update: (nextSrc, nextFallbackSrc = "") => {
            const normalizedSrc = nextSrc ?? "";
            const normalizedFallback = nextFallbackSrc ?? "";
            if (state.src === normalizedSrc && state.fallbackSrc === normalizedFallback) {
                return;
            }
            state.src = normalizedSrc;
            state.fallbackSrc = normalizedFallback;
            if (state.loaded) {
                load(state);
            }
        },
        dispose: () => {
            state.observer?.unobserve(element);
            element.removeEventListener("error", state.onError);
            states.delete(element);
        }
    };
};
//# sourceMappingURL=LazyImage.razor.js.map