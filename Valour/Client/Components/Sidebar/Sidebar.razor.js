const disposers = new Set();

// Horizontal travel that claims a swipe, and how strongly it must dominate vertical travel
const lockDistance = 12;
const lockRatio = 1.5;
const ignoredTargets = 'canvas, input, textarea, select, button, [role="button"], [contenteditable]:not([contenteditable="false"]), .video-fullscreen-backdrop';

// True when the element or an ancestor can still scroll horizontally in the direction a swipe
// would move its content, so the swipe belongs to that scroller instead of the sidebar
const canScrollX = (element, fingerDirection) => {
    for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
        if (node.scrollWidth <= node.clientWidth + 1) continue;
        const overflow = getComputedStyle(node).overflowX;
        if (overflow !== 'auto' && overflow !== 'scroll') continue;
        if (fingerDirection > 0 ? node.scrollLeft > 0 : node.scrollLeft + node.clientWidth < node.scrollWidth - 1) return true;
    }
    return false;
};

export function init(ref, id) {
    const sidebar = document.getElementById(id);
    if (!sidebar) return null;
    let open = false;
    let gesture = null;
    let disposed = false;

    const setSidebarOpen = (value) => {
        if (disposed) return;
        const wasOpen = open;
        open = value === true;
        sidebar.style.transition = '';
        sidebar.style.transform = open ? 'translateX(0px)' : '';
        sidebar.dataset.mobileOpen = String(open);
        document.querySelector('.sidebar-toggle')?.setAttribute('aria-expanded', String(open));
        if (open && !wasOpen) {
            ref.invokeMethodAsync('OnMobileSidebarOpened').catch(() => {});
        }
    };
    const toggleOpen = () => setSidebarOpen(!open);
    const cancelGesture = () => {
        gesture = null;
        setSidebarOpen(open);
    };
    const onTouchStart = (event) => {
        if (gesture) cancelGesture();
        const target = event.target;
        if (event.touches.length !== 1 || !target?.closest || !sidebar.closest('.mobile')) return;
        const touch = event.touches[0];
        const width = sidebar.getBoundingClientRect().width;
        if (open) {
            if (touch.clientX < width - 80 || target.closest(ignoredTargets) || canScrollX(target, -1)) return;
        } else {
            // Opening starts anywhere in the main layout, which excludes modals, menus and popups above it
            const layout = sidebar.closest('.mainrow') ?? document.body;
            if (!layout.contains(target) || target.closest(ignoredTargets) || canScrollX(target, 1)) return;
        }
        gesture = { id: touch.identifier, x: touch.clientX, y: touch.clientY, width, offset: open ? 0 : -width, dragging: false };
    };
    const onTouchMove = (event) => {
        if (!gesture) return;
        if (event.touches.length !== 1) return cancelGesture();
        const touch = [...event.touches].find(item => item.identifier === gesture.id);
        if (!touch) return cancelGesture();
        const dx = touch.clientX - gesture.x, dy = touch.clientY - gesture.y;
        if (!gesture.dragging) {
            const distanceX = Math.abs(dx), distanceY = Math.abs(dy);
            if (distanceX <= lockDistance && distanceY <= lockDistance) return;
            const towardToggle = open ? dx < 0 : dx > 0;
            if (event.defaultPrevented || !towardToggle || distanceX < distanceY * lockRatio) return cancelGesture();
            gesture.dragging = true;
            sidebar.style.transition = 'none';
        }
        if (event.cancelable) event.preventDefault();
        const offset = Math.max(-gesture.width, Math.min(0, gesture.offset + dx));
        sidebar.style.transform = `translateX(${offset}px)`;
    };
    const onTouchEnd = (event) => {
        if (!gesture) return;
        const touch = [...event.changedTouches].find(item => item.identifier === gesture.id);
        if (!touch) return;
        const dx = touch.clientX - gesture.x;
        const dragged = gesture.dragging;
        const nextOpen = dragged && Math.abs(dx) >= Math.min(80, gesture.width * 0.2) ? dx > 0 : open;
        gesture = null;
        // A finished swipe must not also produce a click on whatever was under the finger
        if (dragged && event.cancelable) event.preventDefault();
        setSidebarOpen(nextOpen);
    };
    const clearInlineTransform = () => {
        if (disposed) return;
        gesture = null;
        sidebar.style.transform = '';
    };
    const dispose = () => {
        if (disposed) return;
        disposed = true;
        open = false;
        gesture = null;
        window.removeEventListener('touchstart', onTouchStart);
        window.removeEventListener('touchmove', onTouchMove);
        window.removeEventListener('touchend', onTouchEnd);
        window.removeEventListener('touchcancel', cancelGesture);
        window.removeEventListener('resize', cancelGesture);
        if (window.toggleSidebar === toggleOpen) delete window.toggleSidebar;
        if (window.setSidebarOpen === setSidebarOpen) delete window.setSidebarOpen;
        disposers.delete(dispose);
    };

    window.addEventListener('touchstart', onTouchStart, { passive: true });
    window.addEventListener('touchmove', onTouchMove, { passive: false });
    window.addEventListener('touchend', onTouchEnd, { passive: false });
    window.addEventListener('touchcancel', cancelGesture);
    window.addEventListener('resize', cancelGesture);
    window.toggleSidebar = toggleOpen;
    window.setSidebarOpen = setSidebarOpen;
    sidebar.dataset.mobileOpen = 'false';
    disposers.add(dispose);
    return { toggleOpen, clearInlineTransform, isOpen: () => open, setOpen: setSidebarOpen, dispose };
}

export function cleanup() {
    for (const dispose of [...disposers]) dispose();
}
