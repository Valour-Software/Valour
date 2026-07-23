type PullToRefreshService = {
    setEnabled: (enabled: boolean) => void;
    complete: () => void;
    dispose: () => void;
};

type DotnetRef = {
    invokeMethodAsync<T>(methodIdentifier: string, ...args: unknown[]): Promise<T>;
};

/**
 * Reusable drag-down-to-refresh gesture (Twitter-style).
 *
 * Attaches touch listeners to the nearest scrollable ancestor of the
 * component root. When that container is scrolled to the top and the user
 * drags down, the content follows the finger with rubber-band resistance
 * while an indicator badge slides in. Releasing past the threshold invokes
 * 'OnPullRefresh' on the .NET ref; the spinner holds until .NET calls
 * complete().
 *
 * Touch-only by nature (touch events never fire for mouse input), so
 * desktop scrolling and text selection are unaffected.
 */

// Minimum time the spinner shows, so instant refreshes don't flash
const MIN_SPIN_MS = 500;
// Matches the .ptr-settling transition duration in the CSS
const SETTLE_MS = 400;
// Movement (px) before we commit to pull vs scroll/abandon
const INTENT_SLOP = 8;

const isScrollable = (element: HTMLElement): boolean => {
    const overflowY = getComputedStyle(element).overflowY;
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
    root: HTMLElement,
    dotnetRef: DotnetRef,
    threshold: number = 72,
    maxPull: number = 144
): PullToRefreshService => {
    const scrollParent = findScrollParent(root);
    const scrollEl = scrollParent ?? (document.scrollingElement as HTMLElement | null);
    const listenEl: EventTarget = scrollParent ?? document;

    let enabled = true;
    let startX = 0;
    let startY = 0;
    // null = intent undecided, true = pulling, false = abandoned (scroll)
    let intent: boolean | null = null;
    let pull = 0;
    let armed = false;
    let refreshing = false;
    let refreshStartedAt = 0;
    let settleTimer: number | null = null;

    // Keep the browser's native overscroll glow/bounce from fighting the
    // gesture at the top edge
    const prevOverscroll = scrollEl?.style.overscrollBehaviorY ?? "";
    if (scrollEl) {
        scrollEl.style.overscrollBehaviorY = "contain";
    }

    const setPull = (px: number) => {
        pull = px;
        root.style.setProperty("--pull", `${px.toFixed(1)}px`);
        root.style.setProperty("--pull-progress", Math.min(1, px / threshold).toFixed(3));
    };

    const clearSettleTimer = () => {
        if (settleTimer !== null) {
            clearTimeout(settleTimer);
            settleTimer = null;
        }
    };

    const settleTo = (px: number) => {
        root.classList.remove("ptr-pulling");
        root.classList.add("ptr-settling");
        setPull(px);

        clearSettleTimer();
        settleTimer = window.setTimeout(() => {
            root.classList.remove("ptr-settling");
            settleTimer = null;
        }, SETTLE_MS);
    };

    const disarm = () => {
        armed = false;
        root.classList.remove("ptr-armed");
    };

    const onTouchStart = (e: TouchEvent) => {
        if (!enabled || refreshing || e.touches.length !== 1) {
            intent = false;
            return;
        }

        // Only gestures that begin with the container at the top can pull
        if (scrollEl && scrollEl.scrollTop > 0) {
            intent = false;
            return;
        }

        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        intent = null;
    };

    const onTouchMove = (e: TouchEvent) => {
        if (intent === false || refreshing || !enabled) {
            return;
        }

        const dx = e.touches[0].clientX - startX;
        const dy = e.touches[0].clientY - startY;

        if (intent === null) {
            if (Math.abs(dx) > INTENT_SLOP && Math.abs(dx) > Math.abs(dy)) {
                intent = false; // Horizontal gesture - not ours
                return;
            }

            if (dy < -INTENT_SLOP) {
                intent = false; // Scrolling down into the list
                return;
            }

            if (dy > INTENT_SLOP && (!scrollEl || scrollEl.scrollTop <= 0)) {
                intent = true;
                clearSettleTimer();
                root.classList.remove("ptr-settling");
                root.classList.add("ptr-pulling");
            } else {
                return;
            }
        }

        // Engaged: content follows the finger with rubber-band resistance.
        // preventDefault stops the container from natively scrolling/bouncing.
        if (e.cancelable) {
            e.preventDefault();
        }

        const raw = Math.max(0, dy - INTENT_SLOP);
        const damped = maxPull * (1 - Math.exp((-0.8 * raw) / maxPull));
        setPull(damped);

        const nowArmed = damped >= threshold;
        if (nowArmed !== armed) {
            armed = nowArmed;
            root.classList.toggle("ptr-armed", armed);

            if (armed) {
                try {
                    navigator.vibrate?.(10);
                } catch {
                    // Not supported - purely a nicety
                }
            }
        }
    };

    const onTouchEnd = () => {
        if (intent !== true || refreshing) {
            intent = false;
            return;
        }

        intent = false;

        if (!armed) {
            settleTo(0);
            return;
        }

        disarm();
        refreshing = true;
        refreshStartedAt = performance.now();
        root.classList.add("ptr-refreshing");
        settleTo(threshold * 0.82); // Hold the badge in view while loading

        dotnetRef.invokeMethodAsync<void>("OnPullRefresh")
            .catch(() => service.complete()); // Circuit lost - don't spin forever
    };

    const service: PullToRefreshService = {
        setEnabled: (value: boolean) => {
            enabled = value;

            if (!value) {
                intent = false;

                if (!refreshing && pull > 0) {
                    disarm();
                    settleTo(0);
                }
            }
        },
        complete: () => {
            if (!refreshing) {
                return;
            }

            // Keep the spinner visible long enough to register
            const wait = Math.max(0, MIN_SPIN_MS - (performance.now() - refreshStartedAt));
            window.setTimeout(() => {
                refreshing = false;
                root.classList.remove("ptr-refreshing");
                settleTo(0);
            }, wait);
        },
        dispose: () => {
            clearSettleTimer();
            listenEl.removeEventListener("touchstart", onTouchStart as EventListener);
            listenEl.removeEventListener("touchmove", onTouchMove as EventListener);
            listenEl.removeEventListener("touchend", onTouchEnd as EventListener);
            listenEl.removeEventListener("touchcancel", onTouchEnd as EventListener);

            if (scrollEl) {
                scrollEl.style.overscrollBehaviorY = prevOverscroll;
            }
        }
    };

    listenEl.addEventListener("touchstart", onTouchStart as EventListener, { passive: true });
    listenEl.addEventListener("touchmove", onTouchMove as EventListener, { passive: false });
    listenEl.addEventListener("touchend", onTouchEnd as EventListener, { passive: true });
    listenEl.addEventListener("touchcancel", onTouchEnd as EventListener, { passive: true });

    return service;
};
