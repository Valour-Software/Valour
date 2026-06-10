import DotnetObject = DotNet.DotnetObject;

type TouchSlideService = {
    dispose: () => void;
}

/**
 * Reusable touch swipe-action system.
 *
 * Attaches a single delegated listener set to a container; any descendant
 * matching itemSelector can be swiped left to reveal an action icon. If
 * released past the threshold, 'OnSlideAction' is invoked on the .NET ref
 * with the item's element id.
 *
 * Touch-only by nature (touch events never fire for mouse input).
 * Vertical scrolling is preserved: the slide only engages once the gesture
 * is clearly horizontal, and we never call preventDefault.
 */
export const init = (
    container: HTMLElement,
    itemSelector: string,
    dotnetRef: DotnetObject,
    threshold: number = 56,
    maxSlide: number = 96,
    iconHtml: string = '<i class="bi bi-reply-fill"></i>'
): TouchSlideService => {

    let item: HTMLElement | null = null;
    let iconEl: HTMLElement | null = null;
    let startX = 0;
    let startY = 0;
    // null = intent undecided, true = horizontal slide, false = abandoned (scroll)
    let locked: boolean | null = null;
    let armed = false;

    const beginSlide = () => {
        if (!item) return;
        item.classList.add('touch-sliding');
        item.style.transition = 'none';

        iconEl = document.createElement('div');
        iconEl.className = 'touch-slide-icon';
        iconEl.innerHTML = iconHtml;
        item.appendChild(iconEl);
    };

    const settle = () => {
        if (!item) return;
        const el = item;
        const icon = iconEl;

        el.style.transition = 'transform 200ms cubic-bezier(0.16, 1, 0.3, 1)';
        el.style.transform = '';
        if (icon) icon.style.opacity = '0';

        window.setTimeout(() => {
            el.style.transition = '';
            el.classList.remove('touch-sliding');
            icon?.remove();
        }, 220);

        item = null;
        iconEl = null;
        locked = null;
        armed = false;
    };

    const onTouchStart = (e: TouchEvent) => {
        if (e.touches.length !== 1) return;
        const target = (e.target as HTMLElement).closest(itemSelector) as HTMLElement | null;
        if (!target || !container.contains(target)) return;

        item = target;
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        locked = null;
        armed = false;
    };

    const onTouchMove = (e: TouchEvent) => {
        if (!item) return;

        const t = e.touches[0];
        const dx = t.clientX - startX;
        const dy = t.clientY - startY;

        if (locked === null) {
            // Wait for clear intent before doing anything
            if (Math.abs(dx) < 10 && Math.abs(dy) < 10) return;

            locked = Math.abs(dx) > Math.abs(dy) && dx < 0;
            if (!locked) {
                // Vertical scroll or right swipe — leave it to the browser
                item = null;
                return;
            }
            beginSlide();
        }

        // Clamp with resistance past the threshold
        const abs = Math.min(Math.max(0, -dx), maxSlide);
        const eased = abs <= threshold
            ? abs
            : threshold + (abs - threshold) * 0.35;

        item.style.transform = `translateX(${-eased}px)`;

        armed = abs >= threshold;
        if (iconEl) {
            const progress = Math.min(1, abs / threshold);
            iconEl.style.opacity = String(progress);
            iconEl.style.transform = `scale(${0.6 + 0.4 * progress})`;
            iconEl.classList.toggle('armed', armed);
        }
    };

    const onTouchEnd = () => {
        if (!item) return;

        if (locked && armed) {
            void dotnetRef.invokeMethodAsync('OnSlideAction', item.id);
        }

        settle();
    };

    container.addEventListener('touchstart', onTouchStart, { passive: true });
    container.addEventListener('touchmove', onTouchMove, { passive: true });
    container.addEventListener('touchend', onTouchEnd, { passive: true });
    container.addEventListener('touchcancel', onTouchEnd, { passive: true });

    return {
        dispose: () => {
            container.removeEventListener('touchstart', onTouchStart);
            container.removeEventListener('touchmove', onTouchMove);
            container.removeEventListener('touchend', onTouchEnd);
            container.removeEventListener('touchcancel', onTouchEnd);
        }
    };
}
