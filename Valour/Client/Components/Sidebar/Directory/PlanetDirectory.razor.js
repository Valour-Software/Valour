const itemSelector = '[data-channel-drag-id]';
const interactiveSelector = 'button, a, input, select, textarea, [contenteditable="true"]';
const dragDistance = 8;
const touchHoldDelay = 275;

export function initializeChannelPointerDrag(rootId, dotnet, enableMouseFallback) {
    const root = document.getElementById(rootId);
    if (!root) {
        return { dispose() {} };
    }

    let candidate = null;
    let target = null;
    let targetZone = null;
    let active = false;
    let touchArmed = false;
    let holdTimer = null;
    let suppressClickUntil = 0;

    const clearTarget = () => {
        if (!target) {
            return;
        }

        target.classList.remove(
            'pointer-drag-before',
            'pointer-drag-after',
            'pointer-drag-inside');
        target = null;
        targetZone = null;
    };

    const cleanup = () => {
        if (holdTimer !== null) {
            clearTimeout(holdTimer);
            holdTimer = null;
        }

        candidate?.element.classList.remove('pointer-drag-source');
        clearTarget();
        root.classList.remove('pointer-channel-dragging');
        candidate = null;
        active = false;
        touchArmed = false;
    };

    const beginCandidate = (element, clientX, clientY, isTouch) => {
        candidate = {
            element,
            id: element.dataset.channelDragId,
            startX: clientX,
            startY: clientY,
            isTouch
        };

        if (isTouch) {
            holdTimer = setTimeout(() => {
                touchArmed = candidate !== null;
                holdTimer = null;
            }, touchHoldDelay);
        }
    };

    const updateTarget = (clientX, clientY) => {
        const hit = document.elementFromPoint(clientX, clientY)?.closest(itemSelector);
        if (!hit || !root.contains(hit) || hit === candidate.element) {
            clearTarget();
            return;
        }

        const row = hit.querySelector(':scope > .channel-wrapper') ?? hit;
        const rect = row.getBoundingClientRect();
        const ratio = rect.height > 0 ? (clientY - rect.top) / rect.height : 0.5;
        const isCategory = hit.dataset.channelDragCategory === 'true';
        const zone = isCategory
            ? (ratio < 0.25 ? 'before' : ratio > 0.75 ? 'after' : 'inside')
            : (ratio < 0.5 ? 'before' : 'after');

        if (target === hit && targetZone === zone) {
            return;
        }

        clearTarget();
        target = hit;
        targetZone = zone;
        target.classList.add(`pointer-drag-${zone}`);
    };

    const move = (clientX, clientY, event) => {
        if (!candidate) {
            return;
        }

        const distance = Math.hypot(
            clientX - candidate.startX,
            clientY - candidate.startY);

        if (!active) {
            if (candidate.isTouch && !touchArmed) {
                if (distance >= dragDistance) {
                    cleanup();
                }
                return;
            }

            if (distance < dragDistance) {
                return;
            }

            active = true;
            candidate.element.classList.add('pointer-drag-source');
            root.classList.add('pointer-channel-dragging');
        }

        event.preventDefault();
        updateTarget(clientX, clientY);
    };

    const finish = (event) => {
        if (!candidate) {
            return;
        }

        const sourceId = candidate.id;
        const destinationId = target?.dataset.channelDragId;
        const zone = targetZone;
        const didDrag = active;

        if (didDrag) {
            event.preventDefault();
            event.stopPropagation();
            suppressClickUntil = performance.now() + 500;
        }

        cleanup();

        if (didDrag && destinationId && zone) {
            void dotnet.invokeMethodAsync(
                'DropChannelFromPointer',
                sourceId,
                destinationId,
                zone);
        }
    };

    const onTouchStart = (event) => {
        if (event.touches.length !== 1 || event.target.closest(interactiveSelector)) {
            return;
        }

        const element = event.target.closest(itemSelector);
        if (!element || !root.contains(element)) {
            return;
        }

        const touch = event.touches[0];
        beginCandidate(element, touch.clientX, touch.clientY, true);
    };

    const onTouchMove = (event) => {
        if (!candidate || event.touches.length !== 1) {
            return;
        }

        const touch = event.touches[0];
        move(touch.clientX, touch.clientY, event);
    };

    const onMouseDown = (event) => {
        if (!enableMouseFallback || event.button !== 0 || event.target.closest(interactiveSelector)) {
            return;
        }

        const element = event.target.closest(itemSelector);
        if (element && root.contains(element)) {
            beginCandidate(element, event.clientX, event.clientY, false);
        }
    };

    const onMouseMove = (event) => move(event.clientX, event.clientY, event);
    const onClickCapture = (event) => {
        if (performance.now() < suppressClickUntil) {
            event.preventDefault();
            event.stopPropagation();
        }
    };

    root.addEventListener('touchstart', onTouchStart, { passive: true });
    document.addEventListener('touchmove', onTouchMove, { passive: false });
    document.addEventListener('touchend', finish, { passive: false });
    document.addEventListener('touchcancel', cleanup);
    root.addEventListener('mousedown', onMouseDown);
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', finish);
    root.addEventListener('click', onClickCapture, true);

    return {
        dispose() {
            cleanup();
            root.removeEventListener('touchstart', onTouchStart);
            document.removeEventListener('touchmove', onTouchMove);
            document.removeEventListener('touchend', finish);
            document.removeEventListener('touchcancel', cleanup);
            root.removeEventListener('mousedown', onMouseDown);
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', finish);
            root.removeEventListener('click', onClickCapture, true);
        }
    };
}
